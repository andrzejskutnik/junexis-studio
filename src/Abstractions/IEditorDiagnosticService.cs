namespace Junexis.Studio.Abstractions;

/// <summary>
/// Udostępnia diagnostyki w formie wygodnej dla edytora i nawigacji klawiaturą.
/// </summary>
public interface IEditorDiagnosticService
{
    /// <summary>Emitowane po każdej zmianie wspólnego zbioru diagnostyk.</summary>
    event Action? DiagnosticsChanged;

    /// <summary>Zwraca diagnostyki z lokacją należące do wskazanego pliku.</summary>
    IReadOnlyList<Diagnostic> GetForPath(string path);

    /// <summary>
    /// Zwraca diagnostyki trafiające w pozycję kursora. Linia i kolumna są liczone od jednego.
    /// </summary>
    IReadOnlyList<Diagnostic> GetAt(string path, int line, int column);

    /// <summary>
    /// Znajduje następną albo poprzednią diagnostykę względem bieżącej pozycji,
    /// z zawijaniem przez wszystkie pliki.
    /// </summary>
    Diagnostic? FindAdjacent(string? path, int line, int column, bool previous);
}
