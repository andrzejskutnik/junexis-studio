using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Junexis.Studio.Host;

internal sealed record DapEventMessage(string Event, JsonElement Body);

internal sealed class DapProtocolClient : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _reader;
    private readonly Func<string, JsonElement, CancellationToken, Task<object?>>? _reverseRequestHandler;
    private int _sequence;

    public DapProtocolClient(
        Stream input,
        Stream output,
        Func<string, JsonElement, CancellationToken, Task<object?>>? reverseRequestHandler = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _reverseRequestHandler = reverseRequestHandler;
        _reader = ReadLoopAsync(_lifetime.Token);
    }

    public event Action<DapEventMessage>? EventReceived;

    public async Task<JsonElement> SendRequestAsync(
        string command,
        object? arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var sequence = Interlocked.Increment(ref _sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(sequence, completion))
            throw new InvalidOperationException($"Duplicate DAP request sequence {sequence}.");
        try
        {
            await WriteMessageAsync(new
            {
                seq = sequence,
                type = "request",
                command,
                arguments,
            }, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(sequence, out _);
        }
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
                var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (type == "response")
                    HandleResponse(root);
                else if (type == "event")
                    HandleEvent(root);
                else if (type == "request")
                    await HandleReverseRequestAsync(root, cancellationToken).ConfigureAwait(false);
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
            var exception = failure ?? new EndOfStreamException("DAP adapter stream closed.");
            foreach (var completion in _pending.Values)
                completion.TrySetException(exception);
        }
    }

    private void HandleResponse(JsonElement root)
    {
        if (!root.TryGetProperty("request_seq", out var requestSequence)
            || !_pending.TryGetValue(requestSequence.GetInt32(), out var completion))
            return;
        if (root.TryGetProperty("success", out var success) && !success.GetBoolean())
        {
            var message = root.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "DAP request failed.";
            completion.TrySetException(new InvalidOperationException(message));
            return;
        }
        var body = root.TryGetProperty("body", out var bodyElement)
            ? bodyElement.Clone()
            : EmptyObject();
        completion.TrySetResult(body);
    }

    private void HandleEvent(JsonElement root)
    {
        var name = root.TryGetProperty("event", out var eventElement) ? eventElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
            return;
        var body = root.TryGetProperty("body", out var bodyElement)
            ? bodyElement.Clone()
            : EmptyObject();
        EventReceived?.Invoke(new DapEventMessage(name, body));
    }

    private async Task HandleReverseRequestAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var requestSequence = root.GetProperty("seq").GetInt32();
        var command = root.TryGetProperty("command", out var commandElement)
            ? commandElement.GetString() ?? string.Empty
            : string.Empty;
        if (_reverseRequestHandler is not null)
        {
            try
            {
                var body = await _reverseRequestHandler(command,
                    root.TryGetProperty("arguments", out var arguments) ? arguments.Clone() : EmptyObject(),
                    cancellationToken).ConfigureAwait(false);
                await WriteMessageAsync(new
                {
                    seq = Interlocked.Increment(ref _sequence), type = "response",
                    request_seq = requestSequence, success = true, command, body,
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (NotSupportedException)
            {
                // Fall through to the standard unsupported response.
            }
        }
        await WriteMessageAsync(new
        {
            seq = Interlocked.Increment(ref _sequence), type = "response",
            request_seq = requestSequence, success = false, command,
            message = $"Reverse request '{command}' is not supported by Junexis Studio yet.",
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {json.Length}\r\n\r\n");
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(json, cancellationToken).ConfigureAwait(false);
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
                return header.Count == 0 ? null : throw new EndOfStreamException("Incomplete DAP header.");
            header.Add((byte)value);
            matched = value == delimiter[matched] ? matched + 1 : value == delimiter[0] ? 1 : 0;
            if (header.Count > 16 * 1024)
                throw new InvalidDataException("DAP header exceeds 16 KiB.");
        }

        var text = Encoding.ASCII.GetString([.. header]);
        var contentLength = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && parts[0].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(parts => int.TryParse(parts[1].Trim(), out var length) ? length : -1)
            .FirstOrDefault(-1);
        if (contentLength < 0 || contentLength > 16 * 1024 * 1024)
            throw new InvalidDataException("DAP Content-Length is missing or invalid.");
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
}
