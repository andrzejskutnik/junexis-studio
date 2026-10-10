namespace Junexis.Studio.Abstractions;

/// <summary>
/// Typ panelu rejestrowanego przez rozszerzenia.
/// </summary>
public enum PanelType
{
    /// <summary>
    /// Panel narzędziowy (np. Explorer, Output).
    /// </summary>
    Tool = 0,

    /// <summary>
    /// Panel dokumentu (np. edytor pliku w zakładce).
    /// </summary>
    Document = 1,

    /// <summary>
    /// Panel tymczasowy (np. podgląd, wynik wyszukiwania jednorazowy).
    /// </summary>
    Temporary = 2,
}
