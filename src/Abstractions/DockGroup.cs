namespace Junexis.Studio.Abstractions;

/// <summary>
/// Grupa dokowania wskazująca preferowaną lokalizację panelu w powłoce.
/// </summary>
public enum DockGroup
{
    /// <summary>
    /// Lewa strona (np. Explorer, drzewo plików).
    /// </summary>
    Left = 0,

    /// <summary>
    /// Prawa strona (np. panel właściwości).
    /// </summary>
    Right = 1,

    /// <summary>
    /// Dolna strefa (np. Output, Terminal).
    /// </summary>
    Bottom = 2,

    /// <summary>
    /// Centralny obszar dokumentów (np. edytor).
    /// </summary>
    Center = 3,
}
