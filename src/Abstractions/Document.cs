namespace Junexis.Studio.Abstractions;

/// <summary>
/// Model dokumentu edytora (v0.1).
/// </summary>
/// <param name="Id">Identyfikator dokumentu w systemie.</param>
/// <param name="FilePath">Pełna ścieżka do pliku.</param>
/// <param name="Content">Treść dokumentu.</param>
/// <param name="IsDirty">Czy dokument ma niezapisane zmiany.</param>
/// <param name="LastSavedTimestamp">Czas ostatniego zapisu (UTC) lub <see langword="null"/>, gdy nieznany.</param>
public sealed record Document(
    DocumentId Id,
    string FilePath,
    string Content,
    bool IsDirty,
    DateTimeOffset? LastSavedTimestamp);
