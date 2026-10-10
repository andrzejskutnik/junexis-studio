using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class ProblemsExtensionTests
{
    [Fact]
    [Trait("Issue", "22")]
    public void Initialize_RegistersProblemsToolPanel()
    {
        var context = new HostContext(
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
            views: new TestViewRegistry());

        var sut = new ProblemsExtension();
        sut.Initialize(context);

        var panels = context.Panels.GetRegisteredPanels();
        var problems = panels.Single(p => p.Type == PanelType.Tool && p.Id == "problems");

        var instance = problems.Factory();
        Assert.IsType<ProblemsPanelViewModel>(instance);
    }
}
