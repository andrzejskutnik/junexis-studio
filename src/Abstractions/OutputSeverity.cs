namespace Junexis.Studio.Abstractions;

/// <summary>
/// Poziom istotności wpisu w output.
/// </summary>
public enum OutputSeverity
{
    /// <summary>
    /// Szczegółowe informacje diagnostyczne (najniższy poziom).
    /// </summary>
    Trace = 0,

    /// <summary>
    /// Informacja (domyślny poziom).
    /// </summary>
    Info = 1,

    /// <summary>
    /// Ostrzeżenie.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Błąd.
    /// </summary>
    Error = 3,

    /// <summary>
    /// Krytyczny błąd.
    /// </summary>
    Critical = 4,
}
