using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Junexis.Studio.Host;

internal sealed record LspNotification(string Method, JsonElement Parameters);

internal sealed class LspProtocolClient : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _reader;
    private int _sequence;

    public LspProtocolClient(Stream input, Stream output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _reader = ReadLoopAsync(_lifetime.Token);
    }

    public event Action<LspNotification>? NotificationReceived;

    public event Action<Exception?>? Closed;

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var id = Interlocked.Increment(ref _sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
            throw new InvalidOperationException($"Duplicate LSP request id {id}.");

        try
        {
            await WriteMessageAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken)
                .ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task SendNotificationAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return WriteMessageAsync(new { jsonrpc = "2.0", method, @params = parameters }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        try
        {
            await _reader.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }

        _writeLock.Dispose();
        _lifetime.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var payload = await ReadFrameAsync(_input, cancellationToken).ConfigureAwait(false);
                if (payload is null)
                    break;

                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (root.TryGetProperty("method", out var methodElement))
                {
                    var method = methodElement.GetString() ?? string.Empty;
                    var parameters = root.TryGetProperty("params", out var paramsElement)
                        ? paramsElement.Clone()
                        : EmptyObject();
                    if (root.TryGetProperty("id", out var requestId))
                        await ReplyToServerRequestAsync(requestId.Clone(), method, cancellationToken).ConfigureAwait(false);
                    else
                        NotificationReceived?.Invoke(new LspNotification(method, parameters));
                }
                else if (root.TryGetProperty("id", out var idElement))
                {
                    HandleResponse(idElement, root);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            var exception = failure ?? new EndOfStreamException("LSP server stream closed.");
            foreach (var completion in _pending.Values)
                completion.TrySetException(exception);
            Closed?.Invoke(failure);
        }
    }

    private void HandleResponse(JsonElement idElement, JsonElement root)
    {
        if (idElement.ValueKind != JsonValueKind.Number
            || !idElement.TryGetInt32(out var id)
            || !_pending.TryGetValue(id, out var completion))
            return;

        if (root.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "LSP request failed.";
            completion.TrySetException(new InvalidOperationException(message));
            return;
        }

        completion.TrySetResult(
            root.TryGetProperty("result", out var result) ? result.Clone() : JsonNull());
    }

    private Task ReplyToServerRequestAsync(JsonElement id, string method, CancellationToken cancellationToken)
    {
        object? result = method switch
        {
            "workspace/configuration" => Array.Empty<object>(),
            "window/workDoneProgress/create" => null,
            "client/registerCapability" => null,
            "client/unregisterCapability" => null,
            _ => null,
        };
        return WriteMessageAsync(new { jsonrpc = "2.0", id, result }, cancellationToken);
    }

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {json.Length}\r\n\r\n");
        var frame = new byte[header.Length + json.Length];
        header.CopyTo(frame, 0);
        json.CopyTo(frame, header.Length);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    internal static async Task<byte[]?> ReadFrameAsync(Stream input, CancellationToken cancellationToken)
    {
        var header = new List<byte>(128);
        var matched = 0;
        byte[] delimiter = [13, 10, 13, 10];
        while (matched < delimiter.Length)
        {
            var value = await ReadByteAsync(input, cancellationToken).ConfigureAwait(false);
            if (value < 0)
                return header.Count == 0 ? null : throw new EndOfStreamException("Incomplete LSP header.");
            header.Add((byte)value);
            matched = value == delimiter[matched] ? matched + 1 : value == delimiter[0] ? 1 : 0;
            if (header.Count > 16 * 1024)
                throw new InvalidDataException("LSP header exceeds 16 KiB.");
        }

        var text = Encoding.ASCII.GetString([.. header]);
        var contentLength = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2
                && parts[0].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(parts => int.TryParse(parts[1].Trim(), out var length) ? length : -1)
            .FirstOrDefault(-1);
        if (contentLength < 0 || contentLength > 16 * 1024 * 1024)
            throw new InvalidDataException("LSP Content-Length is missing or invalid.");

        var payload = new byte[contentLength];
        await input.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    private static async Task<int> ReadByteAsync(Stream input, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        return read == 0 ? -1 : buffer[0];
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static JsonElement JsonNull()
    {
        using var document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }
}

internal sealed class TextWriterOutputStream : Stream
{
    private readonly TextWriter _writer;

    public TextWriterOutputStream(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => _writer.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _writer.FlushAsync(cancellationToken);

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var text = Encoding.UTF8.GetString(buffer.Span);
        await _writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        _writer.Write(Encoding.UTF8.GetString(buffer, offset, count));

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

internal sealed class TextReaderInputStream : Stream
{
    private readonly TextReader _reader;
    private byte[] _pending = [];
    private int _pendingOffset;

    public TextReaderInputStream(TextReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        while (_pendingOffset >= _pending.Length)
        {
            var characters = new char[Math.Max(1, Math.Min(4096, buffer.Length))];
            var read = await _reader.ReadAsync(characters.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return 0;
            _pending = Encoding.UTF8.GetBytes(new string(characters, 0, read));
            _pendingOffset = 0;
        }

        var count = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
        _pending.AsMemory(_pendingOffset, count).CopyTo(buffer);
        _pendingOffset += count;
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
