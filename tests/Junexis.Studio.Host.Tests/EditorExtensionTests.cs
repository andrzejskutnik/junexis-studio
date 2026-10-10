using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Testy integracyjne <see cref="EditorExtension"/> — issue #103 (Ctrl+S / Ctrl+W).
/// </summary>
[Trait("Issue", "103")]
public sealed class EditorExtensionTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static HostContext CreateContext(OutputService? output = null)
        => new(
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
            views: new TestViewRegistry(),
            capabilities: new CapabilityRegistry());

    // ── Testy komend ─────────────────────────────────────────────

    [Fact]
    public void Initialize_RegistersEditorSaveCommand()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        Assert.True(context.Commands.CanExecute("editor.save"));
    }

    [Fact]
    public void Initialize_RegistersEditorCloseActiveTabCommand()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        Assert.True(context.Commands.CanExecute("editor.closeActiveTab"));
    }

    [Fact]
    [Trait("Issue", "106")]
    public void Initialize_RegistersEditorTabCycleCommands()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        Assert.True(context.Commands.CanExecute("editor.nextTab"));
        Assert.True(context.Commands.CanExecute("editor.previousTab"));
    }

    [Fact]
    public void Initialize_RegistersEditorFileMenuCommands()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        var commands = context.Commands.GetRegisteredCommands();

        Assert.Contains(commands, c => c.Id == "editor.save" && c.MenuPath == "File/Save");
        Assert.Contains(commands, c => c.Id == "editor.copyActivePath" && c.MenuPath == "File/Copy File Path");
        Assert.Contains(commands, c => c.Id == "editor.closeActiveTab" && c.MenuPath == "File/Close Tab");
        Assert.Contains(commands, c => c.Id == "debug.runToCursor" && c.MenuPath == "Debug/Run to Cursor");
    }

    [Fact]
    public void Initialize_RegistersClipboardEditCommands()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        var commands = context.Commands.GetRegisteredCommands();

        Assert.Contains(commands, c => c.Id == "editor.undo" && c.MenuPath == "Edit/Undo");
        Assert.Contains(commands, c => c.Id == "editor.redo" && c.MenuPath == "Edit/Redo");
        Assert.Contains(commands, c => c.Id == "editor.cut" && c.MenuPath == "Edit/Cut");
        Assert.Contains(commands, c => c.Id == "editor.copy" && c.MenuPath == "Edit/Copy");
        Assert.Contains(commands, c => c.Id == "editor.paste" && c.MenuPath == "Edit/Paste");
        Assert.Contains(commands, c => c.Id == "editor.selectAll" && c.MenuPath == "Edit/Select All");
        Assert.Contains(commands, c => c.Id == "editor.selectLine" && c.MenuPath == "Edit/Select Line");
        Assert.Contains(commands, c => c.Id == "editor.goToLine" && c.MenuPath == "Edit/Go to Line...");
        Assert.Contains(commands, c => c.Id == "editor.openReplace" && c.MenuPath == "Edit/Replace");
    }

    [Fact]
    public void EditorSave_NoActiveTab_LogsToOutput()
    {
        var output = new OutputService();
        var context = CreateContext(output);
        var ext = new EditorExtension();
        ext.Initialize(context);

        // Brak aktywnego dokumentu — komenda powinna zalogować info
        context.Commands.Execute("editor.save");

        var logs = output.GetSnapshot();
        Assert.Contains(logs, e => e.Channel == "Editor" && e.Message.Contains("No active file"));
    }

    [Fact]
    public void EditorCloseActiveTab_NoActiveTab_LogsToOutput()
    {
        var output = new OutputService();
        var context = CreateContext(output);
        var ext = new EditorExtension();
        ext.Initialize(context);

        context.Commands.Execute("editor.closeActiveTab");

        var logs = output.GetSnapshot();
        Assert.Contains(logs, e => e.Channel == "Editor" && e.Message.Contains("No active tab"));
    }

    [Fact]
    [Trait("Issue", "106")]
    public void EditorTabCycle_NoTabs_DoesNotThrow()
    {
        var context = CreateContext();
        var ext = new EditorExtension();
        ext.Initialize(context);

        var ex = Record.Exception(() =>
        {
            context.Commands.Execute("editor.nextTab");
            context.Commands.Execute("editor.previousTab");
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_RegistersEditorDocumentPanel()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        var panels = context.Panels.GetRegisteredPanels();
        Assert.Contains(panels, p => p.Id == "editor" && p.Type == PanelType.Document);
    }

    [Fact]
    public void DocumentPanelFactory_ReturnsSameInstance()
    {
        var context = CreateContext();
        var ext = new EditorExtension();
        ext.Initialize(context);

        var reg = context.Panels.GetRegisteredPanels().First(p => p.Id == "editor");
        var instance1 = reg.Factory();
        var instance2 = reg.Factory();

        // Fabryka powinna zwracać ten sam egzemplarz (eager creation)
        Assert.Same(instance1, instance2);
        Assert.IsType<EditorPanelViewModel>(instance1);
    }

    [Fact]
    [Trait("Issue", "104")]
    public void Initialize_RegistersEditorToggleSearchCommand()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        Assert.True(context.Commands.CanExecute("editor.toggleSearch"));
    }

    [Fact]
    [Trait("Issue", "104")]
    public void EditorToggleSearch_NoActiveTab_DoesNotThrow()
    {
        var context = CreateContext();
        var ext = new EditorExtension();
        ext.Initialize(context);

        // Brak aktywnego taba — nie powinno rzucić wyjątku
        var ex = Record.Exception(() => context.Commands.Execute("editor.toggleSearch"));
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_RegistersEditorReplaceAllCommand()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        Assert.True(context.Commands.CanExecute("editor.replaceAll"));
    }

    [Fact]
    public void EditorReplaceAll_NoActiveTab_DoesNotThrow()
    {
        var context = CreateContext();
        var ext = new EditorExtension();
        ext.Initialize(context);

        var ex = Record.Exception(() => context.Commands.Execute("editor.replaceAll"));
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_RegistersEditorFindNextCommand()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        Assert.True(context.Commands.CanExecute("editor.findNext"));
    }

    [Fact]
    public void EditorFindNext_NoActiveTab_DoesNotThrow()
    {
        var context = CreateContext();
        var ext = new EditorExtension();
        ext.Initialize(context);

        var ex = Record.Exception(() => context.Commands.Execute("editor.findNext"));
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_RegistersEditorFindPrevCommand()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        Assert.True(context.Commands.CanExecute("editor.findPrev"));
    }

    [Fact]
    public void EditorFindPrev_NoActiveTab_DoesNotThrow()
    {
        var context = CreateContext();
        var ext = new EditorExtension();
        ext.Initialize(context);

        var ex = Record.Exception(() => context.Commands.Execute("editor.findPrev"));
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_RegistersEditorOpenReplaceCommand()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        Assert.True(context.Commands.CanExecute("editor.openReplace"));
    }

    [Fact]
    public void EditorOpenReplace_NoActiveTab_DoesNotThrow()
    {
        var context = CreateContext();
        var ext = new EditorExtension();
        ext.Initialize(context);

        var ex = Record.Exception(() => context.Commands.Execute("editor.openReplace"));
        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_RegistersEditorReplaceNextCommand()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        Assert.True(context.Commands.CanExecute("editor.replaceNext"));
    }

    [Fact]
    public void EditorReplaceNext_NoActiveTab_DoesNotThrow()
    {
        var context = CreateContext();
        var ext = new EditorExtension();
        ext.Initialize(context);

        var ex = Record.Exception(() => context.Commands.Execute("editor.replaceNext"));
        Assert.Null(ex);
    }

    [Fact]
    public void ClipboardCommands_WithoutClipboardCapability_DoNotThrow()
    {
        var context = CreateContext();
        var ext = new EditorExtension();
        ext.Initialize(context);

        var ex = Record.Exception(() =>
        {
            context.Commands.Execute("editor.undo");
            context.Commands.Execute("editor.redo");
            context.Commands.Execute("editor.cut");
            context.Commands.Execute("editor.copy");
            context.Commands.Execute("editor.paste");
            context.Commands.Execute("editor.selectAll");
            context.Commands.Execute("editor.selectLine");
            context.Commands.Execute("editor.goToLine");
            context.Commands.Execute("editor.openReplace");
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Initialize_RegistersEditorCopyActivePathCommand()
    {
        var context = CreateContext();
        var ext = new EditorExtension();

        ext.Initialize(context);

        Assert.True(context.Commands.CanExecute("editor.copyActivePath"));
    }

    [Fact]
    public void EditorCopyActivePath_NoActivePath_LogsToOutput()
    {
        var output = new OutputService();
        var context = CreateContext(output);
        var ext = new EditorExtension();
        ext.Initialize(context);

        context.Commands.Execute("editor.copyActivePath");

        var logs = output.GetSnapshot();
        Assert.Contains(logs, e => e.Channel == "Editor" && e.Message.Contains("No active file"));
    }

    [Fact]
    public void EditorCopyActivePath_WithActivePath_LogsCopiedPath()
    {
        var output = new OutputService();
        var context = CreateContext(output);
        context.ActiveDocument.SetActivePath(@"C:\temp\main.jnx");

        var ext = new EditorExtension();
        ext.Initialize(context);

        context.Commands.Execute("editor.copyActivePath");

        var logs = output.GetSnapshot();
        Assert.Contains(logs, e =>
            e.Channel == "Editor" &&
            e.Message.Contains("Path copied:") &&
            e.Message.Contains(@"C:\temp\main.jnx"));
    }
}
