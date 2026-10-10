namespace Junexis.Studio.Abstractions;

/// <summary>
/// Kategoria zdarzenia diagnostyki systemowej.
/// </summary>
public enum SystemDiagnosticCategory
{
    /// <summary>
    /// Błąd ładowania/inicjalizacji rozszerzenia.
    /// </summary>
    ExtensionLoad = 0,

    /// <summary>
    /// Wyjątek rzucony przez kod rozszerzenia w runtime.
    /// </summary>
    ExtensionException = 1,

    /// <summary>
    /// Konflikt wersji (np. zależności).
    /// </summary>
    VersionConflict = 2,

    /// <summary>
    /// Błąd wewnętrzny hosta.
    /// </summary>
    HostError = 3,

    /// <summary>
    /// Inne zdarzenie systemowe.
    /// </summary>
    Other = 4,
}
