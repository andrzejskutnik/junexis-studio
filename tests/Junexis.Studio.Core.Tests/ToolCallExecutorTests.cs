using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="ToolCallExecutor"/>, <see cref="ToolCallRequest"/>
/// and <see cref="ToolCallResult"/>.
/// </summary>
[Trait("Issue", "82")]
public class ToolCallExecutorTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class FakeConnector : IMcpConnector
    {
        public string ConnectorId { get; }
        public bool IsConnected { get; set; } = true;
        public McpResponse? NextResponse { get; set; }
        public bool ThrowOnSend { get; set; }
        public int SendCount;

        public FakeConnector(string id) => ConnectorId = id;

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
        {
            Interlocked.Increment(ref SendCount);
            if (ThrowOnSend) throw new InvalidOperationException("send-error");
            return Task.FromResult(NextResponse ?? McpResponse.Ok("ok"));
        }
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

    private (ToolCallExecutor exec, McpConnectorRegistry reg, FakeConnector conn, RecordingOutput output) Create()
    {
        var output = new RecordingOutput();
        var reg = new McpConnectorRegistry(output);
        var conn = new FakeConnector("test-mcp");
        reg.Register(conn);
        reg.ActiveConnectorId = "test-mcp";
        var exec = new ToolCallExecutor(reg, output);
        return (exec, reg, conn, output);
    }

    // ── Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task Execute_Success()
    {
        var (exec, _, conn, _) = Create();
        conn.NextResponse = McpResponse.Ok("{\"value\":42}");

        var result = await exec.ExecuteAsync(new ToolCallRequest("calc"));

        Assert.True(result.Success);
        Assert.Contains("42", result.Result!);
        Assert.True(result.DurationMs >= 0);
    }

    [Fact]
    public async Task Execute_ConnectorFail_ReturnsFail()
    {
        var (exec, _, conn, _) = Create();
        conn.NextResponse = McpResponse.Fail("bad input");

        var result = await exec.ExecuteAsync(new ToolCallRequest("calc"));

        Assert.False(result.Success);
        Assert.Contains("bad input", result.Error!);
    }

    [Fact]
    public async Task Execute_NoActiveConnector_ReturnsFail()
    {
        var output = new RecordingOutput();
        var reg = new McpConnectorRegistry(output);
        var exec = new ToolCallExecutor(reg, output);

        var result = await exec.ExecuteAsync(new ToolCallRequest("calc"));

        Assert.False(result.Success);
        Assert.Contains("No active", result.Error!);
    }

    [Fact]
    public async Task Execute_SpecificConnector_NotFound()
    {
        var (exec, _, _, _) = Create();

        var result = await exec.ExecuteAsync(new ToolCallRequest("calc"), connectorId: "nonexistent");

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error!);
    }

    [Fact]
    public async Task Execute_DisconnectedConnector_ReturnsFail()
    {
        var (exec, _, conn, _) = Create();
        conn.IsConnected = false;

        var result = await exec.ExecuteAsync(new ToolCallRequest("calc"));

        Assert.False(result.Success);
        Assert.Contains("not connected", result.Error!);
    }

    [Fact]
    public async Task Execute_ExceptionInSend_ReturnsFail()
    {
        var (exec, _, conn, _) = Create();
        conn.ThrowOnSend = true;

        var result = await exec.ExecuteAsync(new ToolCallRequest("calc"));

        Assert.False(result.Success);
        Assert.Contains("send-error", result.Error!);
    }

    // ── Capability gating ───────────────────────────────────────

    [Fact]
    public async Task Execute_DeniedTool_ReturnsFail()
    {
        var (exec, _, _, _) = Create();
        exec.Deny("dangerous-tool");

        var result = await exec.ExecuteAsync(new ToolCallRequest("dangerous-tool"));

        Assert.False(result.Success);
        Assert.Contains("denied", result.Error!);
    }

    [Fact]
    public async Task Execute_AllowList_BlocksUnlisted()
    {
        var (exec, _, _, _) = Create();
        exec.Allow("safe-tool");

        var result = await exec.ExecuteAsync(new ToolCallRequest("other-tool"));

        Assert.False(result.Success);
        Assert.Contains("denied", result.Error!);
    }

    [Fact]
    public async Task Execute_AllowList_PermitsListed()
    {
        var (exec, _, conn, _) = Create();
        exec.Allow("safe-tool");
        conn.NextResponse = McpResponse.Ok("ok");

        var result = await exec.ExecuteAsync(new ToolCallRequest("safe-tool"));

        Assert.True(result.Success);
    }

    [Fact]
    public void IsAllowed_DenyOverridesAllow()
    {
        var (exec, _, _, _) = Create();
        exec.Allow("tool");
        exec.Deny("tool");

        Assert.False(exec.IsAllowed("tool"));
    }

    [Fact]
    public void IsAllowed_NoLists_AllAllowed()
    {
        var (exec, _, _, _) = Create();
        Assert.True(exec.IsAllowed("anything"));
    }

    // ── Logging ─────────────────────────────────────────────────

    [Fact]
    public async Task Execute_LogsToOutput()
    {
        var (exec, _, _, output) = Create();

        await exec.ExecuteAsync(new ToolCallRequest("calc"));

        Assert.Contains(output.Lines, l => l.Contains("Tool call requested: calc"));
        Assert.Contains(output.Lines, l => l.Contains("succeeded"));
    }

    [Fact]
    public async Task Execute_DeniedTool_LogsWarning()
    {
        var (exec, _, _, output) = Create();
        exec.Deny("blocked");

        await exec.ExecuteAsync(new ToolCallRequest("blocked"));

        Assert.Contains(output.Lines, l => l.Contains("denied"));
    }

    // ── Model tests ─────────────────────────────────────────────

    [Fact]
    public void ToolCallRequest_Properties()
    {
        var r = new ToolCallRequest("calc", "{}", 5000);
        Assert.Equal("calc", r.ToolName);
        Assert.Equal("{}", r.Payload);
        Assert.Equal(5000, r.TimeoutMs);
    }

    [Fact]
    public void ToolCallRequest_Defaults()
    {
        var r = new ToolCallRequest("calc");
        Assert.Null(r.Payload);
        Assert.Equal(30000, r.TimeoutMs);
    }

    [Fact]
    public void ToolCallResult_Ok_Factory()
    {
        var r = ToolCallResult.Ok("data", 100);
        Assert.True(r.Success);
        Assert.Equal("data", r.Result);
        Assert.Equal(100, r.DurationMs);
        Assert.Null(r.Error);
    }

    [Fact]
    public void ToolCallResult_Fail_Factory()
    {
        var r = ToolCallResult.Fail("err", 50);
        Assert.False(r.Success);
        Assert.Null(r.Result);
        Assert.Equal("err", r.Error);
        Assert.Equal(50, r.DurationMs);
    }

    [Fact]
    public void Constructor_NullRegistry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ToolCallExecutor(null!));
    }

    [Fact]
    public async Task Execute_NullRequest_Throws()
    {
        var (exec, _, _, _) = Create();
        await Assert.ThrowsAsync<ArgumentNullException>(() => exec.ExecuteAsync(null!));
    }
}
