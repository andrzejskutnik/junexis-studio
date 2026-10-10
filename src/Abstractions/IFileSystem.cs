namespace Junexis.Studio.Abstractions;

/// <summary>
/// Abstrakcja dostępu do systemu plików.
/// Pozwala odseparować usługi Core od <c>System.IO</c> w celu testów i przyszłej wirtualizacji.
/// </summary>
public interface IFileSystem
{
    /// <summary>
    /// Odczytuje zawartość pliku.
    /// </summary>
    /// <param name="path">Pełna ścieżka do pliku.</param>
    /// <returns>Zawartość pliku.</returns>
    string ReadFile(string path);

    /// <summary>
    /// Zapisuje zawartość do pliku.
    /// </summary>
    /// <param name="path">Pełna ścieżka do pliku.</param>
    /// <param name="content">Treść do zapisu.</param>
    void WriteFile(string path, string content);

    /// <summary>
    /// Sprawdza, czy ścieżka istnieje (plik lub katalog).
    /// </summary>
    /// <param name="path">Pełna ścieżka.</param>
    /// <returns><see langword="true"/> jeśli istnieje.</returns>
    bool Exists(string path);

    /// <summary>
    /// Enumeruje wpisy katalogu (pliki i podkatalogi).
    /// </summary>
    /// <param name="path">Pełna ścieżka katalogu.</param>
    /// <returns>Strumień pełnych ścieżek wpisów.</returns>
    IEnumerable<string> EnumerateDirectory(string path);

    /// <summary>
    /// Uruchamia obserwowanie zmian w katalogu.
    /// </summary>
    /// <param name="rootPath">Pełna ścieżka katalogu root.</param>
    /// <param name="includeSubdirectories">Czy obserwować podkatalogi.</param>
    /// <param name="onChange">Callback wywoływany przy zdarzeniu zmiany.</param>
    /// <returns>Handle do zatrzymania obserwacji.</returns>
    IDisposable Watch(string rootPath, bool includeSubdirectories, Action<FileSystemChange> onChange);
}

/// <summary>
/// Zdarzenie zmiany w systemie plików.
/// </summary>
public sealed record FileSystemChange(FileSystemChangeKind Kind, string Path, string? OldPath = null);

/// <summary>
/// Rodzaj zmiany w systemie plików.
/// </summary>
public enum FileSystemChangeKind
{
    /// <summary>
    /// Plik/katalog uległ zmianie.
    /// </summary>
    Changed = 0,

    /// <summary>
    /// Plik/katalog został usunięty.
    /// </summary>
    Deleted = 1,

    /// <summary>
    /// Plik/katalog został zmieniony nazwą (rename).
    /// </summary>
    Renamed = 2,
}
