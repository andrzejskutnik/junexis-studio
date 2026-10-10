using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class McpStatusViewModelTests
{
    private readonly EventBus _eventBus = new();
    private readonly FakeMcpConnectorRegistry _registry = new();

    private McpStatusViewModel CreateSut() => new(_registry, _eventBus);

    // --- ctor ---

    [Fact]
    [Trait("Issue", "64")]
    public void Constructor_NullRegistry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new McpStatusViewModel(null!, _eventBus));
    }

    [Fact]
    [Trait("Issue", "64")]
    public void Constructor_NullEventBus_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new McpStatusViewModel(_registry, null!));
    }

    // --- McpConnectedEvent ---

    [Fact]
    [Trait("Issue", "64")]
    public void OnConnected_AddsLogEntry()
    {
        var sut = CreateSut();
        _eventBus.Publish(new McpConnectedEvent("conn1"));

        Assert.Single(sut.Log);
        Assert.Equal("conn1", sut.Log[0].ConnectorId);
        Assert.Equal(McpLogEntryKind.Connected, sut.Log[0].Kind);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void OnConnected_RaisesLogChanged()
    {
        var sut = CreateSut();
        var raised = false;
        sut.LogChanged += () => raised = true;

        _eventBus.Publish(new McpConnectedEvent("c1"));
        Assert.True(raised);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void OnConnected_RaisesStatusChanged()
    {
        var sut = CreateSut();
        var raised = false;
        sut.StatusChanged += () => raised = true;

        _eventBus.Publish(new McpConnectedEvent("c1"));
        Assert.True(raised);
    }

    // --- McpDisconnectedEvent ---

    [Fact]
    [Trait("Issue", "64")]
    public void OnDisconnected_AddsLogEntry()
    {
        var sut = CreateSut();
        _eventBus.Publish(new McpDisconnectedEvent("c1", "timeout"));

        Assert.Single(sut.Log);
        Assert.Equal(McpLogEntryKind.Disconnected, sut.Log[0].Kind);
        Assert.Contains("timeout", sut.Log[0].Detail);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void OnDisconnected_NoReason_ShowsDefaultDetail()
    {
        var sut = CreateSut();
        _eventBus.Publish(new McpDisconnectedEvent("c1"));

        Assert.Equal("Disconnected", sut.Log[0].Detail);
    }

    // --- McpMessageReceivedEvent ---

    [Fact]
    [Trait("Issue", "64")]
    public void OnMessage_AddsLogEntry()
    {
        var sut = CreateSut();
        _eventBus.Publish(new McpMessageReceivedEvent("c1", "tools/list", new McpResponse(true)));

        Assert.Single(sut.Log);
        Assert.Equal(McpLogEntryKind.Message, sut.Log[0].Kind);
        Assert.Contains("tools/list", sut.Log[0].Detail);
    }

    // --- McpErrorEvent ---

    [Fact]
    [Trait("Issue", "64")]
    public void OnError_AddsLogEntry()
    {
        var sut = CreateSut();
        _eventBus.Publish(new McpErrorEvent("c1", "connection refused"));

        Assert.Single(sut.Log);
        Assert.Equal(McpLogEntryKind.Error, sut.Log[0].Kind);
        Assert.Contains("connection refused", sut.Log[0].Detail);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void OnError_RaisesStatusChanged()
    {
        var sut = CreateSut();
        var raised = false;
        sut.StatusChanged += () => raised = true;

        _eventBus.Publish(new McpErrorEvent("c1", "err"));
        Assert.True(raised);
    }

    // --- Log ordering ---

    [Fact]
    [Trait("Issue", "64")]
    public void Log_NewestFirst()
    {
        var sut = CreateSut();
        _eventBus.Publish(new McpConnectedEvent("c1"));
        _eventBus.Publish(new McpErrorEvent("c1", "oops"));

        Assert.Equal(2, sut.Log.Count);
        Assert.Equal(McpLogEntryKind.Error, sut.Log[0].Kind);
        Assert.Equal(McpLogEntryKind.Connected, sut.Log[1].Kind);
    }

    // --- ClearLog ---

    [Fact]
    [Trait("Issue", "64")]
    public void ClearLog_RemovesAllEntries()
    {
        var sut = CreateSut();
        _eventBus.Publish(new McpConnectedEvent("c1"));
        _eventBus.Publish(new McpErrorEvent("c1", "x"));

        sut.ClearLog();

        Assert.Empty(sut.Log);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void ClearLog_RaisesLogChanged()
    {
        var sut = CreateSut();
        var raised = false;
        sut.LogChanged += () => raised = true;

        sut.ClearLog();
        Assert.True(raised);
    }

    // --- Status properties ---

    [Fact]
    [Trait("Issue", "64")]
    public void IsActiveConnected_WhenNoActive_ReturnsFalse()
    {
        var sut = CreateSut();
        Assert.False(sut.IsActiveConnected);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void ActiveConnectorId_DelegatesToRegistry()
    {
        _registry.ActiveConnectorId = "my-conn";
        var sut = CreateSut();
        Assert.Equal("my-conn", sut.ActiveConnectorId);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void ConnectorIds_DelegatesToRegistry()
    {
        _registry.Ids.Add("a");
        _registry.Ids.Add("b");
        var sut = CreateSut();
        Assert.Equal(2, sut.ConnectorIds.Count);
    }

    // --- Dispose ---

    [Fact]
    [Trait("Issue", "64")]
    public void Dispose_UnsubscribesFromEvents()
    {
        var sut = CreateSut();
        sut.Dispose();

        _eventBus.Publish(new McpConnectedEvent("c1"));
        Assert.Empty(sut.Log);
    }
}

public sealed class McpStatusExtensionTests
{
    private IHostContext CreateContext(ICapabilityRegistry? capabilities = null)
    {
        var caps = capabilities ?? new CapabilityRegistry();
        return new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry(),
            capabilities: caps);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void Id_ReturnsExpected()
    {
        Assert.Equal("junexis.mcp.status", new McpStatusExtension().Id);
    }

    [Fact]
    [Trait("Issue", "64")]
    public void Initialize_WithoutRegistry_DoesNotRegisterPanel()
    {
        var ctx = CreateContext();
        new McpStatusExtension().Initialize(ctx);
        Assert.Empty(ctx.Panels.GetRegisteredPanels());
    }

    [Fact]
    [Trait("Issue", "64")]
    public void Initialize_WithoutRegistry_LogsWarning()
    {
        var ctx = CreateContext();
        new McpStatusExtension().Initialize(ctx);
        var snapshot = ((OutputService)ctx.Output).GetSnapshot();
        Assert.Contains(snapshot, e => e.Channel == "MCP" && e.Message.Contains("not available"));
    }

    [Fact]
    [Trait("Issue", "64")]
    public void Initialize_WithRegistry_RegistersPanel()
    {
        var caps = new CapabilityRegistry();
        caps.Register<IMcpConnectorRegistry>(new FakeMcpConnectorRegistry());
        var ctx = CreateContext(caps);

        new McpStatusExtension().Initialize(ctx);

        Assert.Contains(ctx.Panels.GetRegisteredPanels(), p => p.Id == "junexis.mcp.status");
    }

    [Fact]
    [Trait("Issue", "64")]
    public void Initialize_RegistersClearLogCommand()
    {
        var caps = new CapabilityRegistry();
        caps.Register<IMcpConnectorRegistry>(new FakeMcpConnectorRegistry());
        var ctx = CreateContext(caps);

        new McpStatusExtension().Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("mcp.status.clearLog"));
    }

    [Fact]
    [Trait("Issue", "64")]
    public void Shutdown_BeforeInit_DoesNotThrow()
    {
        new McpStatusExtension().Shutdown();
    }

    // ── Context menu targets (Issue #101) ───────────────────────

    [Fact]
    [Trait("Issue", "101")]
    public void Initialize_ClearLogCommand_HasContextTarget_JunexisMcpStatus()
    {
        var caps = new CapabilityRegistry();
        caps.Register<IMcpConnectorRegistry>(new FakeMcpConnectorRegistry());
        var ctx = CreateContext(caps);

        new McpStatusExtension().Initialize(ctx);

        var cmd = Assert.Single(ctx.Commands.GetRegisteredCommands(), c => c.Id == "mcp.status.clearLog");
        Assert.Equal("junexis.mcp.status", cmd.ContextTarget);
    }
}

// --- Fake MCP connector registry for tests ---

internal sealed class FakeMcpConnectorRegistry : IMcpConnectorRegistry
{
    public List<string> Ids { get; } = new();
    public string? ActiveConnectorId { get; set; }

    public void Register(IMcpConnector connector) { }
    public bool Unregister(string connectorId) => false;
    public IMcpConnector? Get(string connectorId) => null;
    public IReadOnlyList<string> GetAllIds() => Ids.AsReadOnly();
    public IReadOnlyList<IMcpConnector> GetAll() => Array.Empty<IMcpConnector>();
    public IReadOnlyList<IMcpConnector> GetConnected() => Array.Empty<IMcpConnector>();
    public IMcpConnector? GetActive() => null;
}
