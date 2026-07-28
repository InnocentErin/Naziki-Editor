using System.Collections.Concurrent;
using System.IO;
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
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, PendingVersion> _pendingVersions = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PreviewProtocolMessage>> _pendingCommands = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private IDisposable? _changeSubscription;
    private IStoryboardPreviewDataSource? _dataSource;
    private ProjectDataContext? _context;
    private IntPtr _parentWindow;
    private int _pixelWidth = 1;
    private int _pixelHeight = 1;
    private string _authenticationNonce = Guid.NewGuid().ToString("N");
    private string _sessionId = "unbound";
    private CancellationTokenSource? _startCancellation;
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
        IPreviewSettingsProvider settings)
    {
        _transport = transport;
        _process = process;
        _vfs = vfs;
        _validator = validator;
        _settings = settings;
        _lastAppliedSettings = settings.Current;
        _transport.MessageReceived += OnMessageReceived;
        _transport.ConnectionChanged += OnConnectionChanged;
        _process.Exited += OnProcessExited;
        _settings.Changed += OnSettingsChanged;
    }

    public bool IsAvailable => _availability == PreviewAvailabilityState.Ready && _hostReady;
    public double CurrentTime => Volatile.Read(ref _currentTime);
    public double Duration => Volatile.Read(ref _duration);
    public PreviewPlaybackState State => _state;
    public PreviewAvailabilityState Availability => _availability;
    public IReadOnlyList<PreviewDiagnostic> Diagnostics => _diagnostics;
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
        _scrubTimer?.Dispose();
        _scrubTimer = null;
        _externalClockTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        lock (_sync)
            _pendingExternalClockTime = null;
        if (_transport.IsConnected)
        {
            try
            {
                await SendCommandAsync("host.shutdown", new JObject()).ConfigureAwait(false);
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
        if (_process.IsRunning && _transport.IsConnected)
            return;
        if (_parentWindow == IntPtr.Zero)
            return;

        SetDiagnostics(PreviewAvailabilityState.Starting, []);
        _startCancellation?.Cancel();
        _startCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = _startCancellation.Token;
        try
        {
            _authenticationNonce = Guid.NewGuid().ToString("N");
            var connectionTask = _transport.StartAsync(token);
            await _process.StartAsync(new UnityPreviewLaunchOptions(
                _parentWindow,
                _context is null ? Guid.NewGuid().ToString("N") : GetSessionId(),
                _transport.PipeName,
                _authenticationNonce,
                _pixelWidth,
                _pixelHeight,
                _settings.Current.RenderThreads), token).ConfigureAwait(false);
            SetDiagnostics(PreviewAvailabilityState.Connecting, []);
            await connectionTask.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
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
            await _transport.StopAsync().ConfigureAwait(false);
            SetDiagnostics(PreviewAvailabilityState.Faulted,
                [new PreviewDiagnostic(
                    "PREVIEW_START_FAILED",
                    $"原生预览启动失败：{ex.Message}",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Transport)]);
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
            var validation = _validator.Validate(context, snapshot);
            if (!validation.IsValid)
            {
                Pause();
                SetDiagnostics(PreviewAvailabilityState.InvalidData, validation.Diagnostics);
                return;
            }

            await EnsureStartedAsync().ConfigureAwait(false);
            if (!_transport.IsConnected)
                return;
            var materialized = await _vfs.MaterializeAsync(snapshot).ConfigureAwait(false);
            var requestId = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingVersions[requestId] = new PendingVersion(snapshot, materialized, State, completion);
            await SendCommandAsync(command, new JObject
            {
                ["vfsRoot"] = materialized.Directory,
                ["level"] = Path.GetFileName(materialized.LevelPath),
                ["time"] = snapshot.PlaybackTime,
                ["settings"] = JObject.FromObject(_settings.Current),
                ["authenticationNonce"] = _authenticationNonce
            }, requestId, snapshot.Version, _previewVersion, snapshot.Version).ConfigureAwait(false);
            commandSent = true;
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(35)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
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
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(35)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
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
        switch (message.Type)
        {
            case "host.ready":
                if (!string.Equals(
                        message.Payload.Value<string>("authenticationNonce"),
                        _authenticationNonce,
                        StringComparison.Ordinal))
                {
                    SetDiagnostics(PreviewAvailabilityState.Faulted,
                        [new PreviewDiagnostic(
                            "PREVIEW_AUTH_FAILED",
                            "Unity Preview 握手认证失败。",
                            PreviewDiagnosticSeverity.Error,
                            PreviewDiagnosticSource.Transport)]);
                    return;
                }
                _hostReady = true;
                _automaticRestartCount = 0;
                SetDiagnostics(PreviewAvailabilityState.Ready, []);
                _ = SendCommandIfAvailableAsync("preview.settings.apply",
                    new JObject
                    {
                        ["settings"] = JObject.FromObject(_settings.Current),
                        ["pixelWidth"] = _pixelWidth,
                        ["pixelHeight"] = _pixelHeight,
                        ["active"] = true
                    });
                if (_lastKnownGood is not null)
                    ApplySnapshot(_lastKnownGood.Snapshot with { PlaybackTime = CurrentTime });
                break;
            case "preview.ack":
                AcceptPending(message.RequestId, message.TargetPreviewVersion);
                CompletePendingCommand(message);
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
            case "preview.error":
                RejectPending(message);
                break;
        }
    }

    private void AcceptPending(string requestId, long targetVersion)
    {
        if (_pendingVersions.TryRemove(requestId, out var pending))
        {
            _previewVersion = targetVersion;
            _lastKnownGood = new LastKnownGoodPreview(
                pending.Snapshot,
                pending.Vfs.Directory,
                DateTimeOffset.UtcNow,
                CurrentTime,
                pending.PlaybackState);
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
        if (_pendingCommands.TryRemove(message.RequestId, out var command))
            command.TrySetException(new InvalidOperationException(
                message.Payload.Value<string>("message") ?? "Unity rejected the preview command."));
        if (_pendingVersions.TryRemove(message.RequestId, out var pending))
            pending.Completion.TrySetException(new InvalidOperationException(
                message.Payload.Value<string>("message") ?? "Unity rejected the preview version."));
        Pause();
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

    private void OnConnectionChanged(object? sender, bool connected)
    {
        if (!connected && _process.IsRunning)
            SetDiagnostics(PreviewAvailabilityState.Disconnected,
                [new PreviewDiagnostic(
                    "PREVIEW_CONNECTION_LOST",
                    "与 Unity Preview 的连接已断开。",
                    PreviewDiagnosticSeverity.Error,
                    PreviewDiagnosticSource.Transport)]);
    }

    private void OnProcessExited(object? sender, int? exitCode)
    {
        _pendingRestore = CaptureRestorePoint();
        _hostReady = false;
        Pause();
        SetDiagnostics(PreviewAvailabilityState.Faulted,
            [new PreviewDiagnostic(
                "PREVIEW_PROCESS_EXITED",
                $"Unity Preview 已异常退出（退出码 {exitCode?.ToString() ?? "unknown"}）。",
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

    private string GetSessionId() =>
        _sessionId;

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _process.Exited -= OnProcessExited;
        _transport.MessageReceived -= OnMessageReceived;
        _transport.ConnectionChanged -= OnConnectionChanged;
        _changeSubscription?.Dispose();
        _scrubTimer?.Dispose();
        _externalClockTimer?.Dispose();
        _startCancellation?.Cancel();
        _startCancellation?.Dispose();
        _reloadGate.Dispose();
    }

    private sealed record PendingVersion(
        StoryboardPreviewSnapshot Snapshot,
        PreviewVfsVersion Vfs,
        PreviewPlaybackState PlaybackState,
        TaskCompletionSource<bool> Completion);
}
