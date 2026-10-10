namespace Junexis.Studio.Abstractions;

/// <summary>
/// Identyfikator dokumentu w systemie (v0.1).
/// </summary>
/// <param name="Value">Wartość identyfikatora.</param>
public readonly record struct DocumentId(Guid Value)
{
    /// <summary>
    /// Tworzy nowy, losowy identyfikator dokumentu.
    /// </summary>
    /// <returns>Nowy <see cref="DocumentId"/>.</returns>
    public static DocumentId New() => new(Guid.NewGuid());
}
