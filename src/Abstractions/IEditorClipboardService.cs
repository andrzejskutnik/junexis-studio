namespace Junexis.Studio.Abstractions;

/// <summary>
/// Provides clipboard-oriented editor actions for the currently focused text editor.
/// </summary>
public interface IEditorClipboardService
{
    /// <summary>
    /// Undoes the last change in the focused editor.
    /// </summary>
    void Undo();

    /// <summary>
    /// Redoes the last undone change in the focused editor.
    /// </summary>
    void Redo();

    /// <summary>
    /// Cuts the current selection from the focused editor.
    /// </summary>
    void Cut();

    /// <summary>
    /// Copies the current selection from the focused editor.
    /// </summary>
    void Copy();

    /// <summary>
    /// Pastes clipboard contents into the focused editor.
    /// </summary>
    void Paste();

    /// <summary>
    /// Selects all text in the focused editor.
    /// </summary>
    void SelectAll();

    /// <summary>
    /// Selects the current caret line in the focused editor.
    /// </summary>
    void SelectLine();
}