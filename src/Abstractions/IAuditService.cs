namespace Junexis.Studio.Abstractions;

/// <summary>
/// Serwis audytu — rejestruje operacje wrażliwe/administracyjne.
/// Brak implementacji audytu nie wywraca systemu (fail-soft).
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Rejestruje wpis audytu.
    /// </summary>
    /// <param name="entry">Wpis audytu.</param>
    void Record(AuditEntry entry);

    /// <summary>
    /// Zwraca snapshot wpisów audytu (in-memory).
    /// </summary>
    /// <returns>Snapshot wpisów.</returns>
    IReadOnlyList<AuditEntry> GetEntries();

    /// <summary>
    /// Zdarzenie emitowane po nowym wpisie audytu.
    /// </summary>
    event Action<AuditEntry>? EntryRecorded;
}
