namespace Junexis.Studio.Abstractions;

/// <summary>
/// Zdarzenie domenowe publikowane, gdy UI prosi o otwarcie pliku.
/// </summary>
/// <param name="Path">Ścieżka do pliku (zwykle absolutna).</param>
public sealed record FileOpenRequested(string Path);
