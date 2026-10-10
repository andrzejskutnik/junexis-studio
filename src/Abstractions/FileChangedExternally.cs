namespace Junexis.Studio.Abstractions;

/// <summary>
/// Zdarzenie domenowe: plik zmienił się na dysku poza IDE (np. zewnętrzny edytor, git checkout).
/// </summary>
/// <param name="Path">Pełna ścieżka do pliku.</param>
public sealed record FileChangedExternally(string Path);
