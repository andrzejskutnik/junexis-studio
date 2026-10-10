using Junexis.Studio.Abstractions;
using System;

namespace Junexis.Studio.Host;

/// <summary>
/// Rozszerzenie edytora: rejestruje panel dokumentu "editor" i komendy menu kontekstowego.
/// </summary>
internal sealed class EditorExtension : IJunexisExtension
{
    private EditorPanelViewModel? _editorViewModel;
    private DocumentCommandService? _documentCommands;
    private IBreakpointService? _breakpoints;
    private IDebugSessionService? _debugSession;
    private ILanguageService? _languageService;
    private IEditorDiagnosticService? _editorDiagnostics;

    /// <inheritdoc />
    public string Id => "junexis.editor";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        _editorViewModel = new EditorPanelViewModel(context.Workspace, context.EventBus, context.Output, context.ActiveDocument);

        _documentCommands = new DocumentCommandService(_editorViewModel, context.ActiveDocument, context.Capabilities, context.Output);
        context.Capabilities.TryGet(out _breakpoints);
        context.Capabilities.TryGet(out _debugSession);
        context.Capabilities.TryGet(out _languageService);
        context.Capabilities.TryGet(out _editorDiagnostics);

        context.Panels.RegisterDocumentPanel(
            "editor",
            () => _editorViewModel);

        context.Commands.Register(
            id: "editor.save",
            execute: () => _documentCommands.SaveActiveDocument(),
            menuPath: "File/Save",
            displayName: "Save",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.closeActiveTab",
            execute: () => _documentCommands.CloseActiveTab(),
            menuPath: "File/Close Tab",
            displayName: "Close Tab",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.nextTab",
            execute: () => _documentCommands.ActivateNextTab(),
            menuPath: null,
            displayName: "Next Tab",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.previousTab",
            execute: () => _documentCommands.ActivatePreviousTab(),
            menuPath: null,
            displayName: "Previous Tab",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.undo",
            execute: () => _documentCommands.Undo(),
            menuPath: "Edit/Undo",
            displayName: "Undo",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.redo",
            execute: () => _documentCommands.Redo(),
            menuPath: "Edit/Redo",
            displayName: "Redo",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.cut",
            execute: () => _documentCommands.Cut(),
            menuPath: "Edit/Cut",
            displayName: "Cut",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.copy",
            execute: () => _documentCommands.Copy(),
            menuPath: "Edit/Copy",
            displayName: "Copy",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.paste",
            execute: () => _documentCommands.Paste(),
            menuPath: "Edit/Paste",
            displayName: "Paste",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.selectAll",
            execute: () => _documentCommands.SelectAll(),
            menuPath: "Edit/Select All",
            displayName: "Select All",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.selectLine",
            execute: () => _documentCommands.SelectLine(),
            menuPath: "Edit/Select Line",
            displayName: "Select Line",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.goToLine",
            execute: () => _ = _documentCommands.GoToLineAsync(),
            menuPath: "Edit/Go to Line...",
            displayName: "Go to Line...",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.toggleSearch",
            execute: () => _documentCommands.ToggleSearch(),
            menuPath: "Edit/Find",
            displayName: "Find",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.findNext",
            execute: () => _documentCommands.FindNext(),
            menuPath: "Edit/Find Next",
            displayName: "Find Next",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.findPrev",
            execute: () => _documentCommands.FindPrev(),
            menuPath: "Edit/Find Previous",
            displayName: "Find Previous",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.openReplace",
            execute: () => _documentCommands.OpenReplace(),
            menuPath: "Edit/Replace",
            displayName: "Replace",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.replaceNext",
            execute: () => _documentCommands.ReplaceNext(),
            menuPath: "Edit/Replace Next",
            displayName: "Replace Next",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.replaceAll",
            execute: () => _documentCommands.ReplaceAll(),
            menuPath: "Edit/Replace All",
            displayName: "Replace All",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.copyActivePath",
            execute: () => _documentCommands.CopyActivePath(),
            menuPath: "File/Copy File Path",
            displayName: "Copy File Path",
            category: "Editor",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.toggleBreakpoint",
            execute: () => ToggleBreakpoint(context),
            menuPath: "Debug/Toggle Breakpoint",
            displayName: "Toggle Breakpoint",
            category: "Debug",
            contextTarget: "editor");

        context.Commands.Register(
            id: "debug.runToCursor",
            execute: () => RunToCursor(context),
            menuPath: "Debug/Run to Cursor",
            displayName: "Run to Cursor",
            category: "Debug",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.goToDefinition",
            execute: () => _ = GoToDefinitionAsync(context),
            menuPath: "Navigate/Go to Definition",
            displayName: "Go to Definition",
            category: "Language",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.findReferences",
            execute: () => _ = FindReferencesAsync(context),
            menuPath: "Navigate/Find References",
            displayName: "Find References",
            category: "Language",
            contextTarget: "editor");

        context.Commands.Register(
            id: "language.restart",
            execute: () => _ = RestartLanguageServerAsync(context),
            menuPath: "Tools/Restart Language Server",
            displayName: "Restart Language Server",
            category: "Language",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.nextDiagnostic",
            execute: () => NavigateDiagnostic(context, previous: false),
            menuPath: "Navigate/Next Problem",
            displayName: "Next Problem",
            category: "Diagnostics",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.previousDiagnostic",
            execute: () => NavigateDiagnostic(context, previous: true),
            menuPath: "Navigate/Previous Problem",
            displayName: "Previous Problem",
            category: "Diagnostics",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.renameSymbol",
            execute: () => _ = RenameSymbolAsync(context),
            menuPath: "Refactor/Rename Symbol",
            displayName: "Rename Symbol",
            category: "Language",
            contextTarget: "editor");

        context.Commands.Register(
            id: "editor.quickFix",
            execute: () => _ = QuickFixAsync(context),
            menuPath: "Refactor/Quick Fix",
            displayName: "Quick Fix",
            category: "Language",
            contextTarget: "editor");

        context.Output.Write("Extensions", "EditorExtension loaded");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        // no-op (v0.1)
    }

    private void ToggleBreakpoint(IHostContext context)
    {
        var tab = _editorViewModel?.ActiveTab;
        if (tab is null || _breakpoints is null)
        {
            context.Output.Write("Debug", "No active editor document for breakpoint.", OutputSeverity.Warning);
            return;
        }
        var added = _breakpoints.Toggle(tab.FilePath, tab.CaretLine);
        context.Output.Write("Debug", $"Breakpoint {(added ? "added" : "removed")}: {tab.FilePath}:{tab.CaretLine}");
        if (_debugSession?.Snapshot.State is not DebugSessionState.Inactive and not DebugSessionState.Terminated)
        {
            var forFile = _breakpoints.GetBreakpoints()
                .Where(item => item.Path.Equals(tab.FilePath, StringComparison.OrdinalIgnoreCase)).ToArray();
            _ = UpdateSessionBreakpointsAsync(context, tab.FilePath, forFile);
        }
    }

    private async Task UpdateSessionBreakpointsAsync(
        IHostContext context,
        string path,
        IReadOnlyList<DebugSourceBreakpoint> breakpoints)
    {
        try
        {
            await _debugSession!.SetBreakpointsAsync(path, breakpoints).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Output.Write("Debug", $"Cannot update adapter breakpoints: {ex.Message}", OutputSeverity.Error);
        }
    }

    private void RunToCursor(IHostContext context)
    {
        var tab = _editorViewModel?.ActiveTab;
        if (tab is null || _debugSession is null)
        {
            context.Output.Write("Debug", "No active editor document for Run to Cursor.", OutputSeverity.Warning);
            return;
        }
        if (_debugSession.Snapshot.State != DebugSessionState.Stopped)
        {
            context.Output.Write("Debug", "Run to Cursor requires a stopped debug session.", OutputSeverity.Warning);
            return;
        }
        _ = RunToCursorAsync(context, tab.FilePath, tab.CaretLine, tab.CaretColumn);
    }

    private async Task RunToCursorAsync(IHostContext context, string path, int line, int column)
    {
        try
        {
            await _debugSession!.RunToLocationAsync(path, line, column).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Output.Write("Debug", $"Run to Cursor failed: {ex.Message}", OutputSeverity.Error);
        }
    }

    private async Task GoToDefinitionAsync(IHostContext context)
    {
        var tab = _editorViewModel?.ActiveTab;
        if (tab is null || _languageService is null || !_languageService.Supports(tab.FilePath))
        {
            context.Output.Write("Language Services", "No language server for the active document.", OutputSeverity.Warning);
            return;
        }
        try
        {
            var location = await _languageService.DefinitionAsync(
                tab.FilePath,
                new LanguagePosition(tab.CaretLine - 1, tab.CaretColumn - 1)).ConfigureAwait(false);
            if (location is null)
            {
                context.Output.Write("Language Services", "Definition not found.");
                return;
            }
            context.EventBus.Publish(new NavigateToLocationRequested(
                location.Path,
                location.Position.Line + 1,
                location.Position.Character + 1));
        }
        catch (Exception ex)
        {
            context.Output.Write("Language Services", $"Go to Definition failed: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private async Task FindReferencesAsync(IHostContext context)
    {
        var tab = _editorViewModel?.ActiveTab;
        if (tab is null || _languageService is null || !_languageService.Supports(tab.FilePath))
        {
            context.Output.Write("Language Services", "No language server for the active document.", OutputSeverity.Warning);
            return;
        }
        try
        {
            var locations = await _languageService.ReferencesAsync(
                tab.FilePath,
                new LanguagePosition(tab.CaretLine - 1, tab.CaretColumn - 1)).ConfigureAwait(false);
            context.EventBus.Publish(new ActivatePanelRequested("junexis.language-services"));
            if (locations.Count == 0)
            {
                context.Output.Write("Language Services", "References not found.");
                return;
            }
            context.Output.Write("Language Services", $"References: {locations.Count}.");
        }
        catch (Exception ex)
        {
            context.Output.Write("Language Services", $"Find References failed: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private async Task RestartLanguageServerAsync(IHostContext context)
    {
        var path = _editorViewModel?.ActiveTab?.FilePath;
        if (path is null || _languageService is null || !_languageService.Supports(path))
        {
            context.Output.Write("Language Services", "No language server for the active document.", OutputSeverity.Warning);
            return;
        }
        try
        {
            await _languageService.RestartAsync(path).ConfigureAwait(false);
            context.Output.Write("Language Services", "Language server restarted.");
        }
        catch (Exception ex)
        {
            context.Output.Write("Language Services", $"Restart failed: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private void NavigateDiagnostic(IHostContext context, bool previous)
    {
        if (_editorDiagnostics is null)
        {
            context.Output.Write("Problems", "Editor diagnostics are unavailable.", OutputSeverity.Warning);
            return;
        }
        var tab = _editorViewModel?.ActiveTab;
        var diagnostic = _editorDiagnostics.FindAdjacent(
            tab?.FilePath,
            tab?.CaretLine ?? 1,
            tab?.CaretColumn ?? 1,
            previous);
        if (diagnostic?.FilePath is null || diagnostic.Line is null || diagnostic.Column is null)
        {
            context.Output.Write("Problems", "No source diagnostics.");
            return;
        }
        context.EventBus.Publish(new NavigateToLocationRequested(
            diagnostic.FilePath,
            diagnostic.Line.Value,
            diagnostic.Column.Value));
        context.Output.Write(
            "Problems",
            $"{diagnostic.Severity}: {diagnostic.Message} ({diagnostic.FilePath}:{diagnostic.Line}:{diagnostic.Column})");
    }

    private async Task RenameSymbolAsync(IHostContext context)
    {
        var tab = _editorViewModel?.ActiveTab;
        if (tab is null || _languageService is null || !_languageService.Supports(tab.FilePath))
        {
            context.Output.Write("Language Services", "No language server for Rename Symbol.", OutputSeverity.Warning);
            return;
        }
        if (!context.Capabilities.TryGet<IWorkspaceInteractionService>(out var interaction)
            || interaction is null)
        {
            context.Output.Write("Language Services", "Rename prompt is unavailable.", OutputSeverity.Warning);
            return;
        }
        var current = WordAt(tab.Content, tab.CaretLine, tab.CaretColumn) ?? string.Empty;
        var newName = await interaction.PromptForTextAsync(
            "Rename Symbol",
            $"New name for '{current}':",
            current);
        if (string.IsNullOrWhiteSpace(newName) || newName == current)
            return;
        try
        {
            var count = await _languageService.RenameAsync(
                tab.FilePath,
                new LanguagePosition(tab.CaretLine - 1, tab.CaretColumn - 1),
                newName).ConfigureAwait(false);
            context.Output.Write("Language Services", $"Rename applied {count} edit(s).");
        }
        catch (Exception ex)
        {
            context.Output.Write("Language Services", $"Rename failed: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private async Task QuickFixAsync(IHostContext context)
    {
        var tab = _editorViewModel?.ActiveTab;
        if (tab is null || _languageService is null || !_languageService.Supports(tab.FilePath))
        {
            context.Output.Write("Language Services", "No language server for Quick Fix.", OutputSeverity.Warning);
            return;
        }
        try
        {
            var actions = await _languageService.CodeActionsAsync(
                tab.FilePath,
                new LanguagePosition(tab.CaretLine - 1, tab.CaretColumn - 1)).ConfigureAwait(false);
            if (actions.Count == 0)
            {
                context.Output.Write("Language Services", "No code actions at the current position.");
                return;
            }
            var selected = actions.FirstOrDefault(action => action.IsPreferred) ?? actions[0];
            if (actions.Count > 1
                && context.Capabilities.TryGet<IWorkspaceInteractionService>(out var interaction)
                && interaction is not null)
            {
                var choices = string.Join(Environment.NewLine,
                    actions.Select((action, index) => $"{index + 1}. {action.Title}"));
                var input = await interaction.PromptForTextAsync(
                    "Quick Fix",
                    $"Choose action:{Environment.NewLine}{choices}",
                    "1");
                if (!int.TryParse(input, out var choice) || choice < 1 || choice > actions.Count)
                    return;
                selected = actions[choice - 1];
            }
            var count = await _languageService.ApplyCodeActionAsync(selected).ConfigureAwait(false);
            context.Output.Write("Language Services", $"{selected.Title}: applied {count} edit(s).");
        }
        catch (Exception ex)
        {
            context.Output.Write("Language Services", $"Quick Fix failed: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private static string? WordAt(string content, int oneBasedLine, int oneBasedColumn)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        if (oneBasedLine < 1 || oneBasedLine > lines.Length)
            return null;
        var line = lines[oneBasedLine - 1];
        var position = Math.Clamp(oneBasedColumn - 1, 0, line.Length);
        var start = position;
        while (start > 0 && IsWord(line[start - 1]))
            start--;
        var end = position;
        while (end < line.Length && IsWord(line[end]))
            end++;
        return start < end ? line[start..end] : null;
    }

    private static bool IsWord(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '$';

}
