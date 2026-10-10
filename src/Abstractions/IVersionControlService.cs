namespace Junexis.Studio.Abstractions;

/// <summary>
/// Capability systemu kontroli wersji (minimal v0.1: Git przez CLI).
/// </summary>
public interface IVersionControlService
{
    /// <summary>
    /// Sprawdza, czy podana ścieżka znajduje się w repozytorium.
    /// </summary>
    /// <param name="path">Ścieżka do folderu.</param>
    /// <returns><see langword="true"/> jeśli ścieżka jest w repozytorium.</returns>
    bool IsRepository(string path);

    /// <summary>
    /// Zwraca status plików w repozytorium dla aktualnego workspace.
    /// </summary>
    /// <returns>Lista wpisów statusu.</returns>
    IReadOnlyList<VersionControlFileEntry> GetRepositoryStatus();

    /// <summary>
    /// Zwraca status pojedynczego pliku.
    /// </summary>
    /// <param name="path">Ścieżka absolutna lub względna do root repozytorium.</param>
    /// <returns>Status pliku.</returns>
    VersionControlFileStatus GetFileStatus(string path);

    /// <summary>
    /// Wykonuje minimalny commit zmian w repozytorium.
    /// </summary>
    /// <param name="message">Wiadomość commita.</param>
    void Commit(string message);
}
