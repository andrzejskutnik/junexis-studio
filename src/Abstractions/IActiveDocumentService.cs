namespace Junexis.Studio.Abstractions;

/// <summary>
/// Serwis utrzymujący informację o aktualnie aktywnym dokumencie (pliku) w IDE.
/// </summary>
public interface IActiveDocumentService
{
    /// <summary>
    /// Zdarzenie emitowane po zmianie aktywnego dokumentu.
    /// </summary>
    event Action? ActiveDocumentChanged;

    /// <summary>
    /// Pełna ścieżka do aktywnego dokumentu lub <see langword="null"/>, jeśli brak.
    /// </summary>
    string? ActivePath { get; }

    /// <summary>
    /// Ustawia aktywny dokument.
    /// </summary>
    /// <param name="path">Pełna ścieżka do pliku lub <see langword="null"/> aby wyczyścić.</param>
    void SetActivePath(string? path);
}
