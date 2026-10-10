using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.Linq;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class VcsDecorationProviderTests
{
    private readonly EventBus _eventBus = new();

    private VcsService CreateVcs()
    {
        return new VcsService(
            backend: (_, msg) => new VcsCommitResult(true, "abc123"),
            eventBus: _eventBus);
    }

    // --- ctor ---

    [Fact]
    [Trait("Issue", "73")]
    public void Constructor_NullVcs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new VcsDecorationProvider(null!, _eventBus));
    }

    [Fact]
    [Trait("Issue", "73")]
    public void Constructor_NullEventBus_Throws()
    {
        var vcs = CreateVcs();
        Assert.Throws<ArgumentNullException>(() => new VcsDecorationProvider(vcs, null!));
    }

    // --- Initial decorations ---

    [Fact]
    [Trait("Issue", "73")]
    public void Constructor_BuildsInitialDecorations()
    {
        var vcs = CreateVcs();
        vcs.SetChanges(new[]
        {
            new VcsFileChange("file.cs", VcsFileStatus.Modified),
            new VcsFileChange("new.cs", VcsFileStatus.Untracked)
        });

        var sut = new VcsDecorationProvider(vcs, _eventBus);
        Assert.Equal(2, sut.Decorations.Count);
    }

    // --- MapDecoration ---

    [Theory]
    [Trait("Issue", "73")]
    [InlineData(VcsFileStatus.Modified, false, "M", "yellow")]
    [InlineData(VcsFileStatus.Added, true, "A", "green")]
    [InlineData(VcsFileStatus.Deleted, false, "D", "red")]
    [InlineData(VcsFileStatus.Renamed, false, "R", "cyan")]
    [InlineData(VcsFileStatus.Untracked, false, "U", "gray")]
    [InlineData(VcsFileStatus.Conflict, false, "C", "magenta")]
    public void MapDecoration_ReturnsExpectedBadgeAndColor(
        VcsFileStatus status, bool isStaged, string expectedBadge, string expectedColor)
    {
        var (badge, color, _) = VcsDecorationProvider.MapDecoration(status, isStaged);
        Assert.Equal(expectedBadge, badge);
        Assert.Equal(expectedColor, color);
    }

    [Fact]
    [Trait("Issue", "73")]
    public void MapDecoration_Staged_IncludesStagedInTooltip()
    {
        var (_, _, tooltip) = VcsDecorationProvider.MapDecoration(VcsFileStatus.Modified, true);
        Assert.Contains("staged", tooltip);
    }

    [Fact]
    [Trait("Issue", "73")]
    public void MapDecoration_Unstaged_NoStagedInTooltip()
    {
        var (_, _, tooltip) = VcsDecorationProvider.MapDecoration(VcsFileStatus.Modified, false);
        Assert.DoesNotContain("staged", tooltip);
    }

    // --- Auto-refresh on VcsStatusChangedEvent ---

    [Fact]
    [Trait("Issue", "73")]
    public void Decorations_UpdateOnVcsStatusChangedEvent()
    {
        var vcs = CreateVcs();
        var sut = new VcsDecorationProvider(vcs, _eventBus);
        Assert.Empty(sut.Decorations);

        // Now add changes — VcsService.SetChanges publishes VcsStatusChangedEvent
        vcs.SetChanges(new[] { new VcsFileChange("a.cs", VcsFileStatus.Added) });

        Assert.Single(sut.Decorations);
        Assert.Equal("A", sut.Decorations[0].Badge);
    }

    [Fact]
    [Trait("Issue", "73")]
    public void DecorationsChanged_RaisedOnStatusEvent()
    {
        var vcs = CreateVcs();
        var sut = new VcsDecorationProvider(vcs, _eventBus);

        var raised = false;
        sut.DecorationsChanged += () => raised = true;

        vcs.SetChanges(new[] { new VcsFileChange("x.cs", VcsFileStatus.Deleted) });
        Assert.True(raised);
    }

    // --- Dispose ---

    [Fact]
    [Trait("Issue", "73")]
    public void Dispose_UnsubscribesFromEvents()
    {
        var vcs = CreateVcs();
        var sut = new VcsDecorationProvider(vcs, _eventBus);
        sut.Dispose();

        vcs.SetChanges(new[] { new VcsFileChange("y.cs", VcsFileStatus.Modified) });
        // After dispose, decorations should NOT be refreshed (still empty from ctor)
        Assert.Empty(sut.Decorations);
    }

    // --- Refresh ---

    [Fact]
    [Trait("Issue", "73")]
    public void Refresh_RecalculatesDecorations()
    {
        var vcs = CreateVcs();
        var sut = new VcsDecorationProvider(vcs, _eventBus);

        vcs.SetChanges(new[]
        {
            new VcsFileChange("a.cs", VcsFileStatus.Modified),
            new VcsFileChange("b.cs", VcsFileStatus.Untracked),
            new VcsFileChange("c.cs", VcsFileStatus.Added, true)
        });

        Assert.Equal(3, sut.Decorations.Count);

        var staged = sut.Decorations.Where(d => d.Tooltip.Contains("staged")).ToList();
        Assert.Single(staged);
    }
}

public sealed class VcsDecorationExtensionTests
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
    [Trait("Issue", "73")]
    public void Id_ReturnsExpected()
    {
        Assert.Equal("junexis.vcs.decorations", new VcsDecorationExtension().Id);
    }

    [Fact]
    [Trait("Issue", "73")]
    public void Initialize_WithoutVcs_DoesNotRegisterCapability()
    {
        var caps = new CapabilityRegistry();
        var ctx = CreateContext(caps);
        new VcsDecorationExtension().Initialize(ctx);

        Assert.False(caps.TryGet<VcsDecorationProvider>(out _));
    }

    [Fact]
    [Trait("Issue", "73")]
    public void Initialize_WithoutVcs_LogsWarning()
    {
        var ctx = CreateContext();
        new VcsDecorationExtension().Initialize(ctx);

        var snapshot = ((OutputService)ctx.Output).GetSnapshot();
        Assert.Contains(snapshot, e => e.Channel == "VCS" && e.Message.Contains("not available"));
    }

    [Fact]
    [Trait("Issue", "73")]
    public void Initialize_WithVcs_RegistersDecorationProvider()
    {
        var caps = new CapabilityRegistry();
        var vcs = new VcsService(
            backend: (_, _) => new VcsCommitResult(true, "x"),
            eventBus: new EventBus());
        caps.Register<IVcsService>(vcs);
        var ctx = CreateContext(caps);

        new VcsDecorationExtension().Initialize(ctx);

        Assert.True(caps.TryGet<VcsDecorationProvider>(out _));
    }

    [Fact]
    [Trait("Issue", "73")]
    public void Initialize_RegistersRefreshCommand()
    {
        var caps = new CapabilityRegistry();
        var vcs = new VcsService(
            backend: (_, _) => new VcsCommitResult(true, "x"),
            eventBus: new EventBus());
        caps.Register<IVcsService>(vcs);
        var ctx = CreateContext(caps);

        new VcsDecorationExtension().Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("vcs.decorations.refresh"));
    }

    [Fact]
    [Trait("Issue", "73")]
    public void Shutdown_BeforeInit_DoesNotThrow()
    {
        new VcsDecorationExtension().Shutdown();
    }

    // ── Context menu targets (Issue #101) ───────────────────────

    [Fact]
    [Trait("Issue", "101")]
    public void Initialize_RefreshCommand_HasContextTarget_Explorer()
    {
        var caps = new CapabilityRegistry();
        var vcs = new VcsService(
            backend: (_, _) => new VcsCommitResult(true, "x"),
            eventBus: new EventBus());
        caps.Register<IVcsService>(vcs);
        var ctx = CreateContext(caps);

        new VcsDecorationExtension().Initialize(ctx);

        var cmd = Assert.Single(ctx.Commands.GetRegisteredCommands(), c => c.Id == "vcs.decorations.refresh");
        Assert.Equal("explorer", cmd.ContextTarget);
    }
}
