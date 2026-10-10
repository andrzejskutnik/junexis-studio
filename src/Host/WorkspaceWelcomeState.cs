namespace Junexis.Studio.Host;

/// <summary>
/// Stan prezentowany przez ekrany powitalne Studio.
/// </summary>
public sealed record WorkspaceWelcomeState(
    bool HasWorkspace,
    string WorkspaceName,
    string? WorkspacePath,
    bool IsGitRepository,
    IReadOnlyList<RecentWorkspaceItem> RecentWorkspaces)
{
    /// <summary>
    /// Buduje spójny stan bieżącego i ostatnio otwieranych workspace.
    /// </summary>
    public static WorkspaceWelcomeState Create(
        string? workspaceRoot,
        IReadOnlyList<string>? recentFolders)
    {
        var root = NormalizeExistingDirectory(workspaceRoot);
        var recent = (recentFolders ?? [])
            .Select(NormalizeExistingDirectory)
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var displayName = new DirectoryInfo(path).Name;
                return new RecentWorkspaceItem(
                    string.IsNullOrWhiteSpace(displayName) ? path : displayName,
                    path,
                    GitRepositoryContext.HasGitMarker(path));
            })
            .ToArray();

        var workspaceName = root is null
            ? "No folder open"
            : new DirectoryInfo(root).Name;

        return new WorkspaceWelcomeState(
            root is not null,
            string.IsNullOrWhiteSpace(workspaceName) ? root ?? "No folder open" : workspaceName,
            root,
            GitRepositoryContext.HasGitMarker(root),
            recent);
    }

    private static string? NormalizeExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Directory.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch
        {
            return null;
        }
    }
}
