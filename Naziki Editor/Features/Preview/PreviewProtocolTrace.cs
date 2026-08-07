using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Channels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Features.Preview;

internal enum PreviewTraceDirection
{
    Inbound,
    Outbound,
    Lifecycle,
    Fault
}

internal sealed record PreviewProtocolTraceEntry(
    DateTimeOffset Timestamp,
    PreviewTraceDirection Direction,
    long Generation,
    string ConnectionId,
    PreviewSessionPhase Phase,
    string Type,
    string? RequestId,
    long EditorVersion,
    long BasePreviewVersion,
    long TargetPreviewVersion,
    string? Detail = null);

/// <summary>
/// Records protocol metadata only. Payloads and authentication nonces are
/// deliberately excluded so a diagnostics log cannot copy project data.
/// </summary>
internal sealed class PreviewProtocolTrace : IDisposable
{
    private const int MaximumEntries = 200;
    private readonly object _sync = new();
    private readonly Queue<PreviewProtocolTraceEntry> _entries = new();
    private readonly Channel<PreviewProtocolTraceEntry> _fileQueue;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _writer;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastPersistedTelemetry = new();

    public PreviewProtocolTrace()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NazikiEditor",
            "Logs");
        FilePath = Path.Combine(directory, $"preview-{DateTime.Now:yyyyMMdd}.log");
        _fileQueue = Channel.CreateBounded<PreviewProtocolTraceEntry>(
            new BoundedChannelOptions(512)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            });
        _writer = WriteLoopAsync(directory, _lifetime.Token);
    }

    public string FilePath { get; }

    public void Record(PreviewProtocolTraceEntry entry)
    {
        lock (_sync)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > MaximumEntries)
                _entries.Dequeue();
        }

        // High-frequency telemetry remains available in the in-memory ring,
        // while disk persistence is coalesced to one entry per type per second.
        if (entry.Type is "preview.time" or "preview.performance")
        {
            var previous = _lastPersistedTelemetry.GetOrAdd(entry.Type, DateTimeOffset.MinValue);
            if (entry.Timestamp - previous < TimeSpan.FromSeconds(1))
                return;
            _lastPersistedTelemetry[entry.Type] = entry.Timestamp;
        }
        _fileQueue.Writer.TryWrite(entry);
    }

    public IReadOnlyList<PreviewProtocolTraceEntry> Snapshot()
    {
        lock (_sync)
            return _entries.ToArray();
    }

    public string DescribeRecent(int maximum = 12)
    {
        var entries = Snapshot();
        return string.Join(
            Environment.NewLine,
            entries.Skip(Math.Max(0, entries.Count - Math.Max(1, maximum))).Select(entry =>
                $"{entry.Timestamp:O} {entry.Direction} g={entry.Generation} " +
                $"phase={entry.Phase} type={entry.Type} request={entry.RequestId ?? "-"} " +
                $"version={entry.BasePreviewVersion}->{entry.TargetPreviewVersion}" +
                (string.IsNullOrWhiteSpace(entry.Detail) ? string.Empty : $" detail={entry.Detail}")));
    }

    private async Task WriteLoopAsync(string directory, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(directory);
            await using var stream = new FileStream(
                FilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                16 * 1024,
                FileOptions.Asynchronous);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await foreach (var entry in _fileQueue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var json = new JObject
                {
                    ["timestamp"] = entry.Timestamp.ToString("O"),
                    ["direction"] = entry.Direction.ToString(),
                    ["generation"] = entry.Generation,
                    ["connectionId"] = entry.ConnectionId,
                    ["phase"] = entry.Phase.ToString(),
                    ["type"] = entry.Type,
                    ["requestId"] = entry.RequestId,
                    ["editorVersion"] = entry.EditorVersion,
                    ["basePreviewVersion"] = entry.BasePreviewVersion,
                    ["targetPreviewVersion"] = entry.TargetPreviewVersion,
                    ["detail"] = entry.Detail
                };
                await writer.WriteLineAsync(json.ToString(Formatting.None)).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            // Tracing is diagnostic-only and must never alter Preview health.
        }
    }

    public void Dispose()
    {
        _fileQueue.Writer.TryComplete();
        try { _writer.Wait(TimeSpan.FromMilliseconds(500)); }
        catch { }
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
