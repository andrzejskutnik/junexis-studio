using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System.Reflection;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for v0.16 extensions and capabilities.
/// </summary>
[Trait("Issue", "M16")]
public sealed class AnalysisArtifactExtensionTests
{
    [Fact]
    public void Host_RegistersAnalysisSnapshotServiceCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<IAnalysisSnapshotService>(out var service));
        Assert.NotNull(service);
    }

    [Fact]
    public void Host_RegistersArtifactServiceCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<IArtifactService>(out var service));
        Assert.NotNull(service);
    }

    [Fact]
    public void Host_RegistersDebugTargetAssessmentServiceCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<IDebugTargetAssessmentService>(out var service));
        Assert.NotNull(service);
    }

    [Fact]
    public void Host_RegistersHelpCenterPanel()
    {
        var host = new JunexisStudioHost();
        host.Start();
        try
        {
            Assert.Contains(
                host.Context.Panels.GetRegisteredPanels(),
                r => r.Id == "junexis.help.center");
        }
        finally
        {
            host.Shutdown();
        }
    }

    [Fact]
    public void Host_RegistersTypeScriptBuildAndRunCommands()
    {
        var host = new JunexisStudioHost();
        host.Start();
        try
        {
            var cmds = host.Context.Commands.GetRegisteredCommands();
            Assert.Contains(cmds, c => c.Id == "build.typescript");
            Assert.Contains(cmds, c => c.Id == "run.typescript");
        }
        finally
        {
            host.Shutdown();
        }
    }

    [Theory]
    [InlineData("main.ts", "project.build", "build.typescript")]
    [InlineData("main.ts", "project.run", "run.typescript")]
    public void ProjectCommands_DispatchTypeScriptCorrectly(string fileName, string entryCommand, string expectedCommand)
    {
        var executed = (string?)null;
        var commands = new CommandRegistry();
        commands.Register("build.typescript", () => executed = "build.typescript");
        commands.Register("run.typescript", () => executed = "run.typescript");

        var activeDocument = new ActiveDocumentService();
        activeDocument.SetActivePath(Path.Combine(Path.GetTempPath(), fileName));

        var context = new HostContext(
            eventBus: new EventBus(),
            commands: commands,
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: activeDocument,
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry());

        var method = typeof(JunexisStudioHost).GetMethod(
            "RegisterBuiltInCommands",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method!.Invoke(null, [context]);

        context.Commands.Execute(entryCommand);

        Assert.Equal(expectedCommand, executed);
    }
}
