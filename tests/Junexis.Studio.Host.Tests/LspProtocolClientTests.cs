using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class LspProtocolClientTests
{
    [Fact]
    [Trait("Category", "LanguageServices")]
    public async Task Request_IsFramedAndResponseIsCorrelated()
    {
        await using var transport = new InMemoryLspTransport();
        await using var client = new LspProtocolClient(transport.ServerToClient, transport.ClientToServer);
        var server = Task.Run(async () =>
        {
            using var request = await transport.ReadClientMessageAsync();
            Assert.Equal("textDocument/hover", request.RootElement.GetProperty("method").GetString());
            var id = request.RootElement.GetProperty("id").GetInt32();
            await transport.WriteServerMessageAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = new { contents = new { kind = "markdown", value = "**task**" } },
            });
        });

        var response = await client.SendRequestAsync(
            "textDocument/hover",
            new { textDocument = new { uri = "file:///sample.jnx" } });
        await server;

        Assert.Equal(
            "**task**",
            response.GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    [Trait("Category", "LanguageServices")]
    public async Task PublishDiagnosticsNotification_IsRaisedWithClonedParameters()
    {
        await using var transport = new InMemoryLspTransport();
        await using var client = new LspProtocolClient(transport.ServerToClient, transport.ClientToServer);
        var completion = new TaskCompletionSource<LspNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.NotificationReceived += notification => completion.TrySetResult(notification);

        await transport.WriteServerMessageAsync(new
        {
            jsonrpc = "2.0",
            method = "textDocument/publishDiagnostics",
            @params = new
            {
                uri = "file:///sample.jnx",
                diagnostics = new[] { new { severity = 1, message = "broken" } },
            },
        });

        var notification = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("textDocument/publishDiagnostics", notification.Method);
        Assert.Equal("broken", notification.Parameters.GetProperty("diagnostics")[0].GetProperty("message").GetString());
    }

    [Fact]
    [Trait("Category", "LanguageServices")]
    public async Task ServerConfigurationRequest_ReceivesValidJsonRpcResponse()
    {
        await using var transport = new InMemoryLspTransport();
        await using var client = new LspProtocolClient(transport.ServerToClient, transport.ClientToServer);

        await transport.WriteServerMessageAsync(new
        {
            jsonrpc = "2.0",
            id = 41,
            method = "workspace/configuration",
            @params = new { items = Array.Empty<object>() },
        });

        using var response = await transport.ReadClientMessageAsync();
        Assert.Equal(41, response.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Array, response.RootElement.GetProperty("result").ValueKind);
    }

    private sealed class InMemoryLspTransport : IAsyncDisposable
    {
        private readonly AsyncBufferStream _serverToClient = new();
        private readonly AsyncBufferStream _clientToServer = new();

        public Stream ServerToClient => _serverToClient;
        public Stream ClientToServer => _clientToServer;

        public async Task<JsonDocument> ReadClientMessageAsync()
        {
            var payload = await LspProtocolClient.ReadFrameAsync(_clientToServer, CancellationToken.None);
            return JsonDocument.Parse(payload ?? throw new EndOfStreamException());
        }

        public async Task WriteServerMessageAsync(object message)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message);
            var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
            await _serverToClient.WriteAsync(header);
            await _serverToClient.WriteAsync(payload);
        }

        public ValueTask DisposeAsync()
        {
            _serverToClient.Complete();
            _clientToServer.Complete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncBufferStream : Stream
    {
        private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
        private byte[]? _current;
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            while (_current is null || _offset >= _current.Length)
            {
                if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
                    return 0;
                if (_channel.Reader.TryRead(out var next))
                {
                    _current = next;
                    _offset = 0;
                }
            }
            var count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _channel.Writer.WriteAsync(buffer.ToArray(), cancellationToken);

        public void Complete() => _channel.Writer.TryComplete();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            _channel.Writer.TryWrite(buffer.AsSpan(offset, count).ToArray());
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
