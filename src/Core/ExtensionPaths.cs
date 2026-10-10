namespace Junexis.Studio.Core;

/// <summary>
/// Konwencje ścieżek runtime extensions.
/// Host odwołuje się do tych ścieżek przy odkrywaniu rozszerzeń.
/// </summary>
public static class ExtensionPaths
{
    /// <summary>
    /// Nazwa folderu runtime extensions (obok pliku wykonywalnego).
    /// </summary>
    public const string FolderName = "extensions";

    /// <summary>
    /// Nazwa pliku manifestu rozszerzenia.
    /// </summary>
    public const string ManifestFileName = "extension.json";

    /// <summary>
    /// Zwraca ścieżkę bezwzględną folderu extensions obok danego katalogu bazowego.
    /// </summary>
    /// <param name="baseDirectory">Katalog bazowy (np. AppContext.BaseDirectory).</param>
    /// <returns>Ścieżka folderu extensions.</returns>
    public static string GetExtensionsRoot(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        return Path.Combine(baseDirectory, FolderName);
    }

    /// <summary>
    /// Zwraca ścieżkę do pliku extension.json w podfolderze danego rozszerzenia.
    /// </summary>
    /// <param name="baseDirectory">Katalog bazowy.</param>
    /// <param name="extensionFolderName">Nazwa podfolderu rozszerzenia.</param>
    /// <returns>Ścieżka do extension.json.</returns>
    public static string GetManifestPath(string baseDirectory, string extensionFolderName)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        if (string.IsNullOrWhiteSpace(extensionFolderName))
        {
            throw new ArgumentException("Extension folder name cannot be empty.", nameof(extensionFolderName));
        }

        return Path.Combine(baseDirectory, FolderName, extensionFolderName, ManifestFileName);
    }

    /// <summary>
    /// Zwraca listę podfolderów w katalogu extensions (każdy = jedno rozszerzenie).
    /// Jeśli folder extensions nie istnieje, zwraca pustą listę.
    /// </summary>
    /// <param name="baseDirectory">Katalog bazowy.</param>
    /// <returns>Lista ścieżek podfolderów rozszerzeń.</returns>
    public static IReadOnlyList<string> EnumerateExtensionFolders(string baseDirectory)
    {
        var root = GetExtensionsRoot(baseDirectory);

        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.GetDirectories(root);
    }
}
