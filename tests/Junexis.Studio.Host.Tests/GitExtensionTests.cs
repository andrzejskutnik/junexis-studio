using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="GitExtension"/>.
/// </summary>
public sealed class GitExtensionTests
{
    private static HostContext CreateContext(
        OutputService? output = null,
        IVersionControlService? versionControl = null) =>
        new(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: output ?? new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: versionControl,
            views: new TestViewRegistry());

    [Fact]
    [Trait("Issue", "101")]
    public void Id_ReturnsExpected()
    {
        Assert.Equal("junexis.vcs.commands", new GitExtension().Id);
    }

    [Fact]
    [Trait("Issue", "101")]
    public void Initialize_RegistersCommitCommand()
    {
        var ctx = CreateContext();
        new GitExtension().Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("vcs.commit"));
    }

    [Fact]
    [Trait("Issue", "101")]
    public void Initialize_CommitCommand_HasContextTarget_JunexisVcsChanges()
    {
        var ctx = CreateContext();
        new GitExtension().Initialize(ctx);

        var cmd = Assert.Single(ctx.Commands.GetRegisteredCommands(), c => c.Id == "vcs.commit");
        Assert.Equal("junexis.vcs.changes", cmd.ContextTarget);
    }

    [Fact]
    [Trait("Issue", "101")]
    public void Initialize_CommitCommand_HasMenuPath()
    {
        var ctx = CreateContext();
        new GitExtension().Initialize(ctx);

        var cmd = Assert.Single(ctx.Commands.GetRegisteredCommands(), c => c.Id == "vcs.commit");
        Assert.Equal("VCS/Commit...", cmd.MenuPath);
    }

    [Fact]
    [Trait("Issue", "101")]
    public void CommitCommand_ActivatesChangesPanel()
    {
        var eventBus = new EventBus();
        ActivatePanelRequested? activation = null;
        eventBus.Subscribe<ActivatePanelRequested>(request => activation = request);
        var ctx = new HostContext(
            eventBus,
            new CommandRegistry(),
            new PanelRegistry(),
            new OutputService(),
            new TestProcessService(),
            new WorkspaceService(),
            new DiagnosticService(),
            new ActiveDocumentService(),
            new TestOpenDocumentRepository(),
            versionControl: null,
            new TestViewRegistry());
        new GitExtension().Initialize(ctx);

        ctx.Commands.Execute("vcs.commit");

        Assert.NotNull(activation);
        Assert.Equal("junexis.vcs.changes", activation.PanelId);
    }

    [Fact]
    [Trait("Issue", "101")]
    public void CommitCommand_DoesNotInvokeLegacyVersionControlService()
    {
        var versionControl = new ThrowingVersionControlService();
        var ctx = CreateContext(versionControl: versionControl);
        new GitExtension().Initialize(ctx);

        ctx.Commands.Execute("vcs.commit");

        Assert.Equal(0, versionControl.CommitCalls);
    }

    private sealed class ThrowingVersionControlService : IVersionControlService
    {
        public int CommitCalls { get; private set; }

        public bool IsRepository(string path) => true;
        public IReadOnlyList<VersionControlFileEntry> GetRepositoryStatus() => [];
        public VersionControlFileStatus GetFileStatus(string path) => VersionControlFileStatus.Committed;
        public void Commit(string message)
        {
            CommitCalls++;
            throw new InvalidOperationException("Commit must not be called from the menu command.");
        }
    }

    [Fact]
    [Trait("Issue", "101")]
    public void Initialize_NullContext_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GitExtension().Initialize(null!));
    }

    [Fact]
    [Trait("Issue", "101")]
    public void Shutdown_DoesNotThrow()
    {
        new GitExtension().Shutdown();
    }
}
