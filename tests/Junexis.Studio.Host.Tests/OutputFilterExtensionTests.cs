using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="OutputFilterExtension"/> and <see cref="OutputPanelViewModel"/>.
/// </summary>
[Trait("Issue", "67")]
public class OutputFilterExtensionTests
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
    public void ViewModel_NullOutput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OutputPanelViewModel(null!));
    }

    [Fact]
    public void ViewModel_SeedsWithExistingEntries()
    {
        var output = new OutputService();
        output.Write("A", "msg1");
        output.Write("B", "msg2");

        var vm = new OutputPanelViewModel(output);

        Assert.Equal(2, vm.FilteredEntries.Count);
    }

    // ── ViewModel: live updates ─────────────────────────────────

    [Fact]
    public void ViewModel_LiveUpdate_NewEntryAppears()
    {
        var output = new OutputService();
        var vm = new OutputPanelViewModel(output);

        output.Write("Test", "hello");

        Assert.Single(vm.FilteredEntries);
        Assert.Equal("hello", vm.FilteredEntries[0].Message);
    }

    [Fact]
    public void ViewModel_LiveUpdate_RaisesEntriesChanged()
    {
        var output = new OutputService();
        var vm = new OutputPanelViewModel(output);
        var raised = false;
        vm.EntriesChanged += () => raised = true;

        output.Write("Test", "hello");

        Assert.True(raised);
    }

    // ── ViewModel: channel filter ───────────────────────────────

    [Fact]
    public void ViewModel_ChannelFilter_ShowsOnlyMatchingChannel()
    {
        var output = new OutputService();
        output.Write("A", "a1");
        output.Write("B", "b1");
        output.Write("A", "a2");

        var vm = new OutputPanelViewModel(output);
        vm.SetChannelFilter("A");

        Assert.Equal(2, vm.FilteredEntries.Count);
        Assert.All(vm.FilteredEntries, e => Assert.Equal("A", e.Channel));
    }

    [Fact]
    public void ViewModel_ChannelFilter_NullShowsAll()
    {
        var output = new OutputService();
        output.Write("A", "a1");
        output.Write("B", "b1");

        var vm = new OutputPanelViewModel(output);
        vm.SetChannelFilter("A");
        vm.SetChannelFilter(null);

        Assert.Equal(2, vm.FilteredEntries.Count);
    }

    [Fact]
    public void ViewModel_ChannelFilter_RaisesEntriesChanged()
    {
        var output = new OutputService();
        var vm = new OutputPanelViewModel(output);
        var raised = false;
        vm.EntriesChanged += () => raised = true;

        vm.SetChannelFilter("X");

        Assert.True(raised);
    }

    // ── ViewModel: severity filter ──────────────────────────────

    [Fact]
    public void ViewModel_SeverityFilter_ShowsOnlyMatchingSeverity()
    {
        var output = new OutputService();
        output.Write("A", "info", OutputSeverity.Info);
        output.Write("A", "warn", OutputSeverity.Warning);
        output.Write("A", "err", OutputSeverity.Error);

        var vm = new OutputPanelViewModel(output);
        vm.SetSeverityFilter(OutputSeverity.Warning);

        Assert.Single(vm.FilteredEntries);
        Assert.Equal("warn", vm.FilteredEntries[0].Message);
    }

    [Fact]
    public void ViewModel_SeverityFilter_NullShowsAll()
    {
        var output = new OutputService();
        output.Write("A", "info", OutputSeverity.Info);
        output.Write("A", "err", OutputSeverity.Error);

        var vm = new OutputPanelViewModel(output);
        vm.SetSeverityFilter(OutputSeverity.Error);
        vm.SetSeverityFilter(null);

        Assert.Equal(2, vm.FilteredEntries.Count);
    }

    // ── ViewModel: combined filters ─────────────────────────────

    [Fact]
    public void ViewModel_CombinedFilters_ChannelAndSeverity()
    {
        var output = new OutputService();
        output.Write("A", "a-info", OutputSeverity.Info);
        output.Write("A", "a-err", OutputSeverity.Error);
        output.Write("B", "b-err", OutputSeverity.Error);

        var vm = new OutputPanelViewModel(output);
        vm.SetChannelFilter("A");
        vm.SetSeverityFilter(OutputSeverity.Error);

        Assert.Single(vm.FilteredEntries);
        Assert.Equal("a-err", vm.FilteredEntries[0].Message);
    }

    // ── ViewModel: clear ────────────────────────────────────────

    [Fact]
    public void ViewModel_Clear_RemovesAllLocalEntries()
    {
        var output = new OutputService();
        output.Write("A", "msg1");
        output.Write("B", "msg2");

        var vm = new OutputPanelViewModel(output);
        vm.Clear();

        Assert.Empty(vm.FilteredEntries);
    }

    [Fact]
    public void ViewModel_Clear_DoesNotAffectOutputService()
    {
        var output = new OutputService();
        output.Write("A", "msg1");

        var vm = new OutputPanelViewModel(output);
        vm.Clear();

        // Underlying output still has the entry
        Assert.Single(output.GetSnapshot());
    }

    [Fact]
    public void ViewModel_Clear_RaisesEntriesChanged()
    {
        var output = new OutputService();
        var vm = new OutputPanelViewModel(output);
        var raised = false;
        vm.EntriesChanged += () => raised = true;

        vm.Clear();

        Assert.True(raised);
    }

    [Fact]
    public void ViewModel_Clear_NewEntriesStillAppear()
    {
        var output = new OutputService();
        output.Write("A", "old");

        var vm = new OutputPanelViewModel(output);
        vm.Clear();

        output.Write("A", "new");

        Assert.Single(vm.FilteredEntries);
        Assert.Equal("new", vm.FilteredEntries[0].Message);
    }

    // ── ViewModel: available channels ───────────────────────────

    [Fact]
    public void ViewModel_AvailableChannels_ReflectsOutputService()
    {
        var output = new OutputService();
        output.RegisterChannel("Build");
        output.RegisterChannel("VCS");

        var vm = new OutputPanelViewModel(output);

        Assert.Contains("Build", vm.AvailableChannels);
        Assert.Contains("VCS", vm.AvailableChannels);
    }

    // ── ViewModel: dispose ──────────────────────────────────────

    [Fact]
    public void ViewModel_Dispose_UnsubscribesFromEvents()
    {
        var output = new OutputService();
        var vm = new OutputPanelViewModel(output);
        vm.Dispose();

        output.Write("Test", "after dispose");

        // Should not have received the new entry (unsubscribed)
        Assert.Empty(vm.FilteredEntries);
    }

    // ── Extension: initialize ───────────────────────────────────

    [Fact]
    public void Extension_Initialize_RegistersOutputPanel()
    {
        var ctx = CreateContext();
        var ext = new OutputFilterExtension();

        ext.Initialize(ctx);

        var panels = ctx.Panels.GetRegisteredPanels();
        Assert.Contains(panels, p => p.Id == "junexis.output.filtered");
    }

    [Fact]
    public void Extension_Initialize_RegistersClearCommand()
    {
        var ctx = CreateContext();
        var ext = new OutputFilterExtension();

        ext.Initialize(ctx);

        Assert.True(ctx.Commands.CanExecute("output.clear"));
    }

    [Fact]
    public void Extension_Initialize_OutputsLoadedMessage()
    {
        var output = new OutputService();
        var ctx = CreateContext(output: output);
        var ext = new OutputFilterExtension();

        ext.Initialize(ctx);

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e =>
            e.Channel == "Extensions" && e.Message.Contains("loaded"));
    }

    [Fact]
    public void Extension_Id_IsJunexisOutputFilter()
    {
        var ext = new OutputFilterExtension();
        Assert.Equal("junexis.output.filter", ext.Id);
    }

    [Fact]
    public void Extension_Shutdown_IsSafe()
    {
        var ext = new OutputFilterExtension();
        ext.Shutdown();
    }

    [Fact]
    public void Extension_ClearCommand_ClearsViewModel()
    {
        var output = new OutputService();
        output.Write("A", "msg");
        var ctx = CreateContext(output: output);
        var ext = new OutputFilterExtension();

        ext.Initialize(ctx);

        // Get the view model from the panel
        var panel = ctx.Panels.GetRegisteredPanels().First(p => p.Id == "junexis.output.filtered");
        var vm = (OutputPanelViewModel)panel.Factory();

        // Execute clear via command
        ctx.Commands.Execute("output.clear");

        Assert.Empty(vm.FilteredEntries);
    }

    // ── Context menu targets (Issue #101) ───────────────────────

    [Fact]
    [Trait("Issue", "101")]
    public void Extension_ClearCommand_HasContextTarget_JunexisOutputFiltered()
    {
        var ctx = CreateContext();
        var ext = new OutputFilterExtension();

        ext.Initialize(ctx);

        var cmd = Assert.Single(ctx.Commands.GetRegisteredCommands(), c => c.Id == "output.clear");
        Assert.Equal("junexis.output.filtered", cmd.ContextTarget);
    }
}
