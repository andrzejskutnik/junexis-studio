using Junexis.Studio.Abstractions;
using System.Globalization;

namespace Junexis.Studio.Host;

/// <summary>
/// Encapsulates document-focused command behavior used by the editor extension.
/// </summary>
public sealed class DocumentCommandService
{
    private readonly EditorPanelViewModel _editorViewModel;
    private readonly IActiveDocumentService _activeDocument;
    private readonly ICapabilityRegistry _capabilities;
    private readonly IOutput _output;

    /// <summary>
    /// Creates a new document command service for the given editor panel.
    /// </summary>
    /// <param name="editorViewModel">Editor panel view model.</param>
    /// <param name="activeDocument">Active document service.</param>
    /// <param name="capabilities">Capability registry for optional shell integrations.</param>
    /// <param name="output">Output service used for user-facing messages.</param>
    public DocumentCommandService(EditorPanelViewModel editorViewModel, IActiveDocumentService activeDocument, ICapabilityRegistry capabilities, IOutput output)
    {
        _editorViewModel = editorViewModel ?? throw new ArgumentNullException(nameof(editorViewModel));
        _activeDocument = activeDocument ?? throw new ArgumentNullException(nameof(activeDocument));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Saves the active document if present.
    /// </summary>
    public void SaveActiveDocument()
    {
        if (_editorViewModel.ActiveTab is null)
        {
            _output.Write("Editor", "No active file to save.");
            return;
        }

        _editorViewModel.Save();
    }

    /// <summary>
    /// Requests closing the active document tab if present.
    /// </summary>
    public void CloseActiveTab()
    {
        var tab = _editorViewModel.ActiveTab;
        if (tab is null)
        {
            _output.Write("Editor", "No active tab to close.");
            return;
        }

        tab.RequestClose();
    }

    /// <summary>
    /// Toggles the search panel for the active document.
    /// </summary>
    public void ToggleSearch() => _editorViewModel.ToggleSearch();

    /// <summary>
    /// Opens the search and replace panel for the active document.
    /// </summary>
    public void OpenReplace() => _editorViewModel.OpenReplace();

    /// <summary>
    /// Moves to the next search match in the active document.
    /// </summary>
    public void FindNext() => _editorViewModel.FindNext();

    /// <summary>
    /// Moves to the previous search match in the active document.
    /// </summary>
    public void FindPrev() => _editorViewModel.FindPrev();

    /// <summary>
    /// Replaces the next search match in the active document.
    /// </summary>
    public void ReplaceNext() => _editorViewModel.ReplaceNext();

    /// <summary>
    /// Replaces all search matches in the active document.
    /// </summary>
    public void ReplaceAll() => _editorViewModel.ReplaceAll();

    /// <summary>
    /// Activates the next open document tab.
    /// </summary>
    public void ActivateNextTab() => _editorViewModel.ActivateNextTab();

    /// <summary>
    /// Activates the previous open document tab.
    /// </summary>
    public void ActivatePreviousTab() => _editorViewModel.ActivatePreviousTab();

    /// <summary>
    /// Prompts for a target location and navigates the active document to it.
    /// </summary>
    public async Task GoToLineAsync()
    {
        var tab = _editorViewModel.ActiveTab;
        if (tab is null)
        {
            _output.Write("Editor", "No active file.");
            return;
        }

        if (!_capabilities.TryGet<IWorkspaceInteractionService>(out var interaction) || interaction is null)
        {
            _output.Write("Editor", "Go to Line is unavailable.");
            return;
        }

        var input = await interaction.PromptForTextAsync(
            title: "Go to Line",
            message: "Enter target line or line:column:",
            initialValue: $"{tab.CaretLine.ToString(CultureInfo.InvariantCulture)}:{tab.CaretColumn.ToString(CultureInfo.InvariantCulture)}");

        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (!TryParseLocation(input, out var line, out var column))
        {
            _output.Write("Editor", $"Invalid target location: {input}");
            return;
        }

        tab.NavigateTo(line, column);
    }

    private static bool TryParseLocation(string input, out int line, out int column)
    {
        line = 0;
        column = 1;

        var parts = input.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Length > 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out line) || line < 1)
        {
            return false;
        }

        if (parts.Length == 1)
        {
            column = 1;
            return true;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out column) || column < 1)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reports the path of the active document.
    /// </summary>
    public void CopyActivePath()
    {
        var path = _editorViewModel.CurrentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = _activeDocument.ActivePath;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            _output.Write("Editor", "No active file.");
            return;
        }

        _output.Write("Editor", $"Path copied: {path}");
    }

    /// <summary>
    /// Cuts the current selection in the focused editor, if available.
    /// </summary>
    public void Cut()
    {
        if (_capabilities.TryGet<IEditorClipboardService>(out var clipboard) && clipboard is not null)
        {
            clipboard.Cut();
        }
    }

    /// <summary>
    /// Copies the current selection in the focused editor, if available.
    /// </summary>
    public void Copy()
    {
        if (_capabilities.TryGet<IEditorClipboardService>(out var clipboard) && clipboard is not null)
        {
            clipboard.Copy();
        }
    }

    /// <summary>
    /// Pastes clipboard contents into the focused editor, if available.
    /// </summary>
    public void Paste()
    {
        if (_capabilities.TryGet<IEditorClipboardService>(out var clipboard) && clipboard is not null)
        {
            clipboard.Paste();
        }
    }

    /// <summary>
    /// Undoes the last change in the focused editor, if available.
    /// </summary>
    public void Undo()
    {
        if (_capabilities.TryGet<IEditorClipboardService>(out var clipboard) && clipboard is not null)
        {
            clipboard.Undo();
        }
    }

    /// <summary>
    /// Redoes the last undone change in the focused editor, if available.
    /// </summary>
    public void Redo()
    {
        if (_capabilities.TryGet<IEditorClipboardService>(out var clipboard) && clipboard is not null)
        {
            clipboard.Redo();
        }
    }

    /// <summary>
    /// Selects all text in the focused editor, if available.
    /// </summary>
    public void SelectAll()
    {
        if (_capabilities.TryGet<IEditorClipboardService>(out var clipboard) && clipboard is not null)
        {
            clipboard.SelectAll();
        }
    }

    /// <summary>
    /// Selects the current line in the focused editor, if available.
    /// </summary>
    public void SelectLine()
    {
        if (_capabilities.TryGet<IEditorClipboardService>(out var clipboard) && clipboard is not null)
        {
            clipboard.SelectLine();
        }
    }
}