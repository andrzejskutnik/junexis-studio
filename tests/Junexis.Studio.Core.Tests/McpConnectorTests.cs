using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="McpConnectorBase"/>, <see cref="IMcpConnector"/>,
/// MCP messages and MCP events.
/// </summary>
[Trait("Issue", "24")]
public class McpConnectorTests
{
    // ── Fake connector ──────────────────────────────────────────

    private sealed class FakeConnector : McpConnectorBase
    {
        public bool ConnectCalled { get; set; }
        public bool DisconnectCalled { get; set; }
        public McpRequest? LastRequest { get; private set; }
        public McpResponse NextResponse { get; set; } = McpResponse.Ok("ok");
        public bool ThrowOnSend { get; set; }

        public FakeConnector(string id, IEventBus? bus = null, IOutput? output = null)
            : base(id, bus, output) { }

        protected override Task DoConnectAsync(CancellationToken ct)
        {
            ConnectCalled = true;
            return Task.CompletedTask;
        }

        protected override Task DoDisconnectAsync(CancellationToken ct)
        {
            DisconnectCalled = true;
            return Task.CompletedTask;
        }

        protected override Task<McpResponse> DoSendAsync(McpRequest request, CancellationToken ct)
        {
            LastRequest = request;
            if (ThrowOnSend) throw new InvalidOperationException("send-error");
            return Task.FromResult(NextResponse);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────

    private sealed class RecordingEventBus : IEventBus
    {
        public List<object> Published { get; } = new();
        public void Publish(object evt) => Published.Add(evt);
        public void Subscribe<T>(Action<T> handler) { }
        public void Unsubscribe<T>(Action<T> handler) { }
    }

    private sealed class RecordingOutput : IOutput
    {
        public List<string> Lines { get; } = new();
        public string RegisterChannel(string channel) => channel;
        public IReadOnlyList<string> GetChannelsSnapshot() => Array.Empty<string>();
        public void Write(string channel, string message) => Lines.Add($"[{channel}] {message}");
        public void Write(string channel, string message, OutputSeverity severity) => Lines.Add($"[{channel}] {message}");
        public void Write(string channel, string message, OutputSeverity severity, string? source, string? exceptionText = null) => Lines.Add($"[{channel}] {message}");
        public IReadOnlyList<OutputEntry> GetSnapshot() => Array.Empty<OutputEntry>();
        public event Action<OutputEntry>? EntryWritten;
    }

    // ── Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_SetsIsConnected()
    {
        var c = new FakeConnector("test");
        await c.ConnectAsync();
        Assert.True(c.IsConnected);
        Assert.True(c.ConnectCalled);
    }

    [Fact]
    public async Task ConnectAsync_PublishesEvent()
    {
        var bus = new RecordingEventBus();
        var c = new FakeConnector("test", bus);
        await c.ConnectAsync();
        Assert.Contains(bus.Published, e => e is McpConnectedEvent ev && ev.ConnectorId == "test");
    }

    [Fact]
    public async Task ConnectAsync_Idempotent_WhenAlreadyConnected()
    {
        var c = new FakeConnector("test");
        await c.ConnectAsync();
        c.ConnectCalled = false; // reset
        await c.ConnectAsync();  // second call should be no-op
        Assert.False(c.ConnectCalled);
    }

    [Fact]
    public async Task DisconnectAsync_SetsIsConnectedFalse()
    {
        var c = new FakeConnector("test");
        await c.ConnectAsync();
        await c.DisconnectAsync();
        Assert.False(c.IsConnected);
        Assert.True(c.DisconnectCalled);
    }

    [Fact]
    public async Task DisconnectAsync_PublishesEvent()
    {
        var bus = new RecordingEventBus();
        var c = new FakeConnector("test", bus);
        await c.ConnectAsync();
        await c.DisconnectAsync();
        Assert.Contains(bus.Published, e => e is McpDisconnectedEvent ev && ev.ConnectorId == "test");
    }

    [Fact]
    public async Task DisconnectAsync_NoOp_WhenNotConnected()
    {
        var c = new FakeConnector("test");
        await c.DisconnectAsync(); // should not throw
        Assert.False(c.DisconnectCalled);
    }

    [Fact]
    public async Task SendAsync_ReturnsResponse_WhenConnected()
    {
        var c = new FakeConnector("test");
        c.NextResponse = McpResponse.Ok("{\"data\":1}");
        await c.ConnectAsync();

        var resp = await c.SendAsync(new McpRequest("ping", "{}"));
        Assert.True(resp.Success);
        Assert.Equal("{\"data\":1}", resp.Payload);
    }

    [Fact]
    public async Task SendAsync_PublishesMessageReceivedEvent()
    {
        var bus = new RecordingEventBus();
        var c = new FakeConnector("test", bus);
        await c.ConnectAsync();

        await c.SendAsync(new McpRequest("ping"));
        Assert.Contains(bus.Published, e => e is McpMessageReceivedEvent ev
            && ev.ConnectorId == "test" && ev.Method == "ping");
    }

    [Fact]
    public async Task SendAsync_ReturnsFail_WhenNotConnected()
    {
        var c = new FakeConnector("test");
        var resp = await c.SendAsync(new McpRequest("ping"));
        Assert.False(resp.Success);
        Assert.Contains("not connected", resp.Error!);
    }

    [Fact]
    public async Task SendAsync_CatchesException_PublishesErrorEvent()
    {
        var bus = new RecordingEventBus();
        var output = new RecordingOutput();
        var c = new FakeConnector("test", bus, output) { ThrowOnSend = true };
        await c.ConnectAsync();

        var resp = await c.SendAsync(new McpRequest("boom"));
        Assert.False(resp.Success);
        Assert.Contains("send-error", resp.Error!);
        Assert.Contains(bus.Published, e => e is McpErrorEvent ev && ev.ConnectorId == "test");
        Assert.Contains(output.Lines, l => l.Contains("send-error"));
    }

    [Fact]
    public async Task Output_LogsConnectAndDisconnect()
    {
        var output = new RecordingOutput();
        var c = new FakeConnector("test", output: output);
        await c.ConnectAsync();
        await c.DisconnectAsync();

        Assert.Contains(output.Lines, l => l.Contains("Connected"));
        Assert.Contains(output.Lines, l => l.Contains("Disconnected"));
    }

    [Fact]
    public void McpRequest_Constructor()
    {
        var req = new McpRequest("tools/list", "{\"filter\":\"*\"}");
        Assert.Equal("tools/list", req.Method);
        Assert.Equal("{\"filter\":\"*\"}", req.Payload);
    }

    [Fact]
    public void McpResponse_FactoryMethods()
    {
        var ok = McpResponse.Ok("data");
        Assert.True(ok.Success);
        Assert.Equal("data", ok.Payload);

        var fail = McpResponse.Fail("err");
        Assert.False(fail.Success);
        Assert.Equal("err", fail.Error);
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyId()
    {
        Assert.Throws<ArgumentException>(() => new FakeConnector(""));
        Assert.Throws<ArgumentException>(() => new FakeConnector("  "));
    }
}
