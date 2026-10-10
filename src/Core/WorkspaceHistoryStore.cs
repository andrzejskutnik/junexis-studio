using System.Text.Json;

namespace Junexis.Studio.Core;

/// <summary>
/// Przechowuje ostatnio otwarty folder workspace użytkownika.
/// Domyślna ścieżka: <c>%APPDATA%/JunexisStudio/workspace-state.json</c>.
/// </summary>
public sealed class WorkspaceHistoryStore
{
    private const int MaxRecentFolders = 10;
    private readonly string _filePath;

    /// <summary>
    /// Tworzy store używający domyślnej ścieżki stanu użytkownika.
    /// </summary>
    public WorkspaceHistoryStore()
        : this(filePath: null)
    {
    }

    /// <summary>
    /// Tworzy store z jawną ścieżką, przydatną między innymi w testach.
    /// </summary>
    /// <param name="filePath">Ścieżka pliku JSON lub <see langword="null"/> dla ścieżki domyślnej.</param>
    public WorkspaceHistoryStore(string? filePath)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JunexisStudio",
            "workspace-state.json");
    }

    /// <summary>
    /// Zwraca ostatnio otwarty folder, o ile zapis nadal wskazuje istniejący katalog.
    /// </summary>
    /// <returns>Pełna ścieżka folderu lub <see langword="null"/>.</returns>
    public string? LoadLastFolder()
        => LoadRecentFolders().FirstOrDefault();

    /// <summary>
    /// Zwraca ostatnio otwierane, nadal istniejące foldery od najnowszego.
    /// </summary>
    /// <returns>Lista pełnych ścieżek bez duplikatów.</returns>
    public IReadOnlyList<string> LoadRecentFolders()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(_filePath));
            var candidates = state?.RecentFolders is { Count: > 0 }
                ? state.RecentFolders
                : string.IsNullOrWhiteSpace(state?.LastFolder)
                    ? []
                    : [state.LastFolder];

            return candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentFolders)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Zapisuje istniejący folder jako ostatnio otwarty workspace.
    /// Błędy zapisu nie przerywają działania IDE.
    /// </summary>
    /// <param name="folderPath">Ścieżka istniejącego folderu.</param>
    public void SaveLastFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(folderPath);
            if (!Directory.Exists(fullPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            var recent = LoadRecentFolders()
                .Where(path => !string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase))
                .Prepend(fullPath)
                .Take(MaxRecentFolders)
                .ToArray();

            var json = JsonSerializer.Serialize(
                new WorkspaceState(fullPath, recent),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Stan ostatniego workspace jest wygodą, nie warunkiem działania aplikacji.
        }
    }

    private sealed record WorkspaceState(
        string? LastFolder,
        IReadOnlyList<string>? RecentFolders = null,
        Dictionary<string, WorkspaceSession>? Sessions = null);

    /// <summary>Persisted tab session for a specific workspace folder.</summary>
    public sealed record WorkspaceSession(
        IReadOnlyList<string> OpenFiles,
        string? ActiveFile = null);

    /// <summary>Loads the saved tab session for <paramref name="folderPath"/>.</summary>
    public WorkspaceSession? LoadSession(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return null;
        try
        {
            if (!File.Exists(_filePath)) return null;
            var state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(_filePath));
            var key = Path.GetFullPath(folderPath);
            if (state?.Sessions?.TryGetValue(key, out var session) == true)
                return session;
        }
        catch { }
        return null;
    }

    /// <summary>Saves the tab session for <paramref name="folderPath"/>.</summary>
    public void SaveSession(string folderPath, WorkspaceSession session)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        try
        {
            var key = Path.GetFullPath(folderPath);
            var parent = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            WorkspaceState existing;
            try { existing = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(_filePath)) ?? new WorkspaceState(null); }
            catch { existing = new WorkspaceState(null); }

            var sessions = existing.Sessions is not null
                ? new Dictionary<string, WorkspaceSession>(existing.Sessions, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, WorkspaceSession>(StringComparer.OrdinalIgnoreCase);

            sessions[key] = session;
            var updated = existing with { Sessions = sessions };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
