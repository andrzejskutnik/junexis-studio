namespace Junexis.Studio.Abstractions;

/// <summary>
/// Zdarzenie domenowe: plik został zmieniony nazwą (rename) na dysku poza IDE.
/// </summary>
/// <param name="OldPath">Poprzednia pełna ścieżka do pliku.</param>
/// <param name="NewPath">Nowa pełna ścieżka do pliku.</param>
public sealed record FileRenamedExternally(string OldPath, string NewPath);
