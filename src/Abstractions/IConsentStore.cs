namespace Junexis.Studio.Abstractions;

/// <summary>
/// Consent store — przechowuje decyzje użytkownika (allow/deny) dotyczące uprawnień extension.
/// Decyzje mogą być per-workspace lub globalne.
/// </summary>
public interface IConsentStore
{
    /// <summary>
    /// Pobiera decyzję dla danego rozszerzenia i uprawnienia.
    /// </summary>
    /// <param name="extensionId">Identyfikator rozszerzenia.</param>
    /// <param name="permission">Nazwa uprawnienia (np. "fs.read").</param>
    /// <returns>Decyzja użytkownika.</returns>
    ConsentDecision GetDecision(string extensionId, string permission);

    /// <summary>
    /// Zapisuje decyzję użytkownika.
    /// </summary>
    /// <param name="extensionId">Identyfikator rozszerzenia.</param>
    /// <param name="permission">Nazwa uprawnienia.</param>
    /// <param name="decision">Decyzja (Allow/Deny).</param>
    void SetDecision(string extensionId, string permission, ConsentDecision decision);

    /// <summary>
    /// Sprawdza, czy rozszerzenie ma przyznane dane uprawnienie.
    /// </summary>
    /// <param name="extensionId">Identyfikator rozszerzenia.</param>
    /// <param name="permission">Nazwa uprawnienia.</param>
    /// <returns><see langword="true"/> jeśli Allow.</returns>
    bool IsAllowed(string extensionId, string permission);

    /// <summary>
    /// Zwraca wszystkie decyzje dla danego rozszerzenia.
    /// </summary>
    /// <param name="extensionId">Identyfikator rozszerzenia.</param>
    /// <returns>Słownik permission → decision.</returns>
    IReadOnlyDictionary<string, ConsentDecision> GetDecisions(string extensionId);

    /// <summary>
    /// Resetuje wszystkie decyzje dla danego rozszerzenia.
    /// </summary>
    /// <param name="extensionId">Identyfikator rozszerzenia.</param>
    void ResetDecisions(string extensionId);
}
