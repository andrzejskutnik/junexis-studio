using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="McpConnectorRegistry"/> and <see cref="IMcpConnectorRegistry"/>.
/// </summary>
[Trait("Issue", "60")]
public class McpConnectorRegistryTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class StubConnector : IMcpConnector
    {
        public string ConnectorId { get; }
        public bool IsConnected { get; set; }

        public StubConnector(string id, bool connected = false)
        {
            ConnectorId = id;
            IsConnected = connected;
        }

        public Task ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<McpResponse> SendAsync(McpRequest request, CancellationToken ct = default)
            => Task.FromResult(McpResponse.Ok("ok"));
    }

    private sealed class RecordingOutput : IOutput
    {
        public List<string> Lines { get; } = new();
        public string RegisterChannel(string ch) => ch;
        public IReadOnlyList<string> GetChannelsSnapshot() => Array.Empty<string>();
        public void Write(string ch, string msg) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev, string? src, string? ex = null) => Lines.Add($"[{ch}] {msg}");
        public IReadOnlyList<OutputEntry> GetSnapshot() => Array.Empty<OutputEntry>();
        public event Action<OutputEntry>? EntryWritten;
    }

    private McpConnectorRegistry CreateRegistry(RecordingOutput? output = null)
        => new McpConnectorRegistry(output);

    // ── Tests ───────────────────────────────────────────────────

    [Fact]
    public void Register_AddsConnector()
    {
        var reg = CreateRegistry();
        reg.Register(new StubConnector("alpha"));

        Assert.NotNull(reg.Get("alpha"));
        Assert.Single(reg.GetAllIds());
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var reg = CreateRegistry();
        Assert.Throws<ArgumentNullException>(() => reg.Register(null!));
    }

    [Fact]
    public void Register_Duplicate_Throws()
    {
        var reg = CreateRegistry();
        reg.Register(new StubConnector("alpha"));
        Assert.Throws<InvalidOperationException>(() => reg.Register(new StubConnector("alpha")));
    }

    [Fact]
    public void Register_CaseInsensitive()
    {
        var reg = CreateRegistry();
        reg.Register(new StubConnector("Alpha"));
        Assert.Throws<InvalidOperationException>(() => reg.Register(new StubConnector("ALPHA")));
    }

    [Fact]
    public void Unregister_RemovesConnector()
    {
        var reg = CreateRegistry();
        reg.Register(new StubConnector("alpha"));

        Assert.True(reg.Unregister("alpha"));
        Assert.Null(reg.Get("alpha"));
        Assert.Empty(reg.GetAllIds());
    }

    [Fact]
    public void Unregister_NotFound_ReturnsFalse()
    {
        var reg = CreateRegistry();
        Assert.False(reg.Unregister("nope"));
    }

    [Fact]
    public void Unregister_ClearsActiveIfRemoved()
    {
        var reg = CreateRegistry();
        reg.Register(new StubConnector("alpha"));
        reg.ActiveConnectorId = "alpha";

        reg.Unregister("alpha");

        Assert.Null(reg.ActiveConnectorId);
        Assert.Null(reg.GetActive());
    }

    [Fact]
    public void Get_ReturnsNull_WhenNotFound()
    {
        var reg = CreateRegistry();
        Assert.Null(reg.Get("nope"));
    }

    [Fact]
    public void GetAll_ReturnsAllConnectors()
    {
        var reg = CreateRegistry();
        reg.Register(new StubConnector("a"));
        reg.Register(new StubConnector("b"));
        reg.Register(new StubConnector("c"));

        var all = reg.GetAll();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void GetConnected_ReturnsOnlyConnected()
    {
        var reg = CreateRegistry();
        reg.Register(new StubConnector("a", connected: true));
        reg.Register(new StubConnector("b", connected: false));
        reg.Register(new StubConnector("c", connected: true));

        var connected = reg.GetConnected();
        Assert.Equal(2, connected.Count);
        Assert.All(connected, c => Assert.True(c.IsConnected));
    }

    [Fact]
    public void ActiveConnectorId_SetAndGet()
    {
        var reg = CreateRegistry();
        reg.Register(new StubConnector("alpha"));

        reg.ActiveConnectorId = "alpha";
        Assert.Equal("alpha", reg.ActiveConnectorId);
    }

    [Fact]
    public void ActiveConnectorId_SetToNull()
    {
        var reg = CreateRegistry();
        reg.Register(new StubConnector("alpha"));
        reg.ActiveConnectorId = "alpha";

        reg.ActiveConnectorId = null;
        Assert.Null(reg.ActiveConnectorId);
    }

    [Fact]
    public void ActiveConnectorId_SetUnregistered_Throws()
    {
        var reg = CreateRegistry();
        Assert.Throws<InvalidOperationException>(() => reg.ActiveConnectorId = "nope");
    }

    [Fact]
    public void GetActive_ReturnsActiveConnector()
    {
        var reg = CreateRegistry();
        var conn = new StubConnector("alpha");
        reg.Register(conn);
        reg.ActiveConnectorId = "alpha";

        var active = reg.GetActive();
        Assert.Same(conn, active);
    }

    [Fact]
    public void GetActive_ReturnsNull_WhenNoActive()
    {
        var reg = CreateRegistry();
        Assert.Null(reg.GetActive());
    }

    [Fact]
    public void Output_LogsRegisterAndUnregister()
    {
        var output = new RecordingOutput();
        var reg = CreateRegistry(output);

        reg.Register(new StubConnector("alpha"));
        reg.Unregister("alpha");

        Assert.Contains(output.Lines, l => l.Contains("registered: alpha"));
        Assert.Contains(output.Lines, l => l.Contains("unregistered: alpha"));
    }

    [Fact]
    public void Output_LogsActiveChange()
    {
        var output = new RecordingOutput();
        var reg = CreateRegistry(output);
        reg.Register(new StubConnector("alpha"));

        reg.ActiveConnectorId = "alpha";

        Assert.Contains(output.Lines, l => l.Contains("Active connector changed") && l.Contains("alpha"));
    }

    [Fact]
    public void GetAllIds_Snapshot_IsIndependent()
    {
        var reg = CreateRegistry();
        reg.Register(new StubConnector("a"));

        var ids = reg.GetAllIds();
        reg.Register(new StubConnector("b"));

        // Snapshot should not include later registrations
        Assert.Single(ids);
    }
}
