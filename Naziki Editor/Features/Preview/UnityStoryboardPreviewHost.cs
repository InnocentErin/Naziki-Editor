using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.IO;
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
    private readonly ConcurrentDictionary<string, PendingVersion> _pendingVersions = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PreviewProtocolMessage>> _pendingCommands = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private IDisposable? _changeSubscription;
    private IStoryboardPreviewDataSource? _dataSource;
    private ProjectDataContext? _context;
    private IntPtr _parentWindow;
    private int _pixelWidth = 1;
    private int _pixelHeight = 1;
    private string _authenticationNonce = Guid.NewGuid().ToString("N");
    private string _transportSessionId = "unbound";
    private string _sessionId = "unbound";
    private CancellationTokenSource? _startCancellation;
    private TaskCompletionSource<bool>? _hostReadyCompletion;
    private Timer? _healthTimer;
    private int _healthCheckInFlight;
    private int _shuttingDown;
    private long _generation;
    private int _hostRevision;
    private DateTimeOffset _phaseStartedAt = DateTimeOffset.Now;
    private DateTimeOffset _lastMessageAt = DateTimeOffset.Now;
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
        _transport.MessageReceived += OnMessageReceived;
        _transport.ConnectionChanged += OnConnectionChanged;
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

    public bool IsAvailable => _availability == PreviewAvailabilityState.Ready && _hostReady;
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
        _phaseDetail);
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
                    new JObject()), shutdownTimeout.Token).ConfigureAwait(false);
            }
            catch { }
        }
        await _process.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        await _transport.StopAsync().ConfigureAwait(false);
        _hostReady = false;
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
                await _process.StopAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                await _transport.StopAsync().ConfigureAwait(false);
            }

            var generation = Interlocked.Increment(ref _generation);
            _hostReady = false;
            _hostRevision = 0;
            _hostReadyCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TransitionTo(PreviewSessionPhase.LaunchingProcess);
            SetDiagnostics(PreviewAvailabilityState.Starting, []);
            _startCancellation?.Cancel();
            _startCancellation?.Dispose();
            _startCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = _startCancellation.Token;
            _authenticationNonce = Guid.NewGuid().ToString("N");
            _transportSessionId = Guid.NewGuid().ToString("N");
            var connectionTask = _transport.StartAsync(token);
            TransitionTo(PreviewSessionPhase.InitializingGraphics);
            await _process.StartAsync(new UnityPreviewLaunchOptions(
                _parentWindow,
                _transportSessionId,
                _transport.PipeName,
                _authenticationNonce,
                _pixelWidth,
                _pixelHeight,
                _settings.Current.RenderThreads), token).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _generation))
                throw new OperationCanceledException("Preview generation was superseded.");
            TransitionTo(PreviewSessionPhase.ConnectingTransport);
            SetDiagnostics(PreviewAvailabilityState.Connecting, []);
            await connectionTask.ConfigureAwait(false);
            TransitionTo(PreviewSessionPhase.AuthenticatingHost);
            await _hostReadyCompletion.Task.WaitAsync(token).ConfigureAwait(false);
            _startCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            EnsureHealthTimer();
        }
        catch (FileNotFoundException ex)
        {
            await _transport.StopAsync().ConfigureAwait(false);
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
            var handshakeFailure = _diagnostics.FirstOrDefault()?.Code is
                    "PREVIEW_AUTH_FAILED" or "PREVIEW_RUNTIME_OUTDATED" ||
                ex is InvalidDataException &&
                (ex.Message.Contains("authentication failed", StringComparison.Ordinal) ||
                 ex.Message.Contains("host revision or capabilities", StringComparison.Ordinal));
            if (handshakeFailure)
            {
                await _process.StopAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                await _transport.StopAsync().ConfigureAwait(false);
                TransitionTo(PreviewSessionPhase.Failed);
                return;
            }
            await _transport.StopAsync().ConfigureAwait(false);
            TransitionTo(PreviewSessionPhase.Failed);
            var startFailureCode = !_process.IsRunning
                ? "PREVIEW_LAUNCH_FAILED"
                : !_process.IsGraphicsReady
                    ? "PREVIEW_GRAPHICS_FAILED"
                    : !_transport.IsConnected
                        ? "PREVIEW_CONNECTION_FAILED"
                        : "PREVIEW_HOST_READY_TIMEOUT";
            SetDiagnostics(PreviewAvailabilityState.Faulted,
                [new PreviewDiagnostic(
                    startFailureCode,
                    $"原生预览启动失败：{ex.Message}",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Transport)]);
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
            _pendingVersions[requestId] = new PendingVersion(snapshot, materialized, State, completion);
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
        catch (Exception ex)
        {
            if (ex is PreviewUnityRuntimeException)
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
            _pendingVersions[requestId] = new PendingVersion(snapshot, materialized, State, completion);
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
        catch (Exception ex)
        {
            if (ex is PreviewUnityRuntimeException)
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
        catch (Exception ex)
        {
            SetDiagnostics(PreviewAvailabilityState.Disconnected,
                [new PreviewDiagnostic(
                    "PREVIEW_SEND_FAILED",
                    $"预览通信中断：{ex.Message}",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Transport)]);
        }
    }

    private Task SendCommandAsync(
        string type,
        JObject payload,
        string? requestId = null,
        long? editorVersion = null,
        long? basePreviewVersion = null,
        long? targetPreviewVersion = null) =>
        _transport.SendAsync(new PreviewProtocolMessage(
            type,
            GetSessionId(),
            requestId ?? Guid.NewGuid().ToString("N"),
            editorVersion ?? _dataSource?.CurrentVersion ?? 0,
            basePreviewVersion ?? _previewVersion,
            targetPreviewVersion ?? _previewVersion,
            payload));

    private void OnMessageReceived(object? sender, PreviewProtocolMessage message)
    {
        if (!string.Equals(message.SessionId, GetSessionId(), StringComparison.Ordinal))
            return;
        _lastMessageAt = DateTimeOffset.Now;
        switch (message.Type)
        {
            case "host.ready":
                if (!string.Equals(
                        message.Payload.Value<string>("authenticationNonce"),
                        _authenticationNonce,
                        StringComparison.Ordinal))
                {
                    _hostReadyCompletion?.TrySetException(
                        new InvalidDataException("Unity Preview authentication failed."));
                    SetDiagnostics(PreviewAvailabilityState.Faulted,
                        [new PreviewDiagnostic(
                            "PREVIEW_AUTH_FAILED",
                            "Unity Preview 握手认证失败。",
                            PreviewDiagnosticSeverity.Error,
                            PreviewDiagnosticSource.Transport)]);
                    return;
                }
                var hostRevision = message.Payload.Value<int?>("hostRevision") ?? 0;
                var capabilitiesToken = message.Payload["capabilities"];
                bool HasCapability(string name) =>
                    capabilitiesToken is JObject capabilityObject
                        ? capabilityObject.Value<bool?>(name) == true
                        : capabilitiesToken is JArray capabilityArray &&
                          capabilityArray.Values<string>().Contains(name, StringComparer.Ordinal);
                if (hostRevision < 3 ||
                    !HasCapability("officialRuntimeDataOnly") ||
                    !HasCapability("chartPreflightV2") ||
                    !HasCapability("unityLogV1") ||
                    !HasCapability("loadProgressV1") ||
                    !HasCapability("healthCheckV1"))
                {
                    _hostReadyCompletion?.TrySetException(
                        new InvalidDataException("Unity Preview host revision or capabilities are incompatible."));
                    SetDiagnostics(PreviewAvailabilityState.Faulted,
                        [new PreviewDiagnostic(
                            "PREVIEW_RUNTIME_OUTDATED",
                            "Unity 预览播放器版本过旧，缺少正式数据边界、谱面预检或日志通道能力。",
                            PreviewDiagnosticSeverity.Error,
                            PreviewDiagnosticSource.Unity,
                            Suggestion: "请使用 Unity 6000.0.80f1 重新构建 Windows Editor Preview。")]);
                    return;
                }
                _hostRevision = hostRevision;
                _hostReady = true;
                _automaticRestartCount = 0;
                TransitionTo(PreviewSessionPhase.HostReady);
                SetDiagnostics(PreviewAvailabilityState.Connecting, []);
                _hostReadyCompletion?.TrySetResult(true);
                _ = SendCommandAsync("preview.settings.apply",
                    new JObject
                    {
                        ["settings"] = JObject.FromObject(_settings.Current),
                        ["pixelWidth"] = _pixelWidth,
                        ["pixelHeight"] = _pixelHeight,
                        ["active"] = true
                    });
                if (_lastKnownGood is not null)
                    ApplySnapshot(_lastKnownGood.Snapshot with { PlaybackTime = CurrentTime });
                else if (!_changeInFlight && _context is not null && _dataSource is not null)
                    ApplySnapshot(_dataSource.GetSnapshot(_context, CurrentTime));
                break;
            case "preview.load.started":
                TransitionTo(
                    PreviewSessionPhase.LoadingContent,
                    message.RequestId,
                    message.TargetPreviewVersion);
                SetDiagnostics(PreviewAvailabilityState.Connecting, []);
                break;
            case "preview.load.progress":
                TransitionTo(
                    PreviewSessionPhase.LoadingContent,
                    message.RequestId,
                    message.TargetPreviewVersion,
                    message.Payload.Value<string>("stage"));
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case "preview.load.ready":
                var readyTime = message.Payload.Value<double?>("time");
                var readyDuration = message.Payload.Value<double?>("duration");
                if (readyTime.HasValue)
                    Volatile.Write(ref _currentTime, Math.Max(0, readyTime.Value));
                if (readyDuration.HasValue)
                    Volatile.Write(ref _duration, Math.Max(0, readyDuration.Value));
                AcceptPending(message);
                break;
            case "preview.load.failed":
                RejectPending(message);
                break;
            case "preview.health.ok":
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
                TimeChanged?.Invoke(this, time);
                break;
            case "preview.state":
                var reportedTime = message.Payload.Value<double?>("time");
                var reportedDuration = message.Payload.Value<double?>("duration");
                if (reportedTime.HasValue)
                {
                    Volatile.Write(ref _currentTime, Math.Max(0, reportedTime.Value));
                    TimeChanged?.Invoke(this, CurrentTime);
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
                Changed?.Invoke(this, EventArgs.Empty);
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
            SetDiagnostics(PreviewAvailabilityState.Ready, []);
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

    private void OnConnectionChanged(object? sender, PreviewTransportStateChanged state)
    {
        if (Volatile.Read(ref _shuttingDown) != 0)
            return;
        if (state.Generation != _transport.Generation)
            return;
        if (!state.Connected && _process.IsRunning)
        {
            _hostReady = false;
            _hostReadyCompletion?.TrySetException(
                state.Exception ?? new IOException(state.Reason));
            TransitionTo(PreviewSessionPhase.Disconnected);
            SetDiagnostics(PreviewAvailabilityState.Disconnected,
                [new PreviewDiagnostic(
                    "PREVIEW_CONNECTION_LOST",
                    "与 Unity Preview 的连接已断开。",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Transport)]);
        }
    }

    private void OnProcessExited(object? sender, UnityPreviewProcessExited processExit)
    {
        if (Volatile.Read(ref _shuttingDown) != 0)
            return;
        if (processExit.Generation != _process.Generation || processExit.Expected)
            return;
        _pendingRestore = CaptureRestorePoint();
        _hostReady = false;
        _hostReadyCompletion?.TrySetException(
            new InvalidOperationException("Unity Preview exited before the operation completed."));
        Pause();
        TransitionTo(PreviewSessionPhase.Failed);
        SetDiagnostics(PreviewAvailabilityState.Faulted,
            [new PreviewDiagnostic(
                "PREVIEW_PROCESS_EXITED",
                $"Unity Preview 已异常退出（退出码 {processExit.ExitCode?.ToString() ?? "unknown"}）。",
                PreviewDiagnosticSeverity.Error,
                PreviewDiagnosticSource.Unity)]);
        if (_automaticRestartCount++ == 0)
            _ = RestartAsync();
    }

    private async Task RestartAsync()
    {
        _pendingRestore ??= CaptureRestorePoint();
        _pendingVersions.Clear();
        lock (_sync)
        {
            _queuedSnapshot = null;
            _queuedChanges = null;
            _changeInFlight = false;
        }
        await _process.StopAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        await _transport.StopAsync().ConfigureAwait(false);
        _hostReady = false;
        await EnsureStartedAsync().ConfigureAwait(false);
    }

    private async Task RestartForNewSessionAsync()
    {
        _pendingRestore = null;
        _lastKnownGood = null;
        _previewVersion = 0;
        _pendingVersions.Clear();
        lock (_sync)
        {
            _queuedSnapshot = null;
            _queuedChanges = null;
            _changeInFlight = false;
        }
        await _process.StopAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        await _transport.StopAsync().ConfigureAwait(false);
        _hostReady = false;
    }

    private void OnSettingsChanged(object? sender, PreviewSettings settings)
    {
        var previous = _lastAppliedSettings;
        _lastAppliedSettings = settings;
        if (!settings.HardwareAcceleration)
        {
            _ = DisableForSettingsAsync();
            return;
        }
        if (!previous.HardwareAcceleration || previous.RenderThreads != settings.RenderThreads)
        {
            _ = RestartAsync();
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
        StateChanged?.Invoke(this, state);
    }

    private async Task SendCommandAndWaitAsync(string type, JObject payload, TimeSpan timeout)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<PreviewProtocolMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommands[requestId] = completion;
        try
        {
            await SendCommandAsync(type, payload, requestId).ConfigureAwait(false);
            await completion.Task.WaitAsync(timeout).ConfigureAwait(false);
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
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureHealthTimer()
    {
        _healthTimer ??= new Timer(
            _ => _ = RunHealthCheckAsync(),
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));
    }

    private async Task RunHealthCheckAsync()
    {
        if (!_hostReady || !_transport.IsConnected ||
            _phase != PreviewSessionPhase.LoadingContent ||
            DateTimeOffset.Now - _lastMessageAt < TimeSpan.FromSeconds(10) ||
            Interlocked.Exchange(ref _healthCheckInFlight, 1) != 0)
            return;
        try
        {
            await SendCommandAndWaitAsync(
                "preview.health.check",
                new JObject { ["generation"] = _generation },
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            _lastMessageAt = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            var failure = new TimeoutException(
                "Unity Preview stopped responding while loading content.", ex);
            foreach (var item in _pendingVersions.ToArray())
                if (_pendingVersions.TryRemove(item.Key, out var pending))
                    pending.Completion.TrySetException(failure);
            TransitionTo(PreviewSessionPhase.Failed);
            SetDiagnostics(PreviewAvailabilityState.Faulted,
                [new PreviewDiagnostic(
                    "PREVIEW_RUNTIME_UNRESPONSIVE",
                    failure.Message,
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Unity,
                    Suggestion: "Restart Unity Preview and inspect the Unity log for the last loading stage.")]);
            CompleteChangeQueue();
        }
        finally
        {
            Volatile.Write(ref _healthCheckInFlight, 0);
        }
    }

    private void RestorePlayback(PreviewPlaybackRestorePoint restore)
    {
        _clockMode = restore.ClockMode;
        Volatile.Write(ref _currentTime, Math.Max(0, restore.Time));
        SetClockMode(restore.ClockMode);
        Seek(restore.Time);
        SetPlaybackState(restore.State);
        TimeChanged?.Invoke(this, CurrentTime);
    }

    private void SetDiagnostics(PreviewAvailabilityState availability, IReadOnlyList<PreviewDiagnostic> diagnostics)
    {
        _availability = availability;
        _diagnostics = diagnostics;
        Changed?.Invoke(this, EventArgs.Empty);
    }

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
            Timestamp = payload.Value<DateTimeOffset?>("lastOccurredAt")
                ?? payload.Value<DateTimeOffset?>("lastTimestampUtc")
                ?? DateTimeOffset.Now,
            StackTrace = payload.Value<string>("stackTrace"),
            RepeatCount = Math.Max(1, payload.Value<int?>("repeatCount") ?? 1),
            Scene = payload.Value<string>("scene"),
            Frame = payload.Value<int?>("frame"),
            SnapshotVersion = payload.Value<long?>("snapshotVersion") ?? message.TargetPreviewVersion
        };
        AddOrMergeDiagnostic(diagnostic,
            severity == PreviewDiagnosticSeverity.Error ? PreviewAvailabilityState.InvalidData : _availability);

        var errorSeverity = string.Equals(unityType, "Assert", StringComparison.OrdinalIgnoreCase)
            ? ErrorSeverity.Critical
            : severity == PreviewDiagnosticSeverity.Warning ? ErrorSeverity.Warning : ErrorSeverity.Error;
        _errorHandler.HandleError(new ErrorInfo(
            errorSeverity,
            "UnityPreview",
            summary,
            "EditorPreviewBridge",
            contextData: $"Scene={diagnostic.Scene}; Frame={diagnostic.Frame}; Snapshot={diagnostic.SnapshotVersion}; Repeats={diagnostic.RepeatCount}\n{diagnostic.StackTrace}"));
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
        Changed?.Invoke(this, EventArgs.Empty);
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
        _settings.Changed -= OnSettingsChanged;
        _process.Exited -= OnProcessExited;
        _transport.MessageReceived -= OnMessageReceived;
        _transport.ConnectionChanged -= OnConnectionChanged;
        _changeSubscription?.Dispose();
        _scrubTimer?.Dispose();
        _externalClockTimer?.Dispose();
        _healthTimer?.Dispose();
        _startCancellation?.Cancel();
        _startCancellation?.Dispose();
        _reloadGate.Dispose();
        _startGate.Dispose();
    }

    private sealed record PendingVersion(
        StoryboardPreviewSnapshot Snapshot,
        PreviewVfsVersion Vfs,
        PreviewPlaybackState PlaybackState,
        TaskCompletionSource<bool> Completion);

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
