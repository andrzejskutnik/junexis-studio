namespace Junexis.Studio.Abstractions;

/// <summary>
/// Zdarzenie domenowe: użytkownik zapisał dokument (plik) w edytorze.
/// </summary>
/// <param name="Path">Pełna ścieżka do pliku.</param>
public sealed record DocumentSaved(string Path);
