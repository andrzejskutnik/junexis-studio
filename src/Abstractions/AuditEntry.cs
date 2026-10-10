namespace Junexis.Studio.Abstractions;

/// <summary>
/// Niemutowalny wpis audytu — operacja wrażliwa lub administracyjna.
/// </summary>
/// <param name="Action">Opis operacji (np. "config.changed", "mcp.token.rotated").</param>
/// <param name="Detail">Szczegóły (maskowane, bez sekretów).</param>
/// <param name="Timestamp">Czas wystąpienia.</param>
/// <param name="Source">Opcjonalne źródło (np. id rozszerzenia, "Host").</param>
public sealed record AuditEntry(
    string Action,
    string Detail,
    DateTime Timestamp,
    string? Source = null);
