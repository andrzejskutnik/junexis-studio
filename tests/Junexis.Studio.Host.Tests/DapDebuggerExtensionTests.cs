using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="DapDebuggerExtension"/> host integration.
/// </summary>
[Trait("Issue", "M12")]
public sealed class DapDebuggerExtensionTests
{
    [Fact]
    public void Host_RegistersDebugPanel()
    {
        var host = new JunexisStudioHost();
        host.Start();
        try
        {
            var panel = Assert.Single(
                host.Context.Panels.GetRegisteredPanels(),
                registration => registration.Id == "junexis.debugger.dap");

            Assert.Equal("Debug", panel.Title);
            Assert.Equal(PanelType.Tool, panel.Type);
            Assert.Equal(DockGroup.Right, panel.DockGroup);
        }
        finally
        {
            host.Shutdown();
        }
    }

    [Fact]
    public void Host_RegistersBreakpointServiceCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<IBreakpointService>(out var service));
        Assert.NotNull(service);
    }

    [Fact]
    public void Host_RegistersDebugAdapterRegistryCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<IDebugAdapterRegistry>(out var registry));
        Assert.NotNull(registry);
    }

    [Fact]
    public void Host_RegistersDebugCommands()
    {
        var host = new JunexisStudioHost();
        host.Start();
        try
        {
            var cmds = host.Context.Commands.GetRegisteredCommands();

            Assert.Contains(cmds, c => c.Id == "debug.gpp");
            Assert.Contains(cmds, c => c.Id == "debug.go");
            Assert.Contains(cmds, c => c.Id == "debug.continue");
            Assert.Contains(cmds, c => c.Id == "debug.session.stop");
            Assert.Contains(cmds, c => c.Id == "debug.why");
        }
        finally
        {
            host.Shutdown();
        }
    }
}
