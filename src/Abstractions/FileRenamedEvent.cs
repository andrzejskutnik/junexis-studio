namespace Junexis.Studio.Abstractions;

/// <summary>
/// Publikowane po pomyślnej zmianie nazwy pliku lub katalogu w Explorerze.
/// </summary>
/// <param name="OldPath">Poprzednia pełna ścieżka.</param>
/// <param name="NewPath">Nowa pełna ścieżka po zmianie nazwy.</param>
public sealed record FileRenamedEvent(string OldPath, string NewPath);
