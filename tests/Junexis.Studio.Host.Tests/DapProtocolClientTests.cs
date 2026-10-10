using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class DapProtocolClientTests
{
    [Fact]
    public async Task SendRequestAsync_WritesFramedRequestAndCorrelatesResponse()
    {
        await using var transport = new InMemoryDapConnection();
        await using var client = new DapProtocolClient(transport.Input, transport.Output);
        var adapter = Task.Run(async () =>
        {
            using var request = await transport.ReadClientMessageAsync();
            Assert.Equal("threads", request.RootElement.GetProperty("command").GetString());
            var sequence = request.RootElement.GetProperty("seq").GetInt32();
            await transport.WriteAdapterMessageAsync(new
            {
                seq = 20,
                type = "response",
                request_seq = sequence,
                success = true,
                command = "threads",
                body = new { threads = new[] { new { id = 7, name = "main" } } },
            });
        });

        var body = await client.SendRequestAsync("threads", new { });
        await adapter;

        Assert.Equal(7, body.GetProperty("threads")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task EventFrame_IsPublishedWithClonedBody()
    {
        await using var transport = new InMemoryDapConnection();
        await using var client = new DapProtocolClient(transport.Input, transport.Output);
        var completion = new TaskCompletionSource<DapEventMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceived += message => completion.TrySetResult(message);

        await transport.WriteAdapterMessageAsync(new
        {
            seq = 2,
            type = "event",
            @event = "stopped",
            body = new { reason = "breakpoint", threadId = 3 },
        });

        var message = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("stopped", message.Event);
        Assert.Equal(3, message.Body.GetProperty("threadId").GetInt32());
    }

    internal sealed class InMemoryDapConnection : IDapAdapterConnection
    {
        private readonly AsyncBufferStream _adapterToClient = new();
        private readonly AsyncBufferStream _clientToAdapter = new();

        public Stream Input => _adapterToClient;
        public Stream Output => _clientToAdapter;

        public async Task<JsonDocument> ReadClientMessageAsync()
        {
            var payload = await DapProtocolClient.ReadFrameAsync(_clientToAdapter, CancellationToken.None);
            return JsonDocument.Parse(payload ?? throw new EndOfStreamException());
        }

        public async Task WriteAdapterMessageAsync(object message)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message);
            var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
            await _adapterToClient.WriteAsync(header);
            await _adapterToClient.WriteAsync(payload);
        }

        public ValueTask DisposeAsync()
        {
            _adapterToClient.Complete();
            _clientToAdapter.Complete();
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

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
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

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _channel.Writer.WriteAsync(buffer.ToArray(), cancellationToken);
        }

        public void Complete() => _channel.Writer.TryComplete();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            _channel.Writer.TryWrite(buffer.AsSpan(offset, count).ToArray());
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
