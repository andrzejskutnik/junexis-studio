using AvaloniaEdit;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Shell;

/// <summary>
/// Tracks the currently focused <see cref="TextEditor"/> and exposes clipboard commands for it.
/// </summary>
public sealed class EditorClipboardService : IEditorClipboardService
{
    private TextEditor? _activeEditor;

    /// <summary>
    /// Sets the currently active text editor.
    /// </summary>
    /// <param name="editor">Focused editor instance.</param>
    public void SetActiveEditor(TextEditor? editor)
    {
        _activeEditor = editor;
    }

    /// <summary>
    /// Clears the active editor only when it matches the given instance.
    /// </summary>
    /// <param name="editor">Editor instance being detached.</param>
    public void ClearActiveEditor(TextEditor? editor)
    {
        if (ReferenceEquals(_activeEditor, editor))
        {
            _activeEditor = null;
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        _activeEditor?.Undo();
    }

    /// <inheritdoc />
    public void Redo()
    {
        _activeEditor?.Redo();
    }

    /// <inheritdoc />
    public void Cut()
    {
        _activeEditor?.Cut();
    }

    /// <inheritdoc />
    public void Copy()
    {
        _activeEditor?.Copy();
    }

    /// <inheritdoc />
    public void Paste()
    {
        _activeEditor?.Paste();
    }

    /// <inheritdoc />
    public void SelectAll()
    {
        _activeEditor?.SelectAll();
    }

    /// <inheritdoc />
    public void SelectLine()
    {
        var editor = _activeEditor;
        var document = editor?.Document;
        if (editor is null || document is null)
        {
            return;
        }

        var caretLine = editor.TextArea.Caret.Line;
        var line = document.GetLineByNumber(caretLine);
        editor.SelectionStart = line.Offset;
        editor.SelectionLength = line.Length;
    }
}