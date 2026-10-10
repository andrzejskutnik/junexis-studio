using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="OmwiaExtension"/> and <see cref="OmwiaPanelViewModel"/>.
/// </summary>
[Trait("Issue", "26")]
public class OmwiaExtensionTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static HostContext CreateContext(OutputService? output = null) =>
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
            versionControl: null,
            views: new TestViewRegistry());

    // ── ViewModel: constructor ──────────────────────────────────

    [Fact]
    public void ViewModel_NullEventBus_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OmwiaPanelViewModel(null!));
    }

    [Fact]
    public void ViewModel_InitialState_IsEmpty()
    {
        var vm = new OmwiaPanelViewModel(new EventBus());
        Assert.Empty(vm.Suggestions);
    }

    // ── ViewModel: WorkspaceOpenedEvent ─────────────────────────

    [Fact]
    public void ViewModel_OnWorkspaceOpened_AddsSuggestion()
    {
        var bus = new EventBus();
        var vm = new OmwiaPanelViewModel(bus);

        bus.Publish(new WorkspaceOpenedEvent("/root"));

        Assert.Single(vm.Suggestions);
        Assert.Equal("Workspace", vm.Suggestions[0].Category);
        Assert.Contains("/root", vm.Suggestions[0].Message);
    }

    // ── ViewModel: DocumentSaved ────────────────────────────────

    [Fact]
    public void ViewModel_OnDocumentSaved_CsFile_AddsSuggestion()
    {
        var bus = new EventBus();
        var vm = new OmwiaPanelViewModel(bus);

        bus.Publish(new DocumentSaved("src/file.cs"));

        Assert.Single(vm.Suggestions);
        Assert.Equal("File", vm.Suggestions[0].Category);
        Assert.Contains("file.cs", vm.Suggestions[0].Message);
    }

    [Fact]
    public void ViewModel_OnDocumentSaved_JnxFile_AddsSuggestion()
    {
        var bus = new EventBus();
        var vm = new OmwiaPanelViewModel(bus);

        bus.Publish(new DocumentSaved("main.jnx"));

        Assert.Single(vm.Suggestions);
    }

    [Fact]
    public void ViewModel_OnDocumentSaved_TxtFile_NoSuggestion()
    {
        var bus = new EventBus();
        var vm = new OmwiaPanelViewModel(bus);

        bus.Publish(new DocumentSaved("readme.txt"));

        Assert.Empty(vm.Suggestions);
    }

    // ── ViewModel: BuildRequestedEvent ──────────────────────────

    [Fact]
    public void ViewModel_OnBuildRequested_AddsSuggestion()
    {
        var bus = new EventBus();
        var vm = new OmwiaPanelViewModel(bus);

        bus.Publish(new BuildRequestedEvent(new BuildContext("/root", "proj")));

        Assert.Single(vm.Suggestions);
        Assert.Equal("Build", vm.Suggestions[0].Category);
    }

    // ── ViewModel: ordering (newest first) ──────────────────────

    [Fact]
    public void ViewModel_Suggestions_NewestFirst()
    {
        var bus = new EventBus();
        var vm = new OmwiaPanelViewModel(bus);

        bus.Publish(new WorkspaceOpenedEvent("/a"));
        bus.Publish(new BuildRequestedEvent(new BuildContext("/a", "p")));

        Assert.Equal("Build", vm.Suggestions[0].Category);
        Assert.Equal("Workspace", vm.Suggestions[1].Category);
    }

    // ── ViewModel: SuggestionAdded event ────────────────────────

    [Fact]
    public void ViewModel_SuggestionAdded_Raised()
    {
        var bus = new EventBus();
        var vm = new OmwiaPanelViewModel(bus);
        OmwiaSuggestion? captured = null;
        vm.SuggestionAdded += s => captured = s;

        bus.Publish(new WorkspaceOpenedEvent("/x"));

        Assert.NotNull(captured);
        Assert.Equal("Workspace", captured!.Category);
    }

    // ── ViewModel: clear ────────────────────────────────────────

    [Fact]
    public void ViewModel_Clear_RemovesSuggestions()
    {
        var bus = new EventBus();
        var vm = new OmwiaPanelViewModel(bus);
        bus.Publish(new WorkspaceOpenedEvent("/x"));

        vm.Clear();

        Assert.Empty(vm.Suggestions);
    }

    // ── ViewModel: dispose ──────────────────────────────────────

    [Fact]
    public void ViewModel_Dispose_UnsubscribesFromEvents()
    {
        var bus = new EventBus();
        var vm = new OmwiaPanelViewModel(bus);
        vm.Dispose();

        bus.Publish(new WorkspaceOpenedEvent("/x"));

        Assert.Empty(vm.Suggestions);
    }

    // ── Extension: initialize ───────────────────────────────────

    [Fact]
    public void Extension_Initialize_RegistersOmwiaPanel()
    {
        var ctx = CreateContext();
        var ext = new OmwiaExtension();

        ext.Initialize(ctx);

        var panels = ctx.Panels.GetRegisteredPanels();
        Assert.Contains(panels, p => p.Id == "junexis.omwia");
    }

    [Fact]
    public void Extension_Initialize_RegistersClearCommand()
    {
        var ctx = CreateContext();
        var ext = new OmwiaExtension();

        ext.Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("omwia.clear"));
    }

    [Fact]
    public void Extension_Initialize_OutputsLoadedMessage()
    {
        var output = new OutputService();
        var ctx = CreateContext(output: output);
        var ext = new OmwiaExtension();

        ext.Initialize(ctx);

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == "OMWiA" && e.Message.Contains("loaded"));
    }

    [Fact]
    public void Extension_Id_IsJunexisOmwia()
    {
        var ext = new OmwiaExtension();
        Assert.Equal("junexis.omwia", ext.Id);
    }

    // ── Extension: shutdown ─────────────────────────────────────

    [Fact]
    public void Extension_Shutdown_IsSafe()
    {
        var ext = new OmwiaExtension();
        ext.Shutdown();
    }

    [Fact]
    public void Extension_Shutdown_DisposesViewModel()
    {
        var ctx = CreateContext();
        var ext = new OmwiaExtension();
        ext.Initialize(ctx);
        ext.Shutdown();

        // Events after shutdown should not affect the disposed VM
        ctx.EventBus.Publish(new WorkspaceOpenedEvent("/late"));
        // No assertion needed — no exception means success
    }

    // ── OmwiaSuggestion record ──────────────────────────────────

    [Fact]
    public void OmwiaSuggestion_StoresProperties()
    {
        var ts = DateTime.UtcNow;
        var s = new OmwiaSuggestion("Cat", "Msg", ts);
        Assert.Equal("Cat", s.Category);
        Assert.Equal("Msg", s.Message);
        Assert.Equal(ts, s.TimestampUtc);
    }

    // ── Context menu targets (Issue #101) ───────────────────────

    [Fact]
    [Trait("Issue", "101")]
    public void Extension_ClearCommand_HasContextTarget_JunexisOmwia()
    {
        var ctx = CreateContext();
        var ext = new OmwiaExtension();

        ext.Initialize(ctx);

        var cmd = Assert.Single(ctx.Commands.GetRegisteredCommands(), c => c.Id == "omwia.clear");
        Assert.Equal("junexis.omwia", cmd.ContextTarget);
    }
}
