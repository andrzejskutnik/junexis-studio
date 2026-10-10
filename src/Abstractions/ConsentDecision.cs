namespace Junexis.Studio.Abstractions;

/// <summary>
/// Decyzja użytkownika w sprawie uprawnienia.
/// </summary>
public enum ConsentDecision
{
    /// <summary>Brak decyzji (prompt wymagany).</summary>
    Unknown = 0,

    /// <summary>Uprawnienie przyznane.</summary>
    Allow = 1,

    /// <summary>Uprawnienie odrzucone.</summary>
    Deny = 2,
}
