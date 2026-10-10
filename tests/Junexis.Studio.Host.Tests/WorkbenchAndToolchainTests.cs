using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="ToolchainDoctorExtension"/> and <see cref="WorkbenchRunExtension"/> host integration.
/// </summary>
[Trait("Issue", "M15")]
public sealed class WorkbenchAndToolchainTests
{
    [Fact]
    public void Host_RegistersToolchainDoctorCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<IToolchainDoctorService>(out var service));
        Assert.NotNull(service);
    }

    [Fact]
    public void Host_RegistersWorkbenchOperationServiceCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<IWorkbenchOperationService>(out var service));
        Assert.NotNull(service);
    }

    [Fact]
    public void Host_RegistersLaunchProfileRegistryCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<ILaunchProfileRegistry>(out var registry));
        Assert.NotNull(registry);
    }

    [Fact]
    public void Host_RegistersToolchainDoctorPanel()
    {
        var host = new JunexisStudioHost();
        host.Start();
        try
        {
            var panel = Assert.Single(
                host.Context.Panels.GetRegisteredPanels(),
                registration => registration.Id == "junexis.toolchains.doctor");

            Assert.Equal(PanelType.Tool, panel.Type);
        }
        finally
        {
            host.Shutdown();
        }
    }

    [Fact]
    public void Host_RegistersWorkbenchRunViewModelCapability()
    {
        var host = new JunexisStudioHost();
        host.Start();
        try
        {
            Assert.True(host.Context.Capabilities.TryGet<WorkbenchRunViewModel>(out var vm));
            Assert.NotNull(vm);
        }
        finally
        {
            host.Shutdown();
        }
    }
}
