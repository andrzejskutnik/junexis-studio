namespace Junexis.Studio.Host;

/// <summary>
/// Rozpoznaje, czy wskazany folder jest katalogiem głównym kontekstu repozytorium Git.
/// </summary>
public static class GitRepositoryContext
{
    /// <summary>
    /// Sprawdza obecność znacznika <c>.git</c> bezpośrednio w wybranym folderze.
    /// Akceptuje katalog standardowego repozytorium oraz plik używany przez worktree i submodule.
    /// </summary>
    /// <param name="folderPath">Folder otwarty jako workspace.</param>
    /// <returns><see langword="true"/>, gdy folder zawiera znacznik <c>.git</c>.</returns>
    public static bool HasGitMarker(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        try
        {
            var markerPath = Path.Combine(Path.GetFullPath(folderPath), ".git");
            return Directory.Exists(markerPath) || File.Exists(markerPath);
        }
        catch
        {
            return false;
        }
    }
}
