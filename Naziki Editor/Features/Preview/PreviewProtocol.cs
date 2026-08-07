using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Features.Preview;

public interface IUnityPreviewTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    long Generation { get; }
    string PipeName { get; }
    event EventHandler<PreviewProtocolMessage>? MessageReceived;
    event EventHandler<PreviewTransportStateChanged>? ConnectionChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task SendAsync(PreviewProtocolMessage message, CancellationToken cancellationToken = default);
    Task StopAsync();
}

public sealed record PreviewTransportStateChanged(
    long Generation,
    bool Connected,
    string? Reason = null,
    Exception? Exception = null);

public sealed class NamedPipeUnityPreviewTransport : IUnityPreviewTransport
{
    public const string ProtocolName = "naziki.editor-preview.v2";
    private const int MaximumMessageBytes = 64 * 1024 * 1024;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource _lifetime = new();
    private NamedPipeServerStream? _pipe;
    private Task? _readLoop;
    private Task? _dispatchLoop;
    private Channel<PreviewProtocolMessage>? _messages;
    private long _generation;
    private int _disconnectPublished;

    public NamedPipeUnityPreviewTransport()
    {
        PipeName = $"naziki-preview-{Environment.ProcessId}-{Guid.NewGuid():N}";
    }

    public bool IsConnected => _pipe?.IsConnected == true;
    public long Generation => Volatile.Read(ref _generation);
    public string PipeName { get; }
    public event EventHandler<PreviewProtocolMessage>? MessageReceived;
    public event EventHandler<PreviewTransportStateChanged>? ConnectionChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_pipe is not null)
            return;
        Interlocked.Exchange(ref _disconnectPublished, 0);
        var generation = Interlocked.Increment(ref _generation);
        _messages = Channel.CreateUnbounded<PreviewProtocolMessage>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
        if (_lifetime.IsCancellationRequested)
        {
            _lifetime.Dispose();
            _lifetime = new CancellationTokenSource();
        }

        _pipe = new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            64 * 1024,
            64 * 1024);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _pipe.WaitForConnectionAsync(linked.Token).ConfigureAwait(false);
        ConnectionChanged?.Invoke(this, new PreviewTransportStateChanged(generation, true));
        _dispatchLoop = DispatchLoopAsync(_messages.Reader, generation, _lifetime.Token);
        _readLoop = ReadLoopAsync(_pipe, _messages.Writer, generation, _lifetime.Token);
    }

    public async Task SendAsync(PreviewProtocolMessage message, CancellationToken cancellationToken = default)
    {
        var pipe = _pipe;
        if (pipe?.IsConnected != true)
            throw new InvalidOperationException("Unity Preview transport is not connected.");

        var json = JObject.FromObject(message);
        json["protocol"] = ProtocolName;
        var payload = Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
        if (payload.Length > MaximumMessageBytes)
            throw new InvalidDataException($"Preview protocol message exceeds {MaximumMessageBytes} bytes.");

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await pipe.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await pipe.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task StopAsync()
    {
        if (!_lifetime.IsCancellationRequested)
            _lifetime.Cancel();
        _pipe?.Dispose();
        if (_readLoop is not null)
        {
            try { await _readLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
        _messages?.Writer.TryComplete();
        if (_dispatchLoop is not null)
        {
            try { await _dispatchLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _pipe = null;
        _readLoop = null;
        _dispatchLoop = null;
        _messages = null;
        PublishDisconnected(Generation, "Transport stopped.");
    }

    private async Task ReadLoopAsync(
        Stream stream,
        ChannelWriter<PreviewProtocolMessage> messages,
        long generation,
        CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length <= 0 || length > MaximumMessageBytes)
                    throw new InvalidDataException($"Invalid Preview protocol frame length: {length}.");
                var payload = new byte[length];
                await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
                var json = JObject.Parse(Encoding.UTF8.GetString(payload));
                if (!string.Equals(json.Value<string>("protocol"), ProtocolName, StringComparison.Ordinal))
                    continue;
                var message = json.ToObject<PreviewProtocolMessage>();
                if (message is not null)
                    await messages.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or
                                   InvalidDataException or JsonException or ObjectDisposedException)
        {
            failure = ex;
        }
        finally
        {
            messages.TryComplete(failure);
            PublishDisconnected(
                generation,
                failure?.Message ?? "Unity Preview transport closed.",
                failure);
        }
    }

    private async Task DispatchLoopAsync(
        ChannelReader<PreviewProtocolMessage> messages,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in messages.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                MessageReceived?.Invoke(this, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            PublishDisconnected(generation, "Preview message dispatch failed.", ex);
        }
    }

    private void PublishDisconnected(long generation, string reason, Exception? exception = null)
    {
        if (generation != Generation ||
            Interlocked.Exchange(ref _disconnectPublished, 1) != 0)
            return;
        ConnectionChanged?.Invoke(this,
            new PreviewTransportStateChanged(generation, false, reason, exception));
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (count == 0)
                throw new EndOfStreamException();
            read += count;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        _lifetime.Dispose();
    }
}
