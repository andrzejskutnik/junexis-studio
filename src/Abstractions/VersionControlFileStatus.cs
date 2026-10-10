namespace Junexis.Studio.Abstractions;

/// <summary>
/// Status pliku w systemie kontroli wersji.
/// </summary>
public enum VersionControlFileStatus
{
    /// <summary>
    /// Plik jest czysty (brak zmian).
    /// </summary>
    Committed = 0,

    /// <summary>
    /// Plik jest zmodyfikowany (w working tree).
    /// </summary>
    Modified = 1,

    /// <summary>
    /// Plik jest staged.
    /// </summary>
    Staged = 2,

    /// <summary>
    /// Plik jest nieśledzony.
    /// </summary>
    Untracked = 3,

    /// <summary>
    /// Plik jest ignorowany.
    /// </summary>
    Ignored = 4,
}
