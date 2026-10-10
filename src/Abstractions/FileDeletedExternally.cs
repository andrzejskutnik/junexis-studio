namespace Junexis.Studio.Abstractions;

/// <summary>
/// Zdarzenie domenowe: plik został usunięty na dysku poza IDE.
/// </summary>
/// <param name="Path">Pełna ścieżka do pliku.</param>
public sealed record FileDeletedExternally(string Path);
