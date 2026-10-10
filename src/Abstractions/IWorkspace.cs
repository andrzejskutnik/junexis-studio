namespace Junexis.Studio.Abstractions;

/// <summary>
/// Minimalny workspace (folder roboczy + prosty IO) dla v0.1.
/// </summary>
public interface IWorkspace
{
    /// <summary>
    /// Aktualny root workspace (ustawiany przez <see cref="OpenFolder"/>).
    /// </summary>
    string? RootPath { get; }

    /// <summary>
    /// Informuje o otwarciu pliku (v0.1: lokalnie, poza EventBus).
    /// </summary>
    event Action<string>? FileOpened;

    /// <summary>
    /// Informuje o otwarciu folderu workspace (v0.1: lokalnie, poza EventBus).
    /// </summary>
    event Action<string>? FolderOpened;

    /// <summary>
    /// Informuje o zapisie pliku (v0.1: lokalnie, poza EventBus).
    /// </summary>
    event Action<string>? FileSaved;

    /// <summary>
    /// Ustawia folder roboczy.
    /// </summary>
    /// <param name="path">Ścieżka do folderu.</param>
    void OpenFolder(string path);

    /// <summary>
    /// Odczytuje zawartość pliku.
    /// </summary>
    /// <param name="path">Ścieżka absolutna lub względna do <see cref="RootPath"/>.</param>
    /// <returns>Zawartość pliku.</returns>
    string ReadFile(string path);

    /// <summary>
    /// Zapisuje zawartość pliku.
    /// </summary>
    /// <param name="path">Ścieżka absolutna lub względna do <see cref="RootPath"/>.</param>
    /// <param name="content">Treść do zapisu.</param>
    void WriteFile(string path, string content);

    /// <summary>
    /// Zwraca surową treść pliku konfiguracyjnego workspace (<see cref="WorkspaceConfig.FileName"/>).
    /// Brak pliku nie jest błędem krytycznym i skutkuje <see langword="null"/>.
    /// </summary>
    /// <returns>Surowy JSON lub <see langword="null"/> gdy brak pliku lub nie otwarto workspace.</returns>
    string? GetWorkspaceConfigRaw();

    /// <summary>
    /// Zapisuje surową treść pliku konfiguracyjnego workspace (<see cref="WorkspaceConfig.FileName"/>).
    /// </summary>
    /// <param name="content">Treść do zapisu.</param>
    void SetWorkspaceConfigRaw(string content);
}
