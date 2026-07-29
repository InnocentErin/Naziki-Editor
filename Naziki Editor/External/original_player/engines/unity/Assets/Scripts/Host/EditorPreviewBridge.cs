#if CYTOID_EDITOR_HOST && UNITY_STANDALONE_WIN
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Windows-only transport for Naziki Editor. It is intentionally independent from
/// cytoid.game-core.v2 so editor commands cannot alter the production host contract.
/// </summary>
public sealed class EditorPreviewBridge : MonoBehaviour
{
    public const string Protocol = "naziki.editor-preview.v1";
    const int MaxMessageBytes = 64 * 1024 * 1024;
    public const int HostRevision = 4;
    static EditorPreviewBridge instance;
    static readonly object WriteLock = new object();

    readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> reliableOutgoing = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<UnityLogRecord> unityLogs = new ConcurrentQueue<UnityLogRecord>();
    readonly ConcurrentDictionary<string, UnityLogAggregate> pendingUnityLogs =
        new ConcurrentDictionary<string, UnityLogAggregate>();
    readonly ConcurrentDictionary<string, string> latestOutgoing = new ConcurrentDictionary<string, string>();
    readonly AutoResetEvent outgoingSignal = new AutoResetEvent(false);
    CancellationTokenSource lifetime;
    NamedPipeClientStream pipe;
    string sessionId;
    string nonce;
    Thread readerThread;
    Thread writerThread;
    long droppedTelemetryMessages;
    float telemetryElapsed;
    float performanceElapsed;
    int performanceFrames;
    float maximumRenderScale = 1f;
    float minimumRenderScale = .5f;
    float effectiveRenderScale = 1f;
    float frameThresholdMs = 16.67f;
    bool adaptiveQuality = true;
    int overloadedSamples;
    int stableSamples;
    int queuedUnityLogCount;
    string unityVersion;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        unityVersion = Application.unityVersion;
        sessionId = ReadArgument("--naziki-preview-session") ?? "unbound";
        nonce = ReadArgument("--naziki-preview-nonce") ?? string.Empty;
        var pipeName = ReadArgument("--naziki-preview-pipe");
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            Debug.LogError("[EditorPreview] Missing named pipe argument.");
            return;
        }

        lifetime = new CancellationTokenSource();
        Application.logMessageReceivedThreaded += OnUnityLog;
        readerThread = new Thread(() => ConnectAndRead(pipeName, lifetime.Token))
        {
            IsBackground = true,
            Name = "NazikiPreviewPipe"
        };
        readerThread.Start();
        writerThread = new Thread(() => WriteLatestMessages(lifetime.Token))
        {
            IsBackground = true,
            Name = "NazikiPreviewTelemetry"
        };
        writerThread.Start();
    }

    void Update()
    {
        while (incoming.TryDequeue(out var json))
        {
            try
            {
                var message = JObject.Parse(json);
                if (message.Value<string>("protocol") != Protocol) continue;
                var messageSession = message.Value<string>("SessionId") ??
                                     message.Value<string>("sessionId");
                if (!string.Equals(messageSession, sessionId, StringComparison.Ordinal)) continue;
                EditorPreviewController.Handle(message);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[EditorPreview] Command failed: {exception}");
            }
        }

        var game = FindObjectOfType<Game>();
        DrainUnityLogs(game);
        if (game == null || !game.IsLoaded) return;
        telemetryElapsed += UnityEngine.Time.unscaledDeltaTime;
        performanceElapsed += UnityEngine.Time.unscaledDeltaTime;
        performanceFrames++;
        if (telemetryElapsed >= 0.05f)
        {
            telemetryElapsed = 0;
            SendLatestProtocol("preview.time", Guid.NewGuid().ToString("N"),
                new JObject { ["time"] = game.Time });
        }
        if (performanceElapsed >= 1f)
        {
            var elapsed = performanceElapsed;
            performanceElapsed = 0;
            var frames = performanceFrames;
            performanceFrames = 0;
            var averageFrameMs = frames == 0 ? 0 : elapsed * 1000f / frames;
            UpdateAdaptiveQuality(averageFrameMs);
            SendLatestProtocol("preview.performance", Guid.NewGuid().ToString("N"), new JObject
            {
                ["fps"] = frames / elapsed,
                ["averageFrameMs"] = averageFrameMs,
                ["renderWidth"] = Mathf.RoundToInt(UnityEngine.Screen.width * effectiveRenderScale),
                ["renderHeight"] = Mathf.RoundToInt(UnityEngine.Screen.height * effectiveRenderScale),
                ["cacheBytes"] = GC.GetTotalMemory(false),
                ["effectiveRenderScale"] = effectiveRenderScale,
                ["suppressedExceptions"] = 0,
                ["droppedTelemetryMessages"] = Interlocked.Read(ref droppedTelemetryMessages)
            });
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        Application.logMessageReceivedThreaded -= OnUnityLog;
        lifetime?.Cancel();
        outgoingSignal.Set();
        pipe?.Dispose();
        if (readerThread != null && readerThread.IsAlive) readerThread.Join(250);
        if (writerThread != null && writerThread.IsAlive) writerThread.Join(250);
        outgoingSignal.Dispose();
        lifetime?.Dispose();
    }

    void ConnectAndRead(string pipeName, CancellationToken token)
    {
        try
        {
            pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(15000);
            SendProtocol("host.ready", Guid.NewGuid().ToString("N"), new JObject
            {
                ["authenticationNonce"] = nonce,
                ["unityVersion"] = unityVersion,
                ["hostRevision"] = HostRevision,
                ["capabilities"] = new JObject
                {
                    ["officialRuntimeDataOnly"] = true,
                    ["chartPreflightV2"] = true,
                    ["unityLogV1"] = true,
                    ["loadProgressV1"] = true,
                    ["healthCheckV1"] = true,
                    ["persistentBridgeV1"] = true
                }
            });
            var header = new byte[4];
            while (!token.IsCancellationRequested)
            {
                ReadExactly(pipe, header, token);
                var length = BitConverter.ToInt32(header, 0);
                if (length <= 0 || length > MaxMessageBytes)
                    throw new InvalidDataException($"Invalid editor frame length: {length}");
                var payload = new byte[length];
                ReadExactly(pipe, payload, token);
                incoming.Enqueue(Encoding.UTF8.GetString(payload));
            }
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
                Debug.LogError($"[EditorPreview] Pipe disconnected: {exception.Message}");
        }
    }

    static void ReadExactly(Stream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = stream.Read(buffer, offset, buffer.Length - offset);
            if (count == 0) throw new EndOfStreamException();
            offset += count;
        }
    }

    public static void SendProtocol(
        string type,
        string requestId,
        JObject payload,
        long editorVersion = 0,
        long basePreviewVersion = 0,
        long targetPreviewVersion = 0)
    {
        var target = instance;
        if (target?.pipe == null || !target.pipe.IsConnected) return;
        var message = new JObject
        {
            ["protocol"] = Protocol,
            ["Type"] = type,
            ["SessionId"] = target.sessionId,
            ["RequestId"] = requestId,
            ["EditorVersion"] = editorVersion,
            ["BasePreviewVersion"] = basePreviewVersion,
            ["TargetPreviewVersion"] = targetPreviewVersion,
            ["Payload"] = payload ?? new JObject()
        };
        target.reliableOutgoing.Enqueue(message.ToString(Formatting.None));
        target.outgoingSignal.Set();
    }

    static void SendLatestProtocol(string type, string requestId, JObject payload)
    {
        var target = instance;
        if (target?.pipe == null || !target.pipe.IsConnected) return;
        var message = new JObject
        {
            ["protocol"] = Protocol,
            ["Type"] = type,
            ["SessionId"] = target.sessionId,
            ["RequestId"] = requestId,
            ["EditorVersion"] = 0,
            ["BasePreviewVersion"] = 0,
            ["TargetPreviewVersion"] = 0,
            ["Payload"] = payload ?? new JObject()
        }.ToString(Formatting.None);
        if (target.latestOutgoing.ContainsKey(type))
            Interlocked.Increment(ref target.droppedTelemetryMessages);
        target.latestOutgoing[type] = message;
        target.outgoingSignal.Set();
    }

    void WriteLatestMessages(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            outgoingSignal.WaitOne(100);
            if (token.IsCancellationRequested) return;
            while (reliableOutgoing.TryDequeue(out var reliableMessage))
            {
                try { WriteFrame(reliableMessage); }
                catch when (token.IsCancellationRequested) { return; }
                catch
                {
                    Interlocked.Increment(ref droppedTelemetryMessages);
                    return;
                }
            }
            foreach (var item in pendingUnityLogs.ToArray())
            {
                if (!pendingUnityLogs.TryRemove(item.Key, out var entry)) continue;
                try
                {
                    SendProtocol("preview.unityLog", Guid.NewGuid().ToString("N"),
                        entry.ToPayload());
                }
                catch when (token.IsCancellationRequested) { return; }
                catch { Interlocked.Increment(ref droppedTelemetryMessages); }
            }
            foreach (var item in latestOutgoing.ToArray())
            {
                if (!latestOutgoing.TryRemove(item.Key, out var json)) continue;
                try { WriteFrame(json); }
                catch when (token.IsCancellationRequested) { return; }
                catch { Interlocked.Increment(ref droppedTelemetryMessages); }
            }
        }
    }

    void OnUnityLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log)
            return;
        if (condition != null &&
            (condition.Contains("[EditorPreview] Pipe disconnected") ||
             condition.Contains("preview.unityLog")))
            return;
        if (Interlocked.Increment(ref queuedUnityLogCount) > 256)
        {
            Interlocked.Decrement(ref queuedUnityLogCount);
            Interlocked.Increment(ref droppedTelemetryMessages);
            return;
        }
        unityLogs.Enqueue(new UnityLogRecord(condition ?? string.Empty,
            stackTrace ?? string.Empty, type, DateTime.UtcNow));
    }

    void DrainUnityLogs(Game game)
    {
        while (unityLogs.TryDequeue(out var record))
        {
            Interlocked.Decrement(ref queuedUnityLogCount);
            var fingerprint = record.Type + "\n" + record.Message + "\n" + record.StackTrace;
            pendingUnityLogs.AddOrUpdate(
                fingerprint,
                _ => new UnityLogAggregate(
                    record,
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    UnityEngine.Time.frameCount,
                    game != null && game.IsLoaded ? game.Time : 0,
                    EditorPreviewController.CurrentPreviewVersion,
                    unityVersion),
                (_, existing) => existing.Increment(record.UtcTimestamp));
        }
        if (!pendingUnityLogs.IsEmpty)
            outgoingSignal.Set();
    }

    sealed class UnityLogRecord
    {
        public readonly string Message;
        public readonly string StackTrace;
        public readonly LogType Type;
        public readonly DateTime UtcTimestamp;
        public UnityLogRecord(string message, string stackTrace, LogType type, DateTime utcTimestamp) =>
            (Message, StackTrace, Type, UtcTimestamp) = (message, stackTrace, type, utcTimestamp);
    }

    sealed class UnityLogAggregate
    {
        readonly UnityLogRecord first;
        readonly string scene;
        readonly int frame;
        readonly float previewTime;
        readonly long snapshotVersion;
        readonly string unityVersion;
        int count = 1;
        DateTime lastTimestamp;

        public UnityLogAggregate(UnityLogRecord first, string scene, int frame,
            float previewTime, long snapshotVersion, string unityVersion)
        {
            this.first = first;
            this.scene = scene;
            this.frame = frame;
            this.previewTime = previewTime;
            this.snapshotVersion = snapshotVersion;
            this.unityVersion = unityVersion;
            lastTimestamp = first.UtcTimestamp;
        }

        public UnityLogAggregate Increment(DateTime timestamp)
        {
            Interlocked.Increment(ref count);
            lastTimestamp = timestamp;
            return this;
        }

        public JObject ToPayload() => new JObject
        {
            ["severity"] = first.Type == LogType.Warning ? "Warning" :
                first.Type == LogType.Assert ? "Critical" : "Error",
            ["logType"] = first.Type.ToString(),
            ["message"] = first.Message,
            ["stackTrace"] = first.StackTrace,
            ["scene"] = scene,
            ["frame"] = frame,
            ["previewTime"] = previewTime,
            ["snapshotVersion"] = snapshotVersion,
            ["repeatCount"] = Math.Max(1, Volatile.Read(ref count)),
            ["firstTimestampUtc"] = first.UtcTimestamp.ToString("O"),
            ["lastTimestampUtc"] = lastTimestamp.ToString("O"),
            ["unityVersion"] = unityVersion
        };
    }

    void WriteFrame(string json)
    {
        if (pipe == null || !pipe.IsConnected) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxMessageBytes) throw new InvalidDataException("Editor message too large.");
        var header = BitConverter.GetBytes(bytes.Length);
        lock (WriteLock)
        {
            pipe.Write(header, 0, header.Length);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
        }
    }

    public static void SendLegacyTelemetry(string json)
    {
        // Production telemetry remains available for diagnostics but is wrapped so the
        // editor protocol parser never confuses it with a command response.
        SendProtocol("preview.telemetry", Guid.NewGuid().ToString("N"),
            new JObject { ["cytoidGameCoreV2"] = json });
    }

    public static void NotifyPreviewPausedAtEnd(float duration)
    {
        SendProtocol("preview.time", Guid.NewGuid().ToString("N"),
            new JObject { ["time"] = duration });
        SendProtocol("preview.state", Guid.NewGuid().ToString("N"), new JObject
        {
            ["state"] = "Paused",
            ["reason"] = "endOfLevel",
            ["time"] = duration,
            ["duration"] = duration
        });
    }

    public static void ConfigureAdaptiveQuality(
        float maximumScale,
        float minimumScale,
        bool enabled,
        float thresholdMs)
    {
        if (instance == null) return;
        instance.maximumRenderScale = Mathf.Clamp(maximumScale, .5f, 1.25f);
        instance.minimumRenderScale = Mathf.Clamp(minimumScale, .5f, instance.maximumRenderScale);
        instance.effectiveRenderScale = instance.maximumRenderScale;
        instance.adaptiveQuality = enabled;
        instance.frameThresholdMs = Mathf.Clamp(thresholdMs, 8f, 50f);
        instance.overloadedSamples = 0;
        instance.stableSamples = 0;
        ScalableBufferManager.ResizeBuffers(instance.effectiveRenderScale, instance.effectiveRenderScale);
    }

    void UpdateAdaptiveQuality(float averageFrameMs)
    {
        if (!adaptiveQuality || averageFrameMs <= 0) return;
        if (averageFrameMs > frameThresholdMs * 1.1f)
        {
            stableSamples = 0;
            if (++overloadedSamples >= 2 && effectiveRenderScale > minimumRenderScale)
            {
                overloadedSamples = 0;
                effectiveRenderScale = Mathf.Max(minimumRenderScale, effectiveRenderScale - .25f);
                ScalableBufferManager.ResizeBuffers(effectiveRenderScale, effectiveRenderScale);
            }
        }
        else if (averageFrameMs < frameThresholdMs * .85f)
        {
            overloadedSamples = 0;
            if (++stableSamples >= 8 && effectiveRenderScale < maximumRenderScale)
            {
                stableSamples = 0;
                effectiveRenderScale = Mathf.Min(maximumRenderScale, effectiveRenderScale + .25f);
                ScalableBufferManager.ResizeBuffers(effectiveRenderScale, effectiveRenderScale);
            }
        }
        else
        {
            overloadedSamples = 0;
            stableSamples = 0;
        }
    }

    static string ReadArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }
}
#endif
