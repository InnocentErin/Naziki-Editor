using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.IO;
using System.Threading.Channels;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.State;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Features.Preview;

public sealed class UnityStoryboardPreviewHost :
    IStoryboardPreviewHost,
    IPreviewPlaybackController,
    IPreviewDiagnosticsService,
    IUnityPreviewSessionService,
    IDisposable
{
    private readonly IUnityPreviewTransport _transport;
    private readonly IUnityPreviewProcessService _process;
    private readonly IPreviewVfsMaterializer _vfs;
    private readonly IPreviewValidationService _validator;
    private readonly IPreviewSettingsProvider _settings;
    private readonly IErrorHandler _errorHandler;
    private readonly object _sync = new();
    private readonly object _restartSync = new();
    private readonly ConcurrentDictionary<string, PendingVersion> _pendingVersions = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PreviewProtocolMessage>> _pendingCommands = new();
    private readonly ConcurrentQueue<PreviewDiagnostic> _activeLoadWarnings = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _teardownGate = new(1, 1);
    private readonly Channel<CoordinatorWork> _coordinatorQueue;
    private readonly CancellationTokenSource _coordinatorLifetime = new();
    private readonly Task _coordinatorLoop;
    private readonly PreviewProtocolTrace _protocolTrace = new();
    private IDisposable? _changeSubscription;
    private IStoryboardPreviewDataSource? _dataSource;
    private ProjectDataContext? _context;
    private IntPtr _parentWindow;
    private int _pixelWidth = 1;
    private int _pixelHeight = 1;
    private string _authenticationNonce = Guid.NewGuid().ToString("N");
    private string _connectionId = "unbound";
    private string _transportSessionId = "unbound";
    private string _sessionId = "unbound";
    private CancellationTokenSource? _startCancellation;
    private TaskCompletionSource<bool>? _hostReadyCompletion;
    private Timer? _healthTimer;
    private int _healthCheckInFlight;
    private int _recoveryInFlight;
    private int _shuttingDown;
    private long _generation;
    private int _hostRevision;
    private DateTimeOffset _phaseStartedAt = DateTimeOffset.Now;
    private DateTimeOffset _lastMessageAt = DateTimeOffset.Now;
    private DateTimeOffset _lastHeartbeatAt = DateTimeOffset.MinValue;
    private DateTimeOffset _loadStartedAt = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastLoadProgressAt;
    private DateTimeOffset? _healthySince;
    private int _heartbeatFailureCount;
    private int _activeLoadStage = -1;
    private PreviewSessionPhase _phase = PreviewSessionPhase.Idle;
    private string? _activeRequestId;
    private long? _activeSnapshotVersion;
    private string? _phaseDetail;
    private long _previewVersion;
    private bool _hostReady;
    private bool _changeInFlight;
    private StoryboardPreviewChangeSet? _queuedChanges;
    private StoryboardPreviewSnapshot? _queuedSnapshot;
    private double? _pendingScrubTime;
    private Timer? _scrubTimer;
    private double? _pendingExternalClockTime;
    private Timer? _externalClockTimer;
    private int _externalClockTickInFlight;
    private double _currentTime;
    private double _duration;
    private PreviewPlaybackState _state = PreviewPlaybackState.Stopped;
    private PreviewClockMode _clockMode = PreviewClockMode.Internal;
    private PreviewPlaybackState _stateBeforeScrub = PreviewPlaybackState.Stopped;
    private PreviewAvailabilityState _availability = PreviewAvailabilityState.Disconnected;
    private IReadOnlyList<PreviewDiagnostic> _diagnostics = [];
    private LastKnownGoodPreview? _lastKnownGood;
    private PreviewPerformanceSample? _performance;
    private int _automaticRestartCount;
    private PreviewSettings _lastAppliedSettings;
    private PreviewPlaybackRestorePoint? _pendingRestore;
    private PreviewConnectionContext? _connectionContext;
    private PreviewTransportFault? _lastTransportFault;
    private PreviewDiagnostic? _rootFailureDiagnostic;
    private Task? _restartTask;

    public UnityStoryboardPreviewHost(
        IUnityPreviewTransport transport,
        IUnityPreviewProcessService process,
        IPreviewVfsMaterializer vfs,
        IPreviewValidationService validator,
        IPreviewSettingsProvider settings,
        IErrorHandler errorHandler)
    {
        _transport = transport;
        _process = process;
        _vfs = vfs;
        _validator = validator;
        _settings = settings;
        _errorHandler = errorHandler;
        _lastAppliedSettings = settings.Current;
        _coordinatorQueue = Channel.CreateUnbounded<CoordinatorWork>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        _coordinatorLoop = RunCoordinatorLoopAsync(_coordinatorLifetime.Token);
        _transport.MessageReceived += OnMessageReceived;
        _transport.ConnectionChanged += OnConnectionChanged;
        _transport.Faulted += OnTransportFaulted;
        _process.Exited += OnProcessExited;
        _settings.Changed += OnSettingsChanged;
    }

    public UnityStoryboardPreviewHost(
        IUnityPreviewTransport transport,
        IUnityPreviewProcessService process,
        IPreviewVfsMaterializer vfs,
        IPreviewValidationService validator,
        IPreviewSettingsProvider settings)
        : this(transport, process, vfs, validator, settings, new SilentErrorHandler())
    {
    }

    public bool IsAvailable =>
        _availability is PreviewAvailabilityState.Ready or PreviewAvailabilityState.ReadyWithWarnings &&
        _hostReady;
    public double CurrentTime => Volatile.Read(ref _currentTime);
    public double Duration => Volatile.Read(ref _duration);
    public PreviewPlaybackState State => _state;
    public PreviewAvailabilityState Availability => _availability;
    public PreviewSessionStatus SessionStatus => new(
        Volatile.Read(ref _generation),
        _phase,
        _phaseStartedAt,
        _lastMessageAt,
        _process.ProcessId,
        _transport.IsConnected,
        _hostRevision,
        _activeRequestId,
        _activeSnapshotVersion,
        _phaseDetail)
    {
        ConnectionState = GetConnectionState(),
        ContentState = GetContentState()
    };
    public IReadOnlyList<PreviewDiagnostic> Diagnostics => _diagnostics;
    public PreviewDiagnosticSummary Summary
    {
        get
        {
            var snapshot = _diagnostics;
            return new PreviewDiagnosticSummary(
                snapshot.Count(item => item.Severity == PreviewDiagnosticSeverity.Error),
                snapshot.Count(item => item.Severity == PreviewDiagnosticSeverity.Warning),
                snapshot.FirstOrDefault(item => item.Severity == PreviewDiagnosticSeverity.Error)
                    ?? snapshot.FirstOrDefault());
        }
    }
    public LastKnownGoodPreview? LastKnownGood => _lastKnownGood;
    public PreviewPerformanceSample? Performance => _performance;

    public event EventHandler<double>? TimeChanged;
    public event EventHandler<PreviewPlaybackState>? StateChanged;
    public event EventHandler? Changed;

    public void Attach(IStoryboardPreviewDataSource dataSource, IStoryboardChangeFeed changeFeed)
    {
        _dataSource = dataSource;
        _changeSubscription?.Dispose();
        _changeSubscription = changeFeed.Subscribe(ApplyChanges);
    }

    public void Detach()
    {
        _changeSubscription?.Dispose();
        _changeSubscription = null;
        _context = null;
        _queuedChanges = null;
        _pendingVersions.Clear();
    }

    public async Task AttachWindowAsync(IntPtr parentWindow, int pixelWidth, int pixelHeight)
    {
        _parentWindow = parentWindow;
        _pixelWidth = Math.Max(1, pixelWidth);
        _pixelHeight = Math.Max(1, pixelHeight);
        if (_process.IsRunning)
            await _process.ReparentAsync(parentWindow).ConfigureAwait(false);
        if (!_settings.Current.HardwareAcceleration)
        {
            SetDiagnostics(
                PreviewAvailabilityState.Disabled,
                [new PreviewDiagnostic(
                    "PREVIEW_GPU_DISABLED",
                    "原生预览需要启用硬件加速。可在“设置 → 性能设置”中启用。",
                    PreviewDiagnosticSeverity.Warning,
                    PreviewDiagnosticSource.Editor)]);
            return;
        }
        if (_context is null)
            return;
        await EnsureStartedAsync().ConfigureAwait(false);
    }

    public async Task ResizeAsync(int pixelWidth, int pixelHeight, bool active)
    {
        _pixelWidth = Math.Max(1, pixelWidth);
        _pixelHeight = Math.Max(1, pixelHeight);
        if (!IsAvailable)
            return;
        await SendCommandAsync("preview.settings.apply", new JObject
        {
            ["settings"] = JObject.FromObject(_settings.Current),
            ["pixelWidth"] = _pixelWidth,
            ["pixelHeight"] = _pixelHeight,
            ["active"] = active
        }).ConfigureAwait(false);
    }

    public async Task OpenProjectAsync(ProjectDataContext context, double playbackTime = 0)
    {
        _context = context;
        playbackTime = Math.Max(0, playbackTime);
        Volatile.Write(ref _currentTime, playbackTime);
        SetState(PreviewPlaybackState.Paused);
        _pendingRestore = new PreviewPlaybackRestorePoint(
            playbackTime,
            PreviewPlaybackState.Paused,
            _clockMode,
            _previewVersion);
        if (_dataSource is null)
            throw new InvalidOperationException("Preview data source has not been attached.");
        var snapshot = _dataSource.GetSnapshot(context, playbackTime);
        var sessionChanged = !string.Equals(_sessionId, "unbound", StringComparison.Ordinal) &&
                             !string.Equals(_sessionId, snapshot.SessionId, StringComparison.Ordinal);
        _sessionId = snapshot.SessionId;
        if (sessionChanged)
            await RestartForNewSessionAsync().ConfigureAwait(false);
        lock (_sync)
        {
            if (_changeInFlight)
            {
                _queuedSnapshot = snapshot;
                _queuedChanges = null;
                return;
            }
            _changeInFlight = true;
        }
        await PrepareAndSendSnapshotAsync(snapshot, "preview.open").ConfigureAwait(false);
    }

    public async Task RetryAsync()
    {
        _automaticRestartCount = 0;
        await RestartAsync().ConfigureAwait(false);
    }

    public async Task RestartPlayerAsync()
    {
        await _reloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _automaticRestartCount = 0;
            _pendingRestore = CaptureRestorePoint();
            Pause();
            await RestartAsync().ConfigureAwait(false);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public async Task ReloadLevelAsync(ProjectDataContext context, double playbackTime)
    {
        await _reloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _context = context;
            if (_dataSource is null)
                throw new InvalidOperationException("Preview data source has not been attached.");

            _pendingRestore = CaptureRestorePoint() with { Time = Math.Max(0, playbackTime) };
            Pause();
            var snapshot = _dataSource.GetSnapshot(context, _pendingRestore.Time);
            lock (_sync)
            {
                _queuedSnapshot = null;
                _queuedChanges = null;
                if (_changeInFlight)
                {
                    _queuedSnapshot = snapshot;
                    return;
                }
                _changeInFlight = true;
            }
            await PrepareAndSendSnapshotAsync(snapshot, "preview.open").ConfigureAwait(false);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public PreviewPlaybackRestorePoint CaptureRestorePoint() =>
        new(CurrentTime, State, _clockMode, _previewVersion);

    public async Task RefreshViewportAsync(
        string aspectRatio,
        int pixelWidth,
        int pixelHeight)
    {
        await _reloadGate.WaitAsync().ConfigureAwait(false);
        var previousWidth = _pixelWidth;
        var previousHeight = _pixelHeight;
        var restore = CaptureRestorePoint();
        try
        {
            Pause();
            _pixelWidth = Math.Max(1, pixelWidth);
            _pixelHeight = Math.Max(1, pixelHeight);
            if (!IsAvailable)
                return;
            await SendCommandAndWaitAsync("preview.viewport.apply", new JObject
            {
                ["aspectRatio"] = PreviewSettingsProvider.ParseAspectRatio(aspectRatio),
                ["pixelWidth"] = _pixelWidth,
                ["pixelHeight"] = _pixelHeight,
                ["time"] = restore.Time,
                ["settings"] = JObject.FromObject(_settings.Current)
            }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            RestorePlayback(restore);
        }
        catch
        {
            _pixelWidth = previousWidth;
            _pixelHeight = previousHeight;
            RestorePlayback(restore);
            throw;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) != 0)
            return;
        _startCancellation?.Cancel();
        _healthTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _scrubTimer?.Dispose();
        _scrubTimer = null;
        _externalClockTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        lock (_sync)
            _pendingExternalClockTime = null;
        if (_transport.IsConnected)
        {
            try
            {
                using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
                await _transport.SendAsync(new PreviewProtocolMessage(
                    "host.shutdown",
                    GetSessionId(),
                    Guid.NewGuid().ToString("N"),
                    _dataSource?.CurrentVersion ?? 0,
                    _previewVersion,
                    _previewVersion,
                    new JObject())
                {
                    ConnectionId = _connectionId,
                    Generation = _generation
                }, shutdownTimeout.Token).ConfigureAwait(false);
            }
            catch { }
        }
        await TearDownConnectionAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        _rootFailureDiagnostic = null;
        SetDiagnostics(PreviewAvailabilityState.Disconnected, []);
    }

    public void ApplySnapshot(StoryboardPreviewSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_changeInFlight)
            {
                _queuedSnapshot = snapshot;
                _queuedChanges = null;
                return;
            }
            _changeInFlight = true;
        }
        _ = PrepareAndSendSnapshotAsync(snapshot, "preview.replaceSnapshot");
    }

    public void ApplyChanges(StoryboardPreviewChangeSet changes)
    {
        if (changes.Kind == StoryboardPreviewChangeKind.SessionEnded)
        {
            Pause();
            _context = null;
            return;
        }
        var context = _context;
        var dataSource = _dataSource;
        if (context is null || dataSource is null)
            return;

        lock (_sync)
        {
            if (_changeInFlight)
            {
                _queuedChanges = changes;
                return;
            }
            _changeInFlight = true;
        }
        _ = PrepareAndSendChangesAsync(context, dataSource.GetSnapshot(context, CurrentTime), changes);
    }

    public void Seek(double seconds)
    {
        seconds = Math.Max(0, seconds);
        Volatile.Write(ref _currentTime, seconds);
        _ = SendCommandIfAvailableAsync("preview.seek", new JObject { ["time"] = seconds });
    }

    public void SetPlaybackState(PreviewPlaybackState state)
    {
        switch (state)
        {
            case PreviewPlaybackState.Playing: Play(); break;
            case PreviewPlaybackState.Paused: Pause(); break;
            default: Stop(); break;
        }
    }

    public void Play()
    {
        SetState(PreviewPlaybackState.Playing);
        _ = SendCommandIfAvailableAsync("preview.play", new JObject());
    }

    public void Pause()
    {
        SetState(PreviewPlaybackState.Paused);
        _ = SendCommandIfAvailableAsync("preview.pause", new JObject());
    }

    public void Stop()
    {
        SetState(PreviewPlaybackState.Stopped);
        Volatile.Write(ref _currentTime, 0);
        _ = SendCommandIfAvailableAsync("preview.stop", new JObject());
    }

    public void BeginScrub(double seconds)
    {
        seconds = Math.Max(0, seconds);
        _stateBeforeScrub = State;
        Volatile.Write(ref _currentTime, seconds);
        _pendingScrubTime = seconds;
        EnsureScrubTimer();
        _ = SendCommandIfAvailableAsync("preview.scrub.begin", new JObject { ["time"] = seconds });
    }

    public void UpdateScrub(double seconds)
    {
        Volatile.Write(ref _currentTime, Math.Max(0, seconds));
        lock (_sync)
            _pendingScrubTime = CurrentTime;
    }

    public void CommitScrub(double seconds)
    {
        seconds = Math.Max(0, seconds);
        Volatile.Write(ref _currentTime, seconds);
        lock (_sync)
            _pendingScrubTime = null;
        _ = SendCommandIfAvailableAsync("preview.scrub.commit", new JObject
        {
            ["time"] = seconds,
            ["resumeState"] = _stateBeforeScrub.ToString()
        });
    }

    public void SetClockMode(PreviewClockMode mode)
    {
        _clockMode = mode;
        if (mode == PreviewClockMode.External)
            EnsureExternalClockTimer();
        else
        {
            _externalClockTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            lock (_sync)
                _pendingExternalClockTime = null;
        }
        _ = SendCommandIfAvailableAsync(
            "preview.clock.set",
            new JObject
            {
                ["mode"] = mode == PreviewClockMode.External ? "external" : "internal"
            });
    }

    public void SetExternalTime(double seconds)
    {
        seconds = Math.Max(0, seconds);
        Volatile.Write(ref _currentTime, seconds);
        lock (_sync)
            _pendingExternalClockTime = seconds;
        EnsureExternalClockTimer();
    }

    private void TraceMessage(PreviewTraceDirection direction, PreviewProtocolMessage message) =>
        _protocolTrace.Record(new PreviewProtocolTraceEntry(
            DateTimeOffset.Now,
            direction,
            message.Generation,
            message.ConnectionId,
            _phase,
            message.Type,
            message.RequestId,
            message.EditorVersion,
            message.BasePreviewVersion,
            message.TargetPreviewVersion));

    private void TraceLifecycle(string type, string? detail = null) =>
        _protocolTrace.Record(new PreviewProtocolTraceEntry(
            DateTimeOffset.Now,
            PreviewTraceDirection.Lifecycle,
            Volatile.Read(ref _generation),
            _connectionId,
            _phase,
            type,
            null,
            _dataSource?.CurrentVersion ?? 0,
            _previewVersion,
            _previewVersion,
            detail));

    private async Task TearDownConnectionAsync(TimeSpan gracefulTimeout)
    {
        await _teardownGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var context = _connectionContext;
            CancelPendingOperations("Unity Preview connection generation ended.");
            _hostReadyCompletion?.TrySetException(
                new OperationCanceledException("Unity Preview connection generation ended."));
            if (context is not null)
            {
                TraceLifecycle("connection.expected-stop",
                    $"transportGeneration={context.TransportGeneration}");
                if (!context.Lifetime.IsCancellationRequested)
                    context.Lifetime.Cancel();
            }
            _hostReady = false;

            Exception? cleanupFailure = null;
            try
            {
                await _process.StopAsync(gracefulTimeout).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
            }
            try
            {
                await _transport.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cleanupFailure = cleanupFailure is null
                    ? ex
                    : new AggregateException(cleanupFailure, ex);
            }
            if (ReferenceEquals(_connectionContext, context))
            {
                _connectionContext = null;
                context?.Dispose();
            }
            if (cleanupFailure is not null)
            {
                var diagnostic = new PreviewDiagnostic(
                    "PREVIEW_CLEANUP_FAILED",
                    $"Unity Preview 连接清理失败：{cleanupFailure.Message}",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Editor,
                    _protocolTrace.FilePath,
                    Suggestion: "请结束残留 Unity Preview 进程后手动重试。")
                {
                    StackTrace = cleanupFailure.ToString()
                };
                SetDiagnosticsPreservingRoot(
                    PreviewAvailabilityState.Faulted,
                    diagnostic);
                TraceLifecycle("connection.cleanup.failed", cleanupFailure.Message);
            }
        }
        finally
        {
            _teardownGate.Release();
        }
    }

    private void CancelPendingOperations(string reason)
    {
        var failure = new OperationCanceledException(reason);
        foreach (var item in _pendingVersions.ToArray())
            if (_pendingVersions.TryRemove(item.Key, out var pending))
                pending.Completion.TrySetException(failure);
        foreach (var item in _pendingCommands.ToArray())
            if (_pendingCommands.TryRemove(item.Key, out var pending))
                pending.TrySetException(failure);
        _loadStartedAt = DateTimeOffset.MinValue;
        _lastLoadProgressAt = null;
        _activeLoadStage = -1;
    }

    private async Task EnsureStartedAsync()
    {
        await _startGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_process.IsRunning && _transport.IsConnected && _hostReady)
                return;
            if (_parentWindow == IntPtr.Zero)
                return;

            // Process and pipe are a single connection generation. If only one
            // survived, neither half can safely reconnect in place.
            if (_process.IsRunning || _transport.IsConnected)
            {
                await TearDownConnectionAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                if (_process.IsRunning || _transport.IsConnected)
                    throw new InvalidOperationException(
                        "Unity Preview cleanup left a running process or connected pipe.");
            }

            var generation = Interlocked.Increment(ref _generation);
            _connectionId = Guid.NewGuid().ToString("N");
            _hostReady = false;
            _hostRevision = 0;
            _hostReadyCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TransitionTo(PreviewSessionPhase.LaunchingProcess);
            SetConnectionProgressDiagnostics(PreviewAvailabilityState.Starting);
            _startCancellation?.Cancel();
            _startCancellation?.Dispose();
            _startCancellation = new CancellationTokenSource();
            var token = _startCancellation.Token;
            _authenticationNonce = Guid.NewGuid().ToString("N");
            _transportSessionId = Guid.NewGuid().ToString("N");
            _lastHeartbeatAt = DateTimeOffset.MinValue;
            _heartbeatFailureCount = 0;
            _healthySince = null;
            var connectionTask = _transport.StartAsync(token);
            _connectionContext?.Dispose();
            _connectionContext = new PreviewConnectionContext(
                _connectionId,
                generation,
                _transport.Generation,
                _transportSessionId,
                _authenticationNonce,
                new CancellationTokenSource());
            _lastTransportFault = null;
            TraceLifecycle("connection.generation.started",
                $"transportGeneration={_transport.Generation}");
            TransitionTo(PreviewSessionPhase.InitializingGraphics);
            await _process.StartAsync(new UnityPreviewLaunchOptions(
                _parentWindow,
                _connectionId,
                generation,
                _transportSessionId,
                _transport.PipeName,
                _authenticationNonce,
                _pixelWidth,
                _pixelHeight,
                _settings.Current.RenderThreads), token)
                .WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _generation))
                throw new OperationCanceledException("Preview generation was superseded.");
            TransitionTo(PreviewSessionPhase.ConnectingTransport);
            SetConnectionProgressDiagnostics(PreviewAvailabilityState.Connecting);
            await connectionTask.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
            TransitionTo(PreviewSessionPhase.AuthenticatingHost);
            await _hostReadyCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            if (_connectionContext is not { } activeContext ||
                activeContext.HostGeneration != generation ||
                activeContext.Lifetime.IsCancellationRequested)
                throw new OperationCanceledException("Preview handshake belongs to a retired connection generation.");
            EnsureHealthTimer();
        }
        catch (FileNotFoundException ex)
        {
            await TearDownConnectionAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            SetDiagnostics(PreviewAvailabilityState.RuntimeMissing,
                [new PreviewDiagnostic(
                    "PREVIEW_RUNTIME_MISSING",
                    ex.Message,
                    PreviewDiagnosticSeverity.Warning,
                    PreviewDiagnosticSource.Editor,
                    ex.FileName,
                    Suggestion: "使用 External/original_player 的 Windows Editor Preview 构建入口生成 Runtime/OriginalPlayer。")]);
        }
        catch (Exception ex)
        {
            var handshakeFailure = _diagnostics.Any(item => item.Code is
                    "PREVIEW_AUTH_FAILED" or "PREVIEW_RUNTIME_OUTDATED") ||
                ex is InvalidDataException &&
                (ex.Message.Contains("authentication failed", StringComparison.Ordinal) ||
                 ex.Message.Contains("host revision or capabilities", StringComparison.Ordinal));
            if (handshakeFailure)
            {
                await TearDownConnectionAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                TransitionTo(PreviewSessionPhase.Failed);
                return;
            }
            var startFailureCode = !_process.IsRunning
                ? "PREVIEW_LAUNCH_FAILED"
                : !_process.IsGraphicsReady
                    ? "PREVIEW_GRAPHICS_FAILED"
                    : !_transport.IsConnected
                        ? "PREVIEW_CONNECTION_FAILED"
                        : "PREVIEW_HOST_READY_TIMEOUT";
            var startDiagnostic = _diagnostics.FirstOrDefault(item =>
                                      item.Code == "PREVIEW_HANDSHAKE_SEND_FAILED")
                                  ?? new PreviewDiagnostic(
                                      startFailureCode,
                                      startFailureCode == "PREVIEW_HOST_READY_TIMEOUT"
                                          ? $"Unity Preview 管道已连接，但未在 5 秒内完成协议握手：{ex.Message}"
                                          : $"Unity Preview 启动失败：{ex.Message}",
                                      PreviewDiagnosticSeverity.Error,
                                      PreviewDiagnosticSource.Transport,
                                      _protocolTrace.FilePath,
                                      Suggestion: "请查看协议日志中的 host.hello、host.accept 和 host.ready 顺序。")
                                  {
                                      StackTrace = ex + Environment.NewLine + _protocolTrace.DescribeRecent()
                                  };
            await TearDownConnectionAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            TransitionTo(PreviewSessionPhase.Failed);
            SetDiagnosticsPreservingRoot(PreviewAvailabilityState.Faulted,
                [startDiagnostic]);
            ScheduleAutomaticRecovery();
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task PrepareAndSendSnapshotAsync(StoryboardPreviewSnapshot snapshot, string command)
    {
        var commandSent = false;
        try
        {
            var context = _context;
            if (context is null)
                return;
            TransitionTo(PreviewSessionPhase.ValidatingSnapshot, snapshotVersion: snapshot.Version);
            var validation = _validator.Validate(context, snapshot);
            if (!validation.IsValid)
            {
                Pause();
                SetDiagnostics(PreviewAvailabilityState.InvalidData, validation.Diagnostics);
                return;
            }

            await EnsureStartedAsync().ConfigureAwait(false);
            if (!_transport.IsConnected || !_hostReady)
                return;
            TransitionTo(PreviewSessionPhase.MaterializingVfs, snapshotVersion: snapshot.Version);
            var materialized = await _vfs.MaterializeAsync(snapshot).ConfigureAwait(false);
            var requestId = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingVersions[requestId] = new PendingVersion(
                snapshot, materialized, State, completion, validation.Diagnostics);
            BeginLoadWatch(requestId);
            TransitionTo(PreviewSessionPhase.LoadingContent, requestId, snapshot.Version);
            await SendCommandAsync(command, new JObject
            {
                ["vfsRoot"] = materialized.Directory,
                ["level"] = Path.GetFileName(materialized.LevelPath),
                ["time"] = snapshot.PlaybackTime,
                ["settings"] = JObject.FromObject(_settings.Current),
                ["authenticationNonce"] = _authenticationNonce
            }, requestId, snapshot.Version, _previewVersion, snapshot.Version).ConfigureAwait(false);
            commandSent = true;
            await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Connection replacement cancels the old version explicitly.
        }
        catch (Exception ex)
        {
            if (commandSent || ex is PreviewUnityRuntimeException)
                return;
            SetDiagnostics(PreviewAvailabilityState.Faulted,
                [new PreviewDiagnostic(
                    "PREVIEW_SNAPSHOT_FAILED",
                    $"准备预览快照失败：{ex.Message}",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Editor)]);
        }
        finally
        {
            if (!commandSent)
                CompleteChangeQueue();
        }
    }

    private async Task PrepareAndSendChangesAsync(
        ProjectDataContext context,
        StoryboardPreviewSnapshot snapshot,
        StoryboardPreviewChangeSet changes)
    {
        var commandSent = false;
        try
        {
            var validation = _validator.Validate(context, snapshot);
            if (!validation.IsValid)
            {
                Pause();
                SetDiagnostics(PreviewAvailabilityState.InvalidData, validation.Diagnostics);
                return;
            }
            if (!IsAvailable)
            {
                await PrepareAndSendSnapshotAsync(snapshot, "preview.replaceSnapshot").ConfigureAwait(false);
                commandSent = true; // The delegated snapshot method owns completion/ACK handling.
                return;
            }
            var materialized = await _vfs.MaterializeAsync(snapshot).ConfigureAwait(false);
            var requestId = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingVersions[requestId] = new PendingVersion(
                snapshot, materialized, State, completion, validation.Diagnostics);
            BeginLoadWatch(requestId);
            TransitionTo(PreviewSessionPhase.LoadingContent, requestId, snapshot.Version);
            await SendCommandAsync(
                changes.Kind == StoryboardPreviewChangeKind.Incremental
                    ? "preview.applyChanges"
                    : "preview.replaceSnapshot",
                new JObject
                {
                    ["vfsRoot"] = materialized.Directory,
                    ["changes"] = JArray.FromObject(changes.EntityChanges),
                    ["kind"] = changes.Kind.ToString(),
                    ["time"] = snapshot.PlaybackTime,
                    ["settings"] = JObject.FromObject(_settings.Current)
                },
                requestId,
                snapshot.Version,
                _previewVersion,
                snapshot.Version).ConfigureAwait(false);
            commandSent = true;
            await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Connection replacement cancels the old update explicitly.
        }
        catch (Exception ex)
        {
            if (commandSent || ex is PreviewUnityRuntimeException)
                return;
            SetDiagnostics(PreviewAvailabilityState.Faulted,
                [new PreviewDiagnostic(
                    "PREVIEW_UPDATE_FAILED",
                    $"准备预览更新失败：{ex.Message}",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Editor)]);
        }
        finally
        {
            // The in-flight flag is cleared by ACK/NACK. If no command was sent, clear it here.
            if (!commandSent)
                CompleteChangeQueue();
        }
    }

    private async Task SendCommandIfAvailableAsync(string type, JObject payload)
    {
        if (!IsAvailable)
            return;
        try { await SendCommandAsync(type, payload).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // Expected when a command belongs to a retired connection.
        }
        catch (Exception ex)
        {
            SetDiagnosticsPreservingRoot(
                PreviewAvailabilityState.Faulted,
                new PreviewDiagnostic(
                    "PREVIEW_SEND_FAILED",
                    $"预览通信中断：{ex.Message}",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Transport,
                    _protocolTrace.FilePath)
                {
                    StackTrace = ex.ToString()
                });
        }
    }

    private Task SendCommandAsync(
        string type,
        JObject payload,
        string? requestId = null,
        long? editorVersion = null,
        long? basePreviewVersion = null,
        long? targetPreviewVersion = null,
        PreviewConnectionContext? requiredContext = null)
    {
        var context = requiredContext ?? _connectionContext;
        if (context is null || context.Lifetime.IsCancellationRequested ||
            context.HostGeneration != Volatile.Read(ref _generation) ||
            !ReferenceEquals(context, _connectionContext))
            throw new InvalidOperationException("Unity Preview connection context is not active.");
        var message = new PreviewProtocolMessage(
            type,
            context.SessionId,
            requestId ?? Guid.NewGuid().ToString("N"),
            editorVersion ?? _dataSource?.CurrentVersion ?? 0,
            basePreviewVersion ?? _previewVersion,
            targetPreviewVersion ?? _previewVersion,
            payload)
        {
            ConnectionId = context.ConnectionId,
            Generation = context.HostGeneration
        };
        TraceMessage(PreviewTraceDirection.Outbound, message);
        return _transport.SendAsync(message, context.Lifetime.Token);
    }

    private void OnMessageReceived(object? sender, PreviewProtocolMessage message)
    {
        TraceMessage(PreviewTraceDirection.Inbound, message);
        QueueCoordinator(new CoordinatorWork(
            $"message:{message.Type}",
            message,
            () => HandleMessageReceivedAsync(message)));
    }

    private async Task HandleMessageReceivedAsync(PreviewProtocolMessage message)
    {
        if (_connectionContext is not { } context ||
            context.Lifetime.IsCancellationRequested ||
            !string.Equals(message.ConnectionId, context.ConnectionId, StringComparison.Ordinal) ||
            message.Generation != context.HostGeneration ||
            !string.Equals(message.SessionId, context.SessionId, StringComparison.Ordinal))
        {
            AddOrMergeDiagnostic(new PreviewDiagnostic(
                "PREVIEW_STALE_ENVELOPE_IGNORED",
                $"Ignored stale Preview message '{message.Type}'.",
                PreviewDiagnosticSeverity.Information,
                PreviewDiagnosticSource.Transport), _availability);
            return;
        }
        _lastMessageAt = DateTimeOffset.Now;
        switch (message.Type)
        {
            case "host.hello":
                if (!string.Equals(
                        message.Payload.Value<string>("authenticationNonce"),
                        _authenticationNonce,
                        StringComparison.Ordinal))
                {
                    SetDiagnosticsPreservingRoot(PreviewAvailabilityState.Faulted,
                        [new PreviewDiagnostic(
                            "PREVIEW_AUTH_FAILED",
                            "Unity Preview 握手认证失败。",
                            PreviewDiagnosticSeverity.Error,
                            PreviewDiagnosticSource.Transport)]);
                    _hostReadyCompletion?.TrySetException(
                        new InvalidDataException("Unity Preview authentication failed."));
                    return;
                }
                var hostRevision = message.Payload.Value<int?>("hostRevision") ?? 0;
                var capabilitiesToken = message.Payload["capabilities"];
                bool HasCapability(string name) =>
                    capabilitiesToken is JObject capabilityObject
                        ? capabilityObject.Value<bool?>(name) == true
                        : capabilitiesToken is JArray capabilityArray &&
                          capabilityArray.Values<string>().Contains(name, StringComparer.Ordinal);
                if (hostRevision < 5 ||
                    !HasCapability("officialRuntimeDataOnly") ||
                    !HasCapability("chartPreflightV2") ||
                    !HasCapability("unityLogV1") ||
                    !HasCapability("loadProgressV1") ||
                    !HasCapability("healthCheckV1") ||
                    !HasCapability("threeWayHandshakeV2"))
                {
                    SetDiagnosticsPreservingRoot(PreviewAvailabilityState.Faulted,
                        [new PreviewDiagnostic(
                            "PREVIEW_RUNTIME_OUTDATED",
                            "Unity 预览播放器版本过旧，缺少正式数据边界、谱面预检或日志通道能力。",
                            PreviewDiagnosticSeverity.Error,
                            PreviewDiagnosticSource.Unity,
                            Suggestion: "请使用 Unity 6000.0.80f1 重新构建 Windows Editor Preview。")]);
                    _hostReadyCompletion?.TrySetException(
                        new InvalidDataException("Unity Preview host revision or capabilities are incompatible."));
                    return;
                }
                _hostRevision = hostRevision;
                try
                {
                    await SendCommandAsync("host.accept", new JObject
                    {
                        ["authenticationNonce"] = _authenticationNonce,
                        ["hostRevision"] = 5
                    }, message.RequestId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    SetDiagnosticsPreservingRoot(
                        PreviewAvailabilityState.Faulted,
                        new PreviewDiagnostic(
                            "PREVIEW_HANDSHAKE_SEND_FAILED",
                            $"Unity Preview 握手响应发送失败：{ex.Message}",
                            PreviewDiagnosticSeverity.Error,
                            PreviewDiagnosticSource.Transport,
                            _protocolTrace.FilePath,
                            Suggestion: "请检查 Preview 协议日志并重试。")
                        {
                            StackTrace = ex.ToString()
                        });
                    _hostReadyCompletion?.TrySetException(ex);
                }
                break;
            case "host.ready":
                if (_hostRevision < 5 || !string.Equals(
                        message.Payload.Value<string>("authenticationNonce"),
                        _authenticationNonce,
                        StringComparison.Ordinal))
                {
                    _hostReadyCompletion?.TrySetException(
                        new InvalidDataException("Unity Preview completed an invalid handshake."));
                    return;
                }
                _hostReady = true;
                _lastHeartbeatAt = DateTimeOffset.Now;
                _healthySince = DateTimeOffset.Now;
                TransitionTo(PreviewSessionPhase.HostReady);
                SetConnectionProgressDiagnostics(PreviewAvailabilityState.Connecting);
                try
                {
                    await SendCommandAsync("preview.settings.apply",
                        new JObject
                        {
                            ["settings"] = JObject.FromObject(_settings.Current),
                            ["pixelWidth"] = _pixelWidth,
                            ["pixelHeight"] = _pixelHeight,
                            ["active"] = true
                        }).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The connection was retired while applying initial settings.
                    _hostReady = false;
                    _hostReadyCompletion?.TrySetException(
                        new OperationCanceledException("Unity Preview connection was retired during handshake completion."));
                    return;
                }
                catch (Exception ex)
                {
                    AddOrMergeDiagnostic(new PreviewDiagnostic(
                        "PREVIEW_SEND_FAILED",
                        $"Unity Preview 初始设置发送失败：{ex.Message}",
                        PreviewDiagnosticSeverity.Error,
                        PreviewDiagnosticSource.Transport,
                        _protocolTrace.FilePath)
                    {
                        StackTrace = ex.ToString()
                    }, PreviewAvailabilityState.Connecting);
                }
                _hostReadyCompletion?.TrySetResult(true);
                break;
            case "preview.load.started":
                if (!IsCurrentLoadMessage(message))
                    break;
                NoteLoadProgress(message, 0, "started");
                TransitionTo(
                    PreviewSessionPhase.LoadingContent,
                    message.RequestId,
                    message.TargetPreviewVersion);
                SetConnectionProgressDiagnostics(PreviewAvailabilityState.Connecting);
                break;
            case "preview.load.progress":
                if (!IsCurrentLoadMessage(message))
                    break;
                var stage = message.Payload.Value<string>("stage");
                var stageIndex = LoadStageIndex(stage);
                if (stageIndex < _activeLoadStage)
                    break;
                NoteLoadProgress(message, stageIndex, stage);
                TransitionTo(
                    PreviewSessionPhase.LoadingContent,
                    message.RequestId,
                    message.TargetPreviewVersion,
                    stage);
                PublishChanged();
                break;
            case "preview.load.ready":
                if (!IsCurrentLoadMessage(message))
                    break;
                var readyTime = message.Payload.Value<double?>("time");
                var readyDuration = message.Payload.Value<double?>("duration");
                if (readyTime.HasValue)
                    Volatile.Write(ref _currentTime, Math.Max(0, readyTime.Value));
                if (readyDuration.HasValue)
                    Volatile.Write(ref _duration, Math.Max(0, readyDuration.Value));
                AcceptPending(message);
                break;
            case "preview.load.failed":
                if (!IsCurrentLoadMessage(message))
                    break;
                RejectPending(message);
                break;
            case "preview.health.ok":
                _lastHeartbeatAt = DateTimeOffset.Now;
                _heartbeatFailureCount = 0;
                CompletePendingCommand(message);
                break;
            case "preview.ack":
                CompletePendingCommand(message);
                break;
            case "preview.unityLog":
                HandleUnityLog(message);
                break;
            case "preview.rejected":
            case "preview.validationFailed":
                RejectPending(message);
                break;
            case "preview.time":
                var time = Math.Max(0, message.Payload.Value<double?>("time") ?? CurrentTime);
                Volatile.Write(ref _currentTime, time);
                PublishTimeChanged(time);
                break;
            case "preview.state":
                var reportedTime = message.Payload.Value<double?>("time");
                var reportedDuration = message.Payload.Value<double?>("duration");
                if (reportedTime.HasValue)
                {
                    Volatile.Write(ref _currentTime, Math.Max(0, reportedTime.Value));
                    PublishTimeChanged(CurrentTime);
                }
                if (reportedDuration.HasValue)
                    Volatile.Write(ref _duration, Math.Max(0, reportedDuration.Value));
                if (Enum.TryParse<PreviewPlaybackState>(
                        message.Payload.Value<string>("state"), true, out var state))
                    SetState(state);
                break;
            case "preview.performance":
                _performance = new PreviewPerformanceSample(
                    message.Payload.Value<double?>("fps") ?? 0,
                    message.Payload.Value<double?>("averageFrameMs") ?? 0,
                    message.Payload.Value<int?>("renderWidth") ?? 0,
                    message.Payload.Value<int?>("renderHeight") ?? 0,
                    message.Payload.Value<long?>("cacheBytes") ?? 0,
                    message.Payload.Value<double?>("effectiveRenderScale") ?? 1,
                    message.Payload.Value<long?>("suppressedExceptions") ?? 0,
                    message.Payload.Value<long?>("droppedTelemetryMessages") ?? 0);
                PublishChanged();
                break;
            case "preview.telemetry":
                TryHandleUnityRuntimeException(message.Payload);
                break;
            case "preview.error":
                RejectPending(message);
                break;
        }
    }

    private void TryHandleUnityRuntimeException(JObject telemetry)
    {
        var raw = telemetry.Value<string>("cytoidGameCoreV2");
        if (string.IsNullOrWhiteSpace(raw))
            return;

        JObject envelope;
        try
        {
            envelope = JObject.Parse(raw);
        }
        catch
        {
            return;
        }

        if (!string.Equals(envelope.Value<string>("type"), "session.result",
                StringComparison.Ordinal))
        {
            return;
        }

        var error = envelope["payload"]?["error"] as JObject;
        if (error is null)
            return;

        var message = error.Value<string>("message");
        if (string.IsNullOrWhiteSpace(message))
            message = "Unity 初始化关卡时发生了未提供详情的运行时异常。";
        var path = error["details"]?.Value<string>("path") ??
                   ExtractJsonPath(message);
        var exception = new PreviewUnityRuntimeException(message);

        var rejectedAny = false;
        foreach (var item in _pendingVersions.ToArray())
        {
            if (!_pendingVersions.TryRemove(item.Key, out var pending))
                continue;
            pending.Completion.TrySetException(exception);
            rejectedAny = true;
        }
        foreach (var item in _pendingCommands.ToArray())
        {
            if (!_pendingCommands.TryRemove(item.Key, out var pending))
                continue;
            pending.TrySetException(exception);
            rejectedAny = true;
        }

        Pause();
        SetDiagnostics(PreviewAvailabilityState.InvalidData,
            [new PreviewDiagnostic(
                "PREVIEW_UNITY_RUNTIME_EXCEPTION",
                message,
                PreviewDiagnosticSeverity.Error,
                PreviewDiagnosticSource.Unity,
                path,
                Suggestion: error.Value<string>("code"))]);
        if (rejectedAny)
            CompleteChangeQueue();
    }

    private static string? ExtractJsonPath(string message)
    {
        const string marker = "Path '";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += marker.Length;
        var end = message.IndexOf('\'', start);
        return end > start ? $"$.{message[start..end]}" : null;
    }

    private void AcceptPending(PreviewProtocolMessage message)
    {
        if (_pendingVersions.TryRemove(message.RequestId, out var pending))
        {
            if (!MatchesChartIdentity(pending.Vfs.ChartPath, message.Payload["chartIdentity"] as JObject,
                    out var mismatch))
            {
                pending.Completion.TrySetException(new InvalidDataException(mismatch));
                AddOrMergeDiagnostic(new PreviewDiagnostic(
                    "PREVIEW_CHART_SNAPSHOT_MISMATCH",
                    mismatch,
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Unity,
                    pending.Vfs.ChartPath,
                    Suggestion: "请重载关卡；若仍出现，请重新构建 Unity 预览播放器。")
                {
                    SnapshotVersion = message.TargetPreviewVersion
                }, PreviewAvailabilityState.InvalidData);
                CompleteChangeQueue();
                return;
            }
            _previewVersion = message.TargetPreviewVersion;
            _lastKnownGood = new LastKnownGoodPreview(
                pending.Snapshot,
                pending.Vfs.Directory,
                DateTimeOffset.UtcNow,
                CurrentTime,
                pending.PlaybackState);
            TransitionTo(PreviewSessionPhase.PreviewReady, message.RequestId, message.TargetPreviewVersion);
            var readyDiagnostics = pending.Diagnostics
                .Where(item => item.Severity != PreviewDiagnosticSeverity.Error)
                .ToList();
            if (message.Payload["warnings"] is JArray warnings)
            {
                readyDiagnostics.AddRange(warnings.Values<string>()
                    .Where(warning => !string.IsNullOrWhiteSpace(warning))
                    .Select(warning =>
                        new PreviewDiagnostic(
                            "PREVIEW_UNITY_WARNING",
                            warning!,
                            PreviewDiagnosticSeverity.Warning,
                            PreviewDiagnosticSource.Unity)));
            }
            readyDiagnostics.AddRange(_activeLoadWarnings.ToArray());
            _rootFailureDiagnostic = null;
            SetDiagnostics(
                readyDiagnostics.Any(item => item.Severity == PreviewDiagnosticSeverity.Warning)
                    ? PreviewAvailabilityState.ReadyWithWarnings
                    : PreviewAvailabilityState.Ready,
                readyDiagnostics);
            ClearLoadWatch(message.RequestId);
            _ = _vfs.PruneAsync(
                pending.Snapshot.SessionId,
                new HashSet<long> { pending.Snapshot.Version },
                _settings.Current.MaxCacheBytes);
            if (_pendingRestore is { } restore)
            {
                _pendingRestore = null;
                RestorePlayback(restore);
            }
            pending.Completion.TrySetResult(true);
        }
        CompleteChangeQueue();
    }

    private void RejectPending(PreviewProtocolMessage message)
    {
        var rejectedPending = false;
        if (_pendingCommands.TryRemove(message.RequestId, out var command))
        {
            command.TrySetException(new InvalidOperationException(
                message.Payload.Value<string>("message") ?? "Unity rejected the preview command."));
            rejectedPending = true;
        }
        if (_pendingVersions.TryRemove(message.RequestId, out var pending))
        {
            pending.Completion.TrySetException(new InvalidOperationException(
                message.Payload.Value<string>("message") ?? "Unity rejected the preview version."));
            rejectedPending = true;
        }
        // A session.result runtime exception may arrive before Unity emits its
        // delayed generic rejection. Keep the actionable root exception.
        if (!rejectedPending && _diagnostics.Any(item =>
                item.Code == "PREVIEW_UNITY_RUNTIME_EXCEPTION"))
        {
            return;
        }
        Pause();
        ClearLoadWatch(message.RequestId);
        TransitionTo(PreviewSessionPhase.Failed, message.RequestId, message.TargetPreviewVersion);
        SetDiagnostics(PreviewAvailabilityState.InvalidData,
            [new PreviewDiagnostic(
                message.Payload.Value<string>("code") ?? "PREVIEW_UNITY_REJECTED",
                message.Payload.Value<string>("message") ?? "Unity 拒绝了此预览版本，已保留上一个有效画面。",
                PreviewDiagnosticSeverity.Error,
                PreviewDiagnosticSource.Unity,
                message.Payload.Value<string>("path"),
                message.Payload.Value<string>("entityId"),
                message.Payload.Value<string>("property"))]);
        CompleteChangeQueue();
    }

    private void CompleteChangeQueue()
    {
        StoryboardPreviewSnapshot? queuedSnapshot;
        StoryboardPreviewChangeSet? queued;
        lock (_sync)
        {
            _changeInFlight = false;
            queuedSnapshot = _queuedSnapshot;
            _queuedSnapshot = null;
            queued = _queuedChanges;
            _queuedChanges = null;
            if (queuedSnapshot is not null || queued is not null)
                _changeInFlight = true;
        }
        if (queuedSnapshot is not null)
        {
            // A resource/snapshot replacement takes precedence over incremental
            // changes. If edits arrived after that replacement was queued, rebuild
            // the snapshot now so those later edits are not dropped.
            var snapshotToSend = queued is not null && _context is not null && _dataSource is not null
                ? _dataSource.GetSnapshot(_context, CurrentTime)
                : queuedSnapshot;
            _ = PrepareAndSendSnapshotAsync(
                snapshotToSend,
                "preview.replaceSnapshot");
        }
        else if (queued is not null && _context is not null && _dataSource is not null)
        {
            _ = PrepareAndSendChangesAsync(
                _context,
                _dataSource.GetSnapshot(_context, CurrentTime),
                queued);
        }
    }

    private void AbandonChangeQueue()
    {
        lock (_sync)
        {
            _changeInFlight = false;
            _queuedSnapshot = null;
            _queuedChanges = null;
        }
    }

    private void OnConnectionChanged(object? sender, PreviewTransportStateChanged state)
    {
        _protocolTrace.Record(new PreviewProtocolTraceEntry(
            DateTimeOffset.Now, PreviewTraceDirection.Lifecycle,
            Volatile.Read(ref _generation), _connectionId, _phase,
            state.Connected ? "transport.connected" : "transport.disconnected",
            null, _dataSource?.CurrentVersion ?? 0, _previewVersion, _previewVersion,
            state.Reason));
        QueueCoordinator(new CoordinatorWork(
            state.Connected ? "transport.connected" : "transport.disconnected",
            null,
            () =>
            {
                HandleConnectionChanged(state);
                return Task.CompletedTask;
            }));
    }

    private void HandleConnectionChanged(PreviewTransportStateChanged state)
    {
        if (Volatile.Read(ref _shuttingDown) != 0)
            return;
        if (state.Generation != _transport.Generation ||
            _connectionContext is not { } context ||
            context.TransportGeneration != state.Generation ||
            context.Lifetime.IsCancellationRequested)
            return;
        if (!state.Connected && _process.IsRunning)
        {
            _hostReady = false;
            TransitionTo(PreviewSessionPhase.Disconnected);
            SetDiagnosticsPreservingRoot(PreviewAvailabilityState.Disconnected,
                [new PreviewDiagnostic(
                    "PREVIEW_CONNECTION_LOST",
                    "与 Unity Preview 的连接已断开。",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Transport)]);
            EnrichConnectionLostDiagnostic(state);
            _hostReadyCompletion?.TrySetException(
                state.Exception ?? new IOException(
                    state.Reason ?? "Unity Preview pipe closed."));
            ScheduleAutomaticRecovery();
        }
    }

    private void OnProcessExited(object? sender, UnityPreviewProcessExited processExit)
    {
        QueueCoordinator(new CoordinatorWork(
            "process.exited",
            null,
            () =>
            {
                HandleProcessExited(processExit);
                return Task.CompletedTask;
            }));
    }

    private void HandleProcessExited(UnityPreviewProcessExited processExit)
    {
        if (Volatile.Read(ref _shuttingDown) != 0)
            return;
        if (processExit.Generation != _process.Generation || processExit.Expected)
            return;
        _pendingRestore = CaptureRestorePoint();
        _hostReady = false;
        Pause();
        TransitionTo(PreviewSessionPhase.Failed);
        SetDiagnosticsPreservingRoot(PreviewAvailabilityState.Faulted,
            [new PreviewDiagnostic(
                "PREVIEW_PROCESS_EXITED",
                $"Unity Preview 已异常退出（退出码 {processExit.ExitCode?.ToString() ?? "unknown"}）。",
                PreviewDiagnosticSeverity.Error,
                PreviewDiagnosticSource.Unity,
                _protocolTrace.FilePath)
            {
                StackTrace = _protocolTrace.DescribeRecent()
            }]);
        _hostReadyCompletion?.TrySetException(
            new InvalidOperationException("Unity Preview exited before the operation completed."));
        ScheduleAutomaticRecovery();
    }

    private void OnTransportFaulted(object? sender, PreviewTransportFault fault)
    {
        _protocolTrace.Record(new PreviewProtocolTraceEntry(
            DateTimeOffset.Now, PreviewTraceDirection.Fault,
            Volatile.Read(ref _generation), _connectionId, _phase,
            fault.Kind.ToString(), fault.RequestId,
            _dataSource?.CurrentVersion ?? 0, _previewVersion, _previewVersion,
            fault.Reason));
        QueueCoordinator(new CoordinatorWork(
            $"transport.fault:{fault.Kind}",
            null,
            () =>
            {
                HandleTransportFault(fault);
                return Task.CompletedTask;
            }));
    }

    private void HandleTransportFault(PreviewTransportFault fault)
    {
        if (fault.Generation != _transport.Generation)
            return;
        _lastTransportFault = fault;
        if (fault.Kind == PreviewTransportFaultKind.MessageDispatch)
        {
            AddOrMergeDiagnostic(new PreviewDiagnostic(
                "PREVIEW_MESSAGE_HANDLER_FAILED",
                $"编辑器处理 Preview 消息失败，但管道仍保持连接：{fault.Reason}",
                PreviewDiagnosticSeverity.Error,
                PreviewDiagnosticSource.Editor,
                _protocolTrace.FilePath,
                Suggestion: "该错误不会再被误报为连接断开；请查看调用堆栈。")
            {
                StackTrace = fault.Exception.ToString()
            }, _availability);
            return;
        }

        if (fault.Kind is PreviewTransportFaultKind.InvalidFrame or
            PreviewTransportFaultKind.MalformedPayload)
        {
            SetDiagnosticsPreservingRoot(
                PreviewAvailabilityState.Faulted,
                new PreviewDiagnostic(
                    "PREVIEW_FRAME_INVALID",
                    $"Unity Preview 协议帧无效：{fault.Reason}",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Transport,
                    _protocolTrace.FilePath)
                {
                    StackTrace = fault.Exception.ToString()
                });
        }
    }

    private void ScheduleAutomaticRecovery()
    {
        if (Volatile.Read(ref _shuttingDown) != 0 ||
            _automaticRestartCount >= 1 ||
            Interlocked.Exchange(ref _recoveryInFlight, 1) != 0)
            return;
        _automaticRestartCount++;
        _ = RestartAfterFailureAsync();
    }

    private async Task RestartAfterFailureAsync()
    {
        try
        {
            await RestartAsync().ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _recoveryInFlight, 0);
        }
    }

    private Task RestartAsync()
    {
        TaskCompletionSource<bool> completion;
        lock (_restartSync)
        {
            if (_restartTask is { IsCompleted: false } running)
                return running;
            completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _restartTask = completion.Task;
        }
        _ = RunRestartAsync(completion);
        return completion.Task;
    }

    private async Task RunRestartAsync(TaskCompletionSource<bool> completion)
    {
        try
        {
            await RestartCoreAsync().ConfigureAwait(false);
            completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            lock (_restartSync)
            {
                if (ReferenceEquals(_restartTask, completion.Task))
                    _restartTask = null;
            }
        }
    }

    private async Task RestartCoreAsync()
    {
        _pendingRestore ??= CaptureRestorePoint();
        lock (_sync)
        {
            _queuedSnapshot = null;
            _queuedChanges = null;
            _changeInFlight = false;
        }
        await TearDownConnectionAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        await EnsureStartedAsync().ConfigureAwait(false);
        if (!_hostReady)
            return;
        var snapshot = _lastKnownGood is { } lastKnownGood
            ? lastKnownGood.Snapshot with { PlaybackTime = CurrentTime }
            : null;
        if (snapshot is null && _context is not null && _dataSource is not null)
            snapshot = _dataSource.GetSnapshot(_context, CurrentTime);
        if (snapshot is not null)
        {
            lock (_sync)
                _changeInFlight = true;
            await PrepareAndSendSnapshotAsync(snapshot, "preview.open").ConfigureAwait(false);
        }
    }

    private async Task RestartForNewSessionAsync()
    {
        _pendingRestore = null;
        _lastKnownGood = null;
        _rootFailureDiagnostic = null;
        _previewVersion = 0;
        lock (_sync)
        {
            _queuedSnapshot = null;
            _queuedChanges = null;
            _changeInFlight = false;
        }
        await TearDownConnectionAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
    }

    private void OnSettingsChanged(object? sender, PreviewSettings settings)
    {
        var previous = _lastAppliedSettings;
        _lastAppliedSettings = settings;
        if (!string.Equals(previous.FrameRate, settings.FrameRate, StringComparison.Ordinal) &&
            _scrubTimer is not null)
        {
            var frameRate = int.TryParse(settings.FrameRate, out var target) ? target : 60;
            var period = TimeSpan.FromMilliseconds(1000d / Math.Clamp(frameRate, 30, 120));
            _scrubTimer.Change(period, period);
        }
        if (previous.ExternalClockRate != settings.ExternalClockRate &&
            _clockMode == PreviewClockMode.External)
            EnsureExternalClockTimer();
        if (!settings.HardwareAcceleration)
        {
            _ = DisableForSettingsAsync();
            return;
        }
        if (!previous.HardwareAcceleration || previous.RenderThreads != settings.RenderThreads)
        {
            _ = RestartAfterSettingsChangeAsync();
            return;
        }
        _ = SendCommandIfAvailableAsync("preview.settings.apply",
            new JObject
            {
                ["settings"] = JObject.FromObject(settings),
                ["pixelWidth"] = _pixelWidth,
                ["pixelHeight"] = _pixelHeight,
                ["active"] = true
            });
    }

    private async Task RestartAfterSettingsChangeAsync()
    {
        try
        {
            await RestartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetDiagnosticsPreservingRoot(
                PreviewAvailabilityState.Faulted,
                new PreviewDiagnostic(
                    "PREVIEW_RESTART_FAILED",
                    $"应用设置时重启 Unity Preview 失败：{ex.Message}",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Editor,
                    _protocolTrace.FilePath)
                {
                    StackTrace = ex.ToString()
                });
        }
    }

    private async Task DisableForSettingsAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        SetDiagnostics(PreviewAvailabilityState.Disabled,
            [new PreviewDiagnostic(
                "PREVIEW_GPU_DISABLED",
                "硬件加速已关闭，原生预览已停止。",
                PreviewDiagnosticSeverity.Warning,
                PreviewDiagnosticSource.Editor)]);
    }

    private void EnsureScrubTimer()
    {
        if (_scrubTimer is not null)
            return;
        var frameRate = int.TryParse(_settings.Current.FrameRate, out var target) ? target : 60;
        var period = TimeSpan.FromMilliseconds(1000d / Math.Clamp(frameRate, 30, 120));
        _scrubTimer = new Timer(_ =>
        {
            double? time;
            lock (_sync)
            {
                time = _pendingScrubTime;
                _pendingScrubTime = null;
            }
            if (time.HasValue)
                _ = SendCommandIfAvailableAsync(
                    "preview.scrub.update",
                    new JObject { ["time"] = time.Value });
        }, null, period, period);
    }

    private void EnsureExternalClockTimer()
    {
        var period = TimeSpan.FromMilliseconds(
            1000d / Math.Clamp(_settings.Current.ExternalClockRate, 30, 60));
        if (_externalClockTimer is null)
        {
            _externalClockTimer = new Timer(
                _ => _ = FlushExternalClockTickAsync(),
                null,
                TimeSpan.Zero,
                period);
            return;
        }

        _externalClockTimer.Change(TimeSpan.Zero, period);
    }

    private async Task FlushExternalClockTickAsync()
    {
        if (Interlocked.Exchange(ref _externalClockTickInFlight, 1) != 0)
            return;
        try
        {
            double? time;
            lock (_sync)
            {
                time = _pendingExternalClockTime;
                _pendingExternalClockTime = null;
            }
            if (time.HasValue)
            {
                await SendCommandIfAvailableAsync(
                    "preview.clock.tick",
                    new JObject { ["time"] = time.Value }).ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref _externalClockTickInFlight, 0);
        }
    }

    private void SetState(PreviewPlaybackState state)
    {
        if (_state == state)
            return;
        _state = state;
        PublishStateChanged(state);
    }

    private void QueueCoordinator(CoordinatorWork work)
    {
        if (!_coordinatorQueue.Writer.TryWrite(work) &&
            Volatile.Read(ref _shuttingDown) == 0)
        {
            _protocolTrace.Record(new PreviewProtocolTraceEntry(
                DateTimeOffset.Now, PreviewTraceDirection.Fault,
                Volatile.Read(ref _generation), _connectionId, _phase,
                "coordinator.queue.closed", work.Message?.RequestId,
                work.Message?.EditorVersion ?? 0,
                work.Message?.BasePreviewVersion ?? _previewVersion,
                work.Message?.TargetPreviewVersion ?? _previewVersion,
                work.Source));
        }
    }

    private async Task RunCoordinatorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var work in _coordinatorQueue.Reader
                               .ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                try
                {
                    await work.Execute().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    HandleCoordinatorFailure(work, ex);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void HandleCoordinatorFailure(CoordinatorWork work, Exception exception)
    {
        var message = work.Message;
        var diagnostic = new PreviewDiagnostic(
            "PREVIEW_MESSAGE_HANDLER_FAILED",
            message is null
                ? $"编辑器 Preview 协调事件处理失败：{work.Source}。"
                : $"编辑器处理 Unity Preview 消息“{message.Type}”失败：{exception.Message}",
            PreviewDiagnosticSeverity.Error,
            PreviewDiagnosticSource.Editor,
            _protocolTrace.FilePath,
            Suggestion: "请查看协议日志和调用堆栈；该错误不会被伪装为物理断连。")
        {
            StackTrace = exception + Environment.NewLine + _protocolTrace.DescribeRecent()
        };
        _protocolTrace.Record(new PreviewProtocolTraceEntry(
            DateTimeOffset.Now, PreviewTraceDirection.Fault,
            Volatile.Read(ref _generation), _connectionId, _phase,
            "coordinator.handler.failed", message?.RequestId,
            message?.EditorVersion ?? 0,
            message?.BasePreviewVersion ?? _previewVersion,
            message?.TargetPreviewVersion ?? _previewVersion,
            message?.Type ?? work.Source));

        if (message is null || !IsEssentialProtocolMessage(message.Type))
        {
            AddOrMergeDiagnostic(diagnostic, _availability);
            return;
        }

        var failure = new InvalidOperationException(diagnostic.Message, exception);
        _hostReady = false;
        Pause();
        TransitionTo(PreviewSessionPhase.Failed, message.RequestId, message.TargetPreviewVersion);
        SetDiagnosticsPreservingRoot(PreviewAvailabilityState.Faulted, diagnostic);
        AbandonChangeQueue();
        _hostReadyCompletion?.TrySetException(failure);
        foreach (var item in _pendingVersions.ToArray())
            if (_pendingVersions.TryRemove(item.Key, out var pending))
                pending.Completion.TrySetException(failure);
        foreach (var item in _pendingCommands.ToArray())
            if (_pendingCommands.TryRemove(item.Key, out var pending))
                pending.TrySetException(failure);
        ScheduleAutomaticRecovery();
    }

    private static bool IsEssentialProtocolMessage(string type) => type is
        "host.hello" or "host.ready" or
        "preview.load.started" or "preview.load.progress" or
        "preview.load.ready" or "preview.load.failed" or
        "preview.health.ok" or "preview.ack" or
        "preview.rejected" or "preview.validationFailed" or "preview.error";

    private async Task SendCommandAndWaitAsync(string type, JObject payload, TimeSpan timeout)
    {
        var context = _connectionContext
            ?? throw new InvalidOperationException("Unity Preview connection context is not active.");
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<PreviewProtocolMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommands[requestId] = completion;
        try
        {
            await SendCommandAsync(
                type, payload, requestId,
                requiredContext: context).ConfigureAwait(false);
            await completion.Task.WaitAsync(timeout).ConfigureAwait(false);
            if (!ReferenceEquals(context, _connectionContext) ||
                context.Lifetime.IsCancellationRequested)
                throw new OperationCanceledException("Preview command belongs to an old connection generation.");
        }
        finally
        {
            _pendingCommands.TryRemove(requestId, out _);
        }
    }

    private void CompletePendingCommand(PreviewProtocolMessage message)
    {
        if (_pendingCommands.TryRemove(message.RequestId, out var completion))
            completion.TrySetResult(message);
    }

    private void TransitionTo(
        PreviewSessionPhase phase,
        string? requestId = null,
        long? snapshotVersion = null,
        string? detail = null)
    {
        _phase = phase;
        _phaseStartedAt = DateTimeOffset.Now;
        _phaseDetail = detail;
        if (requestId is not null)
            _activeRequestId = requestId;
        if (snapshotVersion.HasValue)
            _activeSnapshotVersion = snapshotVersion;
        TraceLifecycle($"phase.{phase}",
            $"request={requestId ?? _activeRequestId ?? "-"}; snapshot={snapshotVersion?.ToString() ?? _activeSnapshotVersion?.ToString() ?? "-"}; detail={detail ?? "-"}");
        PublishChanged();
    }

    private void BeginLoadWatch(string requestId)
    {
        while (_activeLoadWarnings.TryDequeue(out _)) { }
        _loadStartedAt = DateTimeOffset.Now;
        _lastLoadProgressAt = null;
        _activeLoadStage = -1;
        _activeRequestId = requestId;
    }

    private void ClearLoadWatch(string requestId)
    {
        if (!string.Equals(_activeRequestId, requestId, StringComparison.Ordinal))
            return;
        _loadStartedAt = DateTimeOffset.MinValue;
        _lastLoadProgressAt = null;
        _activeLoadStage = -1;
    }

    private bool IsCurrentLoadMessage(PreviewProtocolMessage message) =>
        string.Equals(message.RequestId, _activeRequestId, StringComparison.Ordinal) &&
        message.TargetPreviewVersion == _activeSnapshotVersion &&
        _pendingVersions.ContainsKey(message.RequestId);

    private void NoteLoadProgress(PreviewProtocolMessage message, int stageIndex, string? stage)
    {
        _lastLoadProgressAt = DateTimeOffset.Now;
        _activeLoadStage = Math.Max(_activeLoadStage, stageIndex);
        _phaseDetail = stage;
    }

    private static int LoadStageIndex(string? stage) => stage switch
    {
        "started" => 0,
        "accepted" => 1,
        "readingVfs" => 2,
        "parsingLevel" => 3,
        "startingGame" => 4,
        "loadingSceneAndAssets" => 5,
        "evaluatingFirstFrame" => 6,
        _ => 0
    };

    private void EnsureHealthTimer()
    {
        _healthTimer ??= new Timer(
            _ => _ = RunHealthCheckAsync(),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
    }

    private async Task RunHealthCheckAsync()
    {
        var context = _connectionContext;
        if (!_hostReady || !_transport.IsConnected || context is null ||
            context.Lifetime.IsCancellationRequested)
            return;
        var now = DateTimeOffset.Now;
        if (_healthySince.HasValue && now - _healthySince.Value >= TimeSpan.FromSeconds(30))
        {
            _automaticRestartCount = 0;
            _healthySince = now;
        }
        if (_phase == PreviewSessionPhase.LoadingContent && _loadStartedAt != DateTimeOffset.MinValue)
        {
            var failure = _lastLoadProgressAt is null && now - _loadStartedAt >= TimeSpan.FromSeconds(5)
                ? "Unity Preview did not report initial load progress within 5 seconds."
                : now - _loadStartedAt >= TimeSpan.FromSeconds(120)
                    ? "Unity Preview content load exceeded the 120 second absolute limit."
                    : _lastLoadProgressAt.HasValue && now - _lastLoadProgressAt.Value >= TimeSpan.FromSeconds(30)
                        ? "Unity Preview content load made no progress for 30 seconds."
                        : null;
            if (failure is not null)
            {
                QueueCoordinator(new CoordinatorWork(
                    "timeout.load",
                    null,
                    () =>
                    {
                        if (ReferenceEquals(context, _connectionContext) &&
                            !context.Lifetime.IsCancellationRequested)
                            FailPendingAndRecover("PREVIEW_LOAD_TIMEOUT", failure);
                        return Task.CompletedTask;
                    }));
                return;
            }
        }
        if (Interlocked.Exchange(ref _healthCheckInFlight, 1) != 0)
            return;
        try
        {
            await SendCommandAndWaitAsync(
                "preview.health.check",
                new JObject { ["generation"] = context.HostGeneration },
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            QueueCoordinator(new CoordinatorWork(
                "heartbeat.succeeded",
                null,
                () =>
                {
                    if (ReferenceEquals(context, _connectionContext) &&
                        !context.Lifetime.IsCancellationRequested)
                    {
                        _lastHeartbeatAt = DateTimeOffset.Now;
                        _heartbeatFailureCount = 0;
                    }
                    return Task.CompletedTask;
                }));
        }
        catch (Exception ex)
        {
            QueueCoordinator(new CoordinatorWork(
                "heartbeat.failed",
                null,
                () =>
                {
                    if (!ReferenceEquals(context, _connectionContext) ||
                        context.Lifetime.IsCancellationRequested)
                        return Task.CompletedTask;
                    if (++_heartbeatFailureCount >= 2)
                        FailPendingAndRecover(
                            "PREVIEW_RUNTIME_UNRESPONSIVE",
                            "Unity Preview missed two consecutive 5 second heartbeats.",
                            ex);
                    return Task.CompletedTask;
                }));
        }
        finally
        {
            Volatile.Write(ref _healthCheckInFlight, 0);
        }
    }

    private void FailPendingAndRecover(string code, string message, Exception? inner = null)
    {
        var failure = new TimeoutException(message, inner);
        _hostReady = false;
        Pause();
        TransitionTo(PreviewSessionPhase.Failed);
        SetDiagnosticsPreservingRoot(PreviewAvailabilityState.Faulted,
            [new PreviewDiagnostic(
                code,
                message,
                PreviewDiagnosticSeverity.Error,
                PreviewDiagnosticSource.Unity,
                _protocolTrace.FilePath,
                Suggestion: "Restart Unity Preview and inspect diagnostics for the last loading stage.")
            {
                StackTrace = (inner?.ToString() ?? string.Empty) +
                             Environment.NewLine + _protocolTrace.DescribeRecent()
            }]);
        AbandonChangeQueue();
        foreach (var item in _pendingVersions.ToArray())
        {
            if (_pendingVersions.TryRemove(item.Key, out var pending))
                pending.Completion.TrySetException(failure);
        }
        foreach (var item in _pendingCommands.ToArray())
        {
            if (_pendingCommands.TryRemove(item.Key, out var pending))
                pending.TrySetException(failure);
        }
        ScheduleAutomaticRecovery();
    }

    private void RestorePlayback(PreviewPlaybackRestorePoint restore)
    {
        _clockMode = restore.ClockMode;
        Volatile.Write(ref _currentTime, Math.Max(0, restore.Time));
        SetClockMode(restore.ClockMode);
        Seek(restore.Time);
        SetPlaybackState(restore.State);
        PublishTimeChanged(CurrentTime);
    }

    private void SetDiagnostics(PreviewAvailabilityState availability, IReadOnlyList<PreviewDiagnostic> diagnostics)
    {
        lock (_sync)
        {
            _availability = availability;
            _diagnostics = diagnostics;
        }
        PublishChanged();
    }

    private void SetConnectionProgressDiagnostics(PreviewAvailabilityState availability)
    {
        PreviewDiagnostic? root;
        lock (_sync)
        {
            root = _rootFailureDiagnostic;
        }
        SetDiagnostics(availability, root is null ? [] : [root]);
    }

    private void SetDiagnosticsPreservingRoot(
        PreviewAvailabilityState availability,
        PreviewDiagnostic diagnostic) =>
        SetDiagnosticsPreservingRoot(availability, [diagnostic]);

    private void SetDiagnosticsPreservingRoot(
        PreviewAvailabilityState availability,
        IReadOnlyList<PreviewDiagnostic> diagnostics)
    {
        lock (_sync)
        {
            _rootFailureDiagnostic ??= diagnostics.FirstOrDefault(item =>
                item.Severity == PreviewDiagnosticSeverity.Error);
            var merged = new List<PreviewDiagnostic>();
            if (_rootFailureDiagnostic is not null)
                merged.Add(_rootFailureDiagnostic);
            foreach (var diagnostic in diagnostics)
            {
                if (!merged.Any(item => item.Code == diagnostic.Code &&
                                        item.Message == diagnostic.Message))
                    merged.Add(diagnostic);
            }
            _availability = availability;
            _diagnostics = merged;
        }
        PublishChanged();
    }

    private void EnrichConnectionLostDiagnostic(PreviewTransportStateChanged state)
    {
        lock (_sync)
        {
            var items = _diagnostics.ToList();
            var index = items.FindIndex(item => item.Code == "PREVIEW_CONNECTION_LOST");
            if (index < 0)
                return;
            var enriched = items[index] with
            {
                Message = $"与 Unity Preview 的物理连接已断开：{state.Reason ?? "管道已关闭"}",
                Path = _protocolTrace.FilePath,
                Suggestion = "请查看协议日志中的最后阶段、消息和 generation。",
                StackTrace = BuildConnectionFailureDetail(state)
            };
            items[index] = enriched;
            if (_rootFailureDiagnostic?.Code == "PREVIEW_CONNECTION_LOST")
                _rootFailureDiagnostic = enriched;
            _diagnostics = items;
        }
        PublishChanged();
    }

    private string BuildConnectionFailureDetail(PreviewTransportStateChanged state)
    {
        var context = _connectionContext;
        var fault = _lastTransportFault;
        var lastMessage = _protocolTrace.Snapshot().LastOrDefault(entry =>
            entry.Direction is PreviewTraceDirection.Inbound or PreviewTraceDirection.Outbound);
        return $"HostGeneration={Volatile.Read(ref _generation)}; " +
               $"TransportGeneration={state.Generation}; " +
               $"ConnectionId={context?.ConnectionId ?? _connectionId}; " +
               $"Phase={_phase}; Stage={_phaseDetail ?? "-"}; LastMessageAt={_lastMessageAt:O}; " +
               $"LastMessage={lastMessage?.Type ?? "-"}; LastRequest={lastMessage?.RequestId ?? "-"}; " +
               $"Fault={fault?.Kind.ToString() ?? "none"}; Reason={state.Reason}\n" +
               (state.Exception?.ToString() ?? fault?.Exception.ToString() ?? string.Empty) +
               Environment.NewLine + _protocolTrace.DescribeRecent();
    }

    private void PublishChanged()
    {
        var invocationList = Changed?.GetInvocationList();
        if (invocationList is null)
            return;
        foreach (var handler in invocationList)
        {
            try { ((EventHandler)handler)(this, EventArgs.Empty); }
            catch (Exception ex) { RecordObserverFailure("Changed", ex); }
        }
    }

    private void PublishTimeChanged(double time) =>
        PublishEventSafely(TimeChanged, time, "TimeChanged");

    private void PublishStateChanged(PreviewPlaybackState state) =>
        PublishEventSafely(StateChanged, state, "StateChanged");

    private void PublishEventSafely<T>(EventHandler<T>? handlers, T value, string eventName)
    {
        var invocationList = handlers?.GetInvocationList();
        if (invocationList is null)
            return;
        foreach (var handler in invocationList)
        {
            try { ((EventHandler<T>)handler)(this, value); }
            catch (Exception ex) { RecordObserverFailure(eventName, ex); }
        }
    }

    private void RecordObserverFailure(string eventName, Exception exception)
    {
        _protocolTrace.Record(new PreviewProtocolTraceEntry(
            DateTimeOffset.Now, PreviewTraceDirection.Fault,
            Volatile.Read(ref _generation), _connectionId, _phase,
            $"editor.event.{eventName}.failed", null,
            _dataSource?.CurrentVersion ?? 0, _previewVersion, _previewVersion,
            exception.Message));
        try
        {
            _errorHandler.HandleException(
                exception,
                ErrorSeverity.Error,
                "UnityPreviewObserver",
                $"Preview 事件订阅者处理失败：{eventName}",
                nameof(UnityStoryboardPreviewHost));
        }
        catch
        {
            // Error reporting is also an observer and cannot affect protocol health.
        }
    }

    private PreviewConnectionState GetConnectionState()
    {
        if (_availability == PreviewAvailabilityState.Disabled)
            return PreviewConnectionState.Disabled;
        if (_diagnostics.Any(item => item.Code == "PREVIEW_RUNTIME_OUTDATED"))
            return PreviewConnectionState.ProtocolIncompatible;
        if (_hostReady && _transport.IsConnected && _process.IsRunning)
            return PreviewConnectionState.Healthy;
        return _phase switch
        {
            PreviewSessionPhase.LaunchingProcess => PreviewConnectionState.ProcessStarting,
            PreviewSessionPhase.InitializingGraphics => PreviewConnectionState.GraphicsStarting,
            PreviewSessionPhase.ConnectingTransport => PreviewConnectionState.PipeConnecting,
            PreviewSessionPhase.AuthenticatingHost => PreviewConnectionState.Handshaking,
            PreviewSessionPhase.Disconnected => PreviewConnectionState.Disconnected,
            PreviewSessionPhase.Failed => PreviewConnectionState.Faulted,
            _ => PreviewConnectionState.Disconnected
        };
    }

    private PreviewContentState GetContentState() => _availability switch
    {
        PreviewAvailabilityState.Ready => PreviewContentState.Ready,
        PreviewAvailabilityState.ReadyWithWarnings => PreviewContentState.ReadyWithWarnings,
        PreviewAvailabilityState.InvalidData or PreviewAvailabilityState.Faulted => PreviewContentState.Failed,
        _ => _phase switch
        {
            PreviewSessionPhase.ValidatingSnapshot => PreviewContentState.Validating,
            PreviewSessionPhase.MaterializingVfs => PreviewContentState.Materializing,
            PreviewSessionPhase.LoadingContent => PreviewContentState.Loading,
            _ => PreviewContentState.Empty
        }
    };

    private void HandleUnityLog(PreviewProtocolMessage message)
    {
        var payload = message.Payload;
        var unityType = payload.Value<string>("logType") ?? "Error";
        var severity = string.Equals(unityType, "Warning", StringComparison.OrdinalIgnoreCase)
            ? PreviewDiagnosticSeverity.Warning
            : PreviewDiagnosticSeverity.Error;
        var summary = payload.Value<string>("summary")
            ?? payload.Value<string>("message")
            ?? "Unity 预览发生了未提供详情的运行时问题。";
        var diagnostic = new PreviewDiagnostic(
            "PREVIEW_UNITY_" + unityType.ToUpperInvariant(),
            summary,
            severity,
            PreviewDiagnosticSource.Unity,
            payload.Value<string>("resourcePath"),
            payload.Value<string>("entityId"),
            "请点击诊断按钮查看 Unity 完整日志和调用堆栈。")
        {
            Timestamp = ParseUnityLogTimestamp(payload),
            StackTrace = payload.Value<string>("stackTrace"),
            RepeatCount = Math.Max(1, payload.Value<int?>("repeatCount") ?? 1),
            Scene = payload.Value<string>("scene"),
            Frame = payload.Value<int?>("frame"),
            SnapshotVersion = payload.Value<long?>("snapshotVersion") ?? message.TargetPreviewVersion
        };
        AddOrMergeDiagnostic(diagnostic,
            severity == PreviewDiagnosticSeverity.Error ? PreviewAvailabilityState.InvalidData : _availability);
        if (severity == PreviewDiagnosticSeverity.Warning &&
            _phase == PreviewSessionPhase.LoadingContent)
            _activeLoadWarnings.Enqueue(diagnostic);

        var errorSeverity = string.Equals(unityType, "Assert", StringComparison.OrdinalIgnoreCase)
            ? ErrorSeverity.Critical
            : severity == PreviewDiagnosticSeverity.Warning ? ErrorSeverity.Warning : ErrorSeverity.Error;
        try
        {
            _errorHandler.HandleError(new ErrorInfo(
                errorSeverity,
                "UnityPreview",
                summary,
                "EditorPreviewBridge",
                contextData: $"Scene={diagnostic.Scene}; Frame={diagnostic.Frame}; Snapshot={diagnostic.SnapshotVersion}; Repeats={diagnostic.RepeatCount}\n{diagnostic.StackTrace}"));
        }
        catch (Exception ex)
        {
            RecordObserverFailure("ErrorHandler", ex);
        }
    }

    private static DateTimeOffset ParseUnityLogTimestamp(JObject payload)
    {
        foreach (var name in new[] { "lastOccurredAt", "lastTimestampUtc", "firstTimestampUtc" })
        {
            var text = payload.Value<string>(name);
            if (DateTimeOffset.TryParse(text, out var timestamp))
                return timestamp;
        }
        return DateTimeOffset.Now;
    }

    private void AddOrMergeDiagnostic(PreviewDiagnostic diagnostic, PreviewAvailabilityState availability)
    {
        lock (_sync)
        {
            var items = _diagnostics.ToList();
            var index = items.FindIndex(item =>
                item.Code == diagnostic.Code &&
                item.Message == diagnostic.Message &&
                item.StackTrace == diagnostic.StackTrace);
            if (index >= 0)
            {
                var current = items[index];
                items[index] = diagnostic with
                {
                    RepeatCount = Math.Max(current.RepeatCount, diagnostic.RepeatCount),
                    Timestamp = diagnostic.Timestamp
                };
            }
            else
            {
                items.Add(diagnostic);
                if (items.Count > 200)
                    items.RemoveRange(0, items.Count - 200);
            }
            _diagnostics = items;
            _availability = availability;
        }
        PublishChanged();
    }

    private static bool MatchesChartIdentity(string chartPath, JObject? actual, out string mismatch)
    {
        if (actual is null)
        {
            mismatch = "Unity 未返回谱面预检身份信息。";
            return false;
        }
        try
        {
            var json = JObject.Parse(File.ReadAllText(chartPath));
            var expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(chartPath))).ToLowerInvariant();
            var expectedNotes = (json["note_list"] as JArray)?.Count ?? 0;
            var expectedPages = (json["page_list"] as JArray)?.Count ?? 0;
            var expectedTempos = (json["tempo_list"] as JArray)?.Count ?? 0;
            var actualHash = actual.Value<string>("sha256") ?? "";
            var actualPath = actual.Value<string>("path") ?? "";
            if (string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase) &&
                expectedNotes == actual.Value<int?>("noteCount") &&
                expectedPages == actual.Value<int?>("pageCount") &&
                expectedTempos == actual.Value<int?>("tempoCount"))
            {
                mismatch = string.Empty;
                return true;
            }
            mismatch =
                $"谱面快照不一致。路径={actualPath}；编辑器 SHA-256={expectedHash}，Unity SHA-256={actualHash}；" +
                $"音符={expectedNotes}/{actual.Value<int?>("noteCount")}，页面={expectedPages}/{actual.Value<int?>("pageCount")}，BPM 段={expectedTempos}/{actual.Value<int?>("tempoCount")}。";
            return false;
        }
        catch (Exception ex)
        {
            mismatch = $"编辑器无法核对已物化谱面：{ex.Message}";
            return false;
        }
    }

    private string GetSessionId() =>
        _transportSessionId;

    public void Dispose()
    {
        Interlocked.Exchange(ref _shuttingDown, 1);
        _settings.Changed -= OnSettingsChanged;
        _process.Exited -= OnProcessExited;
        _transport.MessageReceived -= OnMessageReceived;
        _transport.ConnectionChanged -= OnConnectionChanged;
        _transport.Faulted -= OnTransportFaulted;
        _changeSubscription?.Dispose();
        _scrubTimer?.Dispose();
        _externalClockTimer?.Dispose();
        _healthTimer?.Dispose();
        _startCancellation?.Cancel();
        _startCancellation?.Dispose();
        _connectionContext?.Dispose();
        _connectionContext = null;
        _coordinatorQueue.Writer.TryComplete();
        try { _coordinatorLoop.Wait(TimeSpan.FromMilliseconds(500)); }
        catch { }
        _coordinatorLifetime.Cancel();
        _coordinatorLifetime.Dispose();
        _protocolTrace.Dispose();
        _reloadGate.Dispose();
        _startGate.Dispose();
        _teardownGate.Dispose();
    }

    private sealed record CoordinatorWork(
        string Source,
        PreviewProtocolMessage? Message,
        Func<Task> Execute);

    private sealed record PreviewConnectionContext(
        string ConnectionId,
        long HostGeneration,
        long TransportGeneration,
        string SessionId,
        string AuthenticationNonce,
        CancellationTokenSource Lifetime) : IDisposable
    {
        public void Dispose()
        {
            if (!Lifetime.IsCancellationRequested)
                Lifetime.Cancel();
            Lifetime.Dispose();
        }
    }

    private sealed record PendingVersion(
        StoryboardPreviewSnapshot Snapshot,
        PreviewVfsVersion Vfs,
        PreviewPlaybackState PlaybackState,
        TaskCompletionSource<bool> Completion,
        IReadOnlyList<PreviewDiagnostic> Diagnostics);

    private sealed class PreviewUnityRuntimeException(string message)
        : InvalidOperationException(message);

    private sealed class SilentErrorHandler : IErrorHandler
    {
        public void HandleError(ErrorInfo errorInfo) { }
        public void HandleException(Exception ex, ErrorSeverity severity, string errorType,
            string description, string location, string? contextData = null) { }
        public bool TryExecute(Action action, string errorType, string location, string? contextData = null)
        {
            action();
            return true;
        }
        public T? TryExecute<T>(Func<T> func, string errorType, string location, string? contextData = null) =>
            func();
    }
}
