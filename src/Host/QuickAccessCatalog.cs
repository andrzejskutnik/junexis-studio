using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Rodzaj elementu dostępnego w palecie szybkiego dostępu.
/// </summary>
public enum QuickAccessItemKind
{
    Command,
    File,
}

/// <summary>
/// Lekki, niezależny od UI model pozycji palety.
/// </summary>
public sealed record QuickAccessItem(
    string Title,
    string Description,
    string? Shortcut,
    string Value,
    QuickAccessItemKind Kind);

/// <summary>
/// Buduje i filtruje katalog poleceń oraz plików dla szybkiej nawigacji.
/// </summary>
public static class QuickAccessCatalog
{
    private const int DefaultFileLimit = 5000;
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".svn",
        ".hg",
        ".vs",
        ".idea",
        ".junexis",
        "bin",
        "obj",
        "node_modules",
    };

    /// <summary>
    /// Tworzy listę aktualnie wykonywalnych poleceń.
    /// </summary>
    public static IReadOnlyList<QuickAccessItem> CreateCommands(
        ICommandRegistry commands,
        IKeybindingRegistry keybindings)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(keybindings);

        var shortcuts = keybindings.GetBindings()
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => string.Join(", ", group.Select(pair => pair.Key.ToString())),
                StringComparer.Ordinal);

        return commands.GetRegisteredCommands()
            .Where(command => commands.CanExecute(command.Id))
            .Select(command =>
            {
                var title = GetCommandTitle(command);
                var description = string.IsNullOrWhiteSpace(command.Category)
                    ? command.Id
                    : $"{command.Category} · {command.Id}";
                shortcuts.TryGetValue(command.Id, out var shortcut);
                return new QuickAccessItem(title, description, shortcut, command.Id, QuickAccessItemKind.Command);
            })
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Indeksuje pliki workspace, pomijając katalogi narzędziowe i zależności.
    /// </summary>
    public static IReadOnlyList<QuickAccessItem> CreateFiles(string? workspaceRoot, int limit = DefaultFileLimit)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot) || limit <= 0)
        {
            return Array.Empty<QuickAccessItem>();
        }

        var root = Path.GetFullPath(workspaceRoot);
        var result = new List<QuickAccessItem>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0 && result.Count < limit)
        {
            var directory = pending.Pop();
            foreach (var file in SafeEnumerateFiles(directory))
            {
                var relativePath = Path.GetRelativePath(root, file);
                result.Add(new QuickAccessItem(
                    Path.GetFileName(file),
                    relativePath,
                    null,
                    Path.GetFullPath(file),
                    QuickAccessItemKind.File));

                if (result.Count >= limit)
                {
                    break;
                }
            }

            foreach (var child in SafeEnumerateDirectories(directory))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }

        return result
            .OrderBy(item => item.Description, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Filtruje pozycje z lekkim fuzzy matchingiem i zwraca najlepsze trafienia.
    /// </summary>
    public static IReadOnlyList<QuickAccessItem> Search(
        IEnumerable<QuickAccessItem> items,
        string? query,
        int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (limit <= 0)
        {
            return Array.Empty<QuickAccessItem>();
        }

        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length == 0)
        {
            return items.Take(limit).ToArray();
        }

        return items
            .Select(item => (Item: item, Score: Score(item, normalizedQuery)))
            .Where(candidate => candidate.Score >= 0)
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Title.Length)
            .ThenBy(candidate => candidate.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(candidate => candidate.Item)
            .ToArray();
    }

    private static string GetCommandTitle(CommandRegistration command)
    {
        if (!string.IsNullOrWhiteSpace(command.DisplayName))
        {
            return command.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(command.MenuPath))
        {
            var separator = command.MenuPath.LastIndexOf('/');
            return separator >= 0 ? command.MenuPath[(separator + 1)..] : command.MenuPath;
        }

        return command.Id;
    }

    private static int Score(QuickAccessItem item, string query)
    {
        var title = item.Title;
        var titleIndex = title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (titleIndex == 0)
        {
            return 0;
        }

        if (titleIndex > 0)
        {
            return 10 + titleIndex;
        }

        var searchable = $"{title} {item.Description} {item.Value}";
        var fullIndex = searchable.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (fullIndex >= 0)
        {
            return 100 + fullIndex;
        }

        var queryIndex = 0;
        var gaps = 0;
        var previousMatch = -1;
        for (var index = 0; index < searchable.Length && queryIndex < query.Length; index++)
        {
            if (char.ToUpperInvariant(searchable[index]) != char.ToUpperInvariant(query[queryIndex]))
            {
                continue;
            }

            if (previousMatch >= 0)
            {
                gaps += index - previousMatch - 1;
            }

            previousMatch = index;
            queryIndex++;
        }

        return queryIndex == query.Length ? 500 + gaps : -1;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
