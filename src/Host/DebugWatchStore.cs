using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

internal static class DebugWatchStore
{
    internal static readonly string RelativePath = Path.Combine(".junexis", "studio-watches.json");

    internal static IReadOnlyList<string> Load(IWorkspace workspace)
    {
        var path = Resolve(workspace);
        if (path is null || !File.Exists(path))
            return [];
        var values = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
        return values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal static void Save(IWorkspace workspace, IReadOnlyList<string> expressions)
    {
        var path = Resolve(workspace);
        if (path is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(expressions, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string? Resolve(IWorkspace workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.RootPath))
            return null;
        var root = Path.GetFullPath(workspace.RootPath);
        var path = Path.GetFullPath(Path.Combine(root, RelativePath));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Debug watch path escapes the workspace root.");
        return path;
    }
}
