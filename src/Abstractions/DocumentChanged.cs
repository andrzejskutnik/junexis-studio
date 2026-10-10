namespace Junexis.Studio.Abstractions;

/// <summary>
/// Zdarzenie domenowe: treść aktywnego dokumentu zmieniła się w edytorze (np. wpisywanie).
/// </summary>
/// <param name="Path">Pełna ścieżka do pliku.</param>
/// <param name="Content">Aktualna treść dokumentu po zmianie.</param>
public sealed record DocumentChanged(string Path, string Content);
