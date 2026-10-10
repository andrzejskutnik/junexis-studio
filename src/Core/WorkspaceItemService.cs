using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Domyślna implementacja <see cref="IWorkspaceItemService"/> dla operacji na plikach i katalogach workspace.
/// </summary>
public sealed class WorkspaceItemService : IWorkspaceItemService
{
    private readonly IEventBus _eventBus;
    private readonly IOutput _output;

    /// <summary>
    /// Tworzy serwis operacji na elementach workspace.
    /// </summary>
    /// <param name="eventBus">Event bus do publikacji rename eventów.</param>
    /// <param name="output">Output do raportowania błędów i warningów.</param>
    public WorkspaceItemService(IEventBus eventBus, IOutput output)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <inheritdoc />
    public string? CreateFile(string? workspaceRoot, string? selectedPath, bool selectedIsDirectory, string? name = null)
    {
        var dir = ResolveTargetDirectory(workspaceRoot, selectedPath, selectedIsDirectory);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            _output.Write("Explorer", "New File: brak katalogu docelowego.", OutputSeverity.Warning);
            return null;
        }

        if (name is not null && name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _output.Write("Explorer", $"New File: nazwa '{name}' zawiera niedozwolone znaki.", OutputSeverity.Warning);
            return null;
        }

        var fileName = name ?? FindUniqueName(dir, "NewFile", ".txt", checkDirectories: false);
        var fullPath = Path.Combine(dir, fileName);

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            _output.Write("Explorer", $"New File: '{fileName}' już istnieje.", OutputSeverity.Warning);
            return null;
        }

        try
        {
            File.WriteAllText(fullPath, string.Empty);
            return fullPath;
        }
        catch (Exception ex)
        {
            _output.Write("Explorer", $"New File failed: {ex.Message}", OutputSeverity.Error);
            return null;
        }
    }

    /// <inheritdoc />
    public string? CreateFolder(string? workspaceRoot, string? selectedPath, bool selectedIsDirectory, string? name = null)
    {
        var dir = ResolveTargetDirectory(workspaceRoot, selectedPath, selectedIsDirectory);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            _output.Write("Explorer", "New Folder: brak katalogu docelowego.", OutputSeverity.Warning);
            return null;
        }

        if (name is not null && name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _output.Write("Explorer", $"New Folder: nazwa '{name}' zawiera niedozwolone znaki.", OutputSeverity.Warning);
            return null;
        }

        var folderName = name ?? FindUniqueName(dir, "NewFolder", string.Empty, checkDirectories: true);
        var fullPath = Path.Combine(dir, folderName);

        if (Directory.Exists(fullPath) || File.Exists(fullPath))
        {
            _output.Write("Explorer", $"New Folder: '{folderName}' już istnieje.", OutputSeverity.Warning);
            return null;
        }

        try
        {
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }
        catch (Exception ex)
        {
            _output.Write("Explorer", $"New Folder failed: {ex.Message}", OutputSeverity.Error);
            return null;
        }
    }

    /// <inheritdoc />
    public bool Delete(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _output.Write("Explorer", "Delete: brak wybranego węzła.", OutputSeverity.Warning);
            return false;
        }

        var name = Path.GetFileName(path);

        try
        {
            if (isDirectory)
            {
                if (Directory.EnumerateFileSystemEntries(path).Any())
                {
                    _output.Write("Explorer",
                        $"Delete: katalog '{name}' nie jest pusty — operacja anulowana.",
                        OutputSeverity.Warning);
                    return false;
                }

                Directory.Delete(path);
            }
            else
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex)
        {
            _output.Write("Explorer", $"Delete failed: {ex.Message}", OutputSeverity.Error);
            return false;
        }
    }

    /// <inheritdoc />
    public bool Rename(string path, bool isDirectory, string newName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _output.Write("Explorer", "Rename: brak wybranego węzła.", OutputSeverity.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            _output.Write("Explorer", "Rename: nowa nazwa nie może być pusta.", OutputSeverity.Warning);
            return false;
        }

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _output.Write("Explorer", $"Rename: nazwa '{newName}' zawiera niedozwolone znaki.", OutputSeverity.Warning);
            return false;
        }

        var parentDir = Path.GetDirectoryName(path) ?? string.Empty;
        var newPath = Path.Combine(parentDir, newName);

        if (string.Equals(path, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (isDirectory ? Directory.Exists(newPath) : File.Exists(newPath))
        {
            _output.Write("Explorer", $"Rename: '{newName}' już istnieje.", OutputSeverity.Warning);
            return false;
        }

        try
        {
            if (isDirectory)
            {
                Directory.Move(path, newPath);
            }
            else
            {
                File.Move(path, newPath);
            }

            _eventBus.Publish(new FileRenamedEvent(path, newPath));
            return true;
        }
        catch (Exception ex)
        {
            _output.Write("Explorer", $"Rename failed: {ex.Message}", OutputSeverity.Error);
            return false;
        }
    }

    /// <inheritdoc />
    public bool Move(string path, bool isDirectory, string destinationDir)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _output.Write("Explorer", "Move: brak wybranego węzła.", OutputSeverity.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(destinationDir))
        {
            _output.Write("Explorer", "Move: nie podano katalogu docelowego.", OutputSeverity.Warning);
            return false;
        }

        if (!Directory.Exists(destinationDir))
        {
            _output.Write("Explorer", $"Move: katalog docelowy '{destinationDir}' nie istnieje.", OutputSeverity.Warning);
            return false;
        }

        var name = Path.GetFileName(path);
        var sourceDir = Path.GetDirectoryName(path) ?? string.Empty;

        if (string.Equals(sourceDir, destinationDir, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (isDirectory && IsSubdirectory(path, destinationDir))
        {
            _output.Write("Explorer", "Move: nie można przenieść katalogu do jego własnego podkatalogu.", OutputSeverity.Warning);
            return false;
        }

        var newPath = Path.Combine(destinationDir, name);
        if (isDirectory ? Directory.Exists(newPath) : File.Exists(newPath))
        {
            _output.Write("Explorer", $"Move: '{name}' już istnieje w docelowym katalogu.", OutputSeverity.Warning);
            return false;
        }

        try
        {
            if (isDirectory)
            {
                Directory.Move(path, newPath);
            }
            else
            {
                File.Move(path, newPath);
            }

            _eventBus.Publish(new FileRenamedEvent(path, newPath));
            return true;
        }
        catch (Exception ex)
        {
            _output.Write("Explorer", $"Move failed: {ex.Message}", OutputSeverity.Error);
            return false;
        }
    }

    private static string ResolveTargetDirectory(string? workspaceRoot, string? selectedPath, bool selectedIsDirectory)
    {
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            if (selectedIsDirectory)
            {
                return selectedPath;
            }

            return Path.GetDirectoryName(selectedPath) ?? workspaceRoot ?? string.Empty;
        }

        return workspaceRoot ?? string.Empty;
    }

    private static bool IsSubdirectory(string sourceDir, string destinationDir)
    {
        var source = Path.GetFullPath(sourceDir);
        var destination = Path.GetFullPath(destinationDir);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(source, destination, comparison))
        {
            return false;
        }

        var sourceWithSeparator = source.EndsWith(Path.DirectorySeparatorChar)
            ? source
            : source + Path.DirectorySeparatorChar;

        return destination.StartsWith(sourceWithSeparator, comparison);
    }

    private static string FindUniqueName(string dir, string baseName, string ext, bool checkDirectories)
    {
        var candidate = baseName + ext;
        if (!Exists(dir, candidate, checkDirectories))
        {
            return candidate;
        }

        for (var i = 1; i <= 999; i++)
        {
            candidate = $"{baseName}{i}{ext}";
            if (!Exists(dir, candidate, checkDirectories))
            {
                return candidate;
            }
        }

        return $"{baseName}_{Guid.NewGuid():N}{ext}";
    }

    private static bool Exists(string dir, string candidate, bool checkDirectories)
    {
        var path = Path.Combine(dir, candidate);
        return File.Exists(path) || (checkDirectories && Directory.Exists(path));
    }
}