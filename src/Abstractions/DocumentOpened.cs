namespace Junexis.Studio.Abstractions;

/// <summary>
/// Zdarzenie domenowe: użytkownik otworzył dokument (plik) w edytorze.
/// </summary>
/// <param name="Path">Pełna ścieżka do pliku.</param>
/// <param name="Content">Treść dokumentu po otwarciu.</param>
public sealed record DocumentOpened(string Path, string Content);
