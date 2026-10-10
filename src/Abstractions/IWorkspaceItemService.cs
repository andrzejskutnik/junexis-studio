namespace Junexis.Studio.Abstractions;

/// <summary>
/// Serwis operacji na elementach workspace niezwiązanych z edycją treści dokumentu.
/// </summary>
public interface IWorkspaceItemService
{
    /// <summary>
    /// Tworzy nowy plik w katalogu wynikającym z aktualnego zaznaczenia w Explorerze.
    /// </summary>
    /// <param name="workspaceRoot">Root workspace.</param>
    /// <param name="selectedPath">Pełna ścieżka zaznaczonego elementu albo <see langword="null"/>.</param>
    /// <param name="selectedIsDirectory">Czy zaznaczony element jest katalogiem.</param>
    /// <param name="name">Opcjonalna nazwa nowego pliku.</param>
    /// <returns>Pełna ścieżka utworzonego pliku albo <see langword="null"/>.</returns>
    string? CreateFile(string? workspaceRoot, string? selectedPath, bool selectedIsDirectory, string? name = null);

    /// <summary>
    /// Tworzy nowy katalog w katalogu wynikającym z aktualnego zaznaczenia w Explorerze.
    /// </summary>
    /// <param name="workspaceRoot">Root workspace.</param>
    /// <param name="selectedPath">Pełna ścieżka zaznaczonego elementu albo <see langword="null"/>.</param>
    /// <param name="selectedIsDirectory">Czy zaznaczony element jest katalogiem.</param>
    /// <param name="name">Opcjonalna nazwa katalogu.</param>
    /// <returns>Pełna ścieżka utworzonego katalogu albo <see langword="null"/>.</returns>
    string? CreateFolder(string? workspaceRoot, string? selectedPath, bool selectedIsDirectory, string? name = null);

    /// <summary>
    /// Usuwa wskazany plik albo pusty katalog.
    /// </summary>
    /// <param name="path">Pełna ścieżka elementu.</param>
    /// <param name="isDirectory">Czy element jest katalogiem.</param>
    /// <returns><see langword="true"/> gdy operacja się powiodła.</returns>
    bool Delete(string path, bool isDirectory);

    /// <summary>
    /// Zmienia nazwę wskazanego pliku albo katalogu.
    /// </summary>
    /// <param name="path">Pełna ścieżka elementu źródłowego.</param>
    /// <param name="isDirectory">Czy element jest katalogiem.</param>
    /// <param name="newName">Nowa nazwa bez ścieżki.</param>
    /// <returns><see langword="true"/> gdy operacja się powiodła.</returns>
    bool Rename(string path, bool isDirectory, string newName);

    /// <summary>
    /// Przenosi wskazany plik albo katalog do innego katalogu docelowego.
    /// </summary>
    /// <param name="path">Pełna ścieżka elementu źródłowego.</param>
    /// <param name="isDirectory">Czy element jest katalogiem.</param>
    /// <param name="destinationDir">Pełna ścieżka katalogu docelowego.</param>
    /// <returns><see langword="true"/> gdy operacja się powiodła.</returns>
    bool Move(string path, bool isDirectory, string destinationDir);
}