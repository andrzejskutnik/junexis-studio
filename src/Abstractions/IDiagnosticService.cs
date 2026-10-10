namespace Junexis.Studio.Abstractions;

/// <summary>
/// Serwis diagnostyki (problemy/błędy/ostrzeżenia) niezależny od <see cref="IOutput"/>.
/// </summary>
public interface IDiagnosticService
{
    /// <summary>
    /// Zdarzenie emitowane po zmianie diagnostyk (Add/Clear).
    /// </summary>
    event Action? DiagnosticsChanged;

    /// <summary>
    /// Usuwa wszystkie diagnostyki.
    /// </summary>
    void Clear();

    /// <summary>
    /// Dodaje nową diagnostykę.
    /// </summary>
    /// <param name="diagnostic">Diagnostyka do dodania.</param>
    void Add(Diagnostic diagnostic);

    /// <summary>
    /// Zwraca snapshot diagnostyk.
    /// </summary>
    /// <returns>Snapshot diagnostyk.</returns>
    IReadOnlyList<Diagnostic> GetDiagnostics();

    /// <summary>
    /// Returns diagnostics filtered by source (owner), case-insensitive.
    /// </summary>
    /// <param name="source">The source identifier to filter by.</param>
    /// <returns>Diagnostics matching the given source.</returns>
    IReadOnlyList<Diagnostic> GetBySource(string source);

    /// <summary>
    /// Clears all diagnostics for a given source (owner), case-insensitive.
    /// </summary>
    /// <param name="source">The source identifier whose diagnostics should be removed.</param>
    void ClearBySource(string source);
}
