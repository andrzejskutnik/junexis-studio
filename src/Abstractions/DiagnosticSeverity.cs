namespace Junexis.Studio.Abstractions;

/// <summary>
/// Poziom ważności diagnostyki.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Informacja.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Ostrzeżenie.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Błąd.
    /// </summary>
    Error = 2,
}
