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
    static EditorPreviewBridge instance;
    static readonly object WriteLock = new object();

    readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
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

    void Awake()
    {
        instance = this;
        sessionId = ReadArgument("--naziki-preview-session") ?? "unbound";
        nonce = ReadArgument("--naziki-preview-nonce") ?? string.Empty;
        var pipeName = ReadArgument("--naziki-preview-pipe");
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            Debug.LogError("[EditorPreview] Missing named pipe argument.");
            return;
        }

        lifetime = new CancellationTokenSource();
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
                ["unityVersion"] = Application.unityVersion
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
        target.WriteFrame(message.ToString(Formatting.None));
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
            foreach (var item in latestOutgoing.ToArray())
            {
                if (!latestOutgoing.TryRemove(item.Key, out var json)) continue;
                try { WriteFrame(json); }
                catch when (token.IsCancellationRequested) { return; }
                catch { Interlocked.Increment(ref droppedTelemetryMessages); }
            }
        }
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
