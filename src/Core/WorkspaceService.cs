using Junexis.Studio.Abstractions;
using System.Collections.Concurrent;

namespace Junexis.Studio.Core;

/// <summary>
/// Minimalny workspace oparty o System.IO.
/// </summary>
public sealed class WorkspaceService : IWorkspace, IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly IEventBus? _eventBus;
    private IDisposable? _watchSubscription;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentWrites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan WriteSuppressionWindow = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Aktualny root workspace, jeśli otwarto folder.
    /// </summary>
    public string? RootPath { get; private set; }

    /// <summary>
    /// Zdarzenie wywoływane po odczycie pliku.
    /// </summary>
    public event Action<string>? FileOpened;

    /// <summary>
    /// Zdarzenie wywoływane po ustawieniu <see cref="RootPath"/>.
    /// </summary>
    public event Action<string>? FolderOpened;

    /// <summary>
    /// Zdarzenie wywoływane po zapisie pliku.
    /// </summary>
    public event Action<string>? FileSaved;

    /// <summary>
    /// Tworzy workspace bez integracji z <see cref="IEventBus"/> (np. dla testów).
    /// </summary>
    public WorkspaceService()
        : this(fileSystem: null, eventBus: null)
    {
    }

    /// <summary>
    /// Tworzy workspace z opcjonalną integracją z <see cref="IEventBus"/>.
    /// Jeśli <paramref name="eventBus"/> jest ustawiony, workspace uruchomi watcher plików
    /// po <see cref="OpenFolder"/> i będzie publikował zdarzenia zmian na dysku.
    /// </summary>
    /// <param name="eventBus">Opcjonalny event bus do publikacji eventów watcher'a.</param>
    public WorkspaceService(IEventBus? eventBus)
        : this(fileSystem: null, eventBus: eventBus)
    {
    }

    /// <summary>
    /// Tworzy workspace z podanym <see cref="IFileSystem"/>.
    /// </summary>
    /// <param name="fileSystem">Abstrakcja systemu plików (gdy <see langword="null"/>, używa <see cref="DiskFileSystem"/>).</param>
    /// <param name="eventBus">Opcjonalny event bus do publikacji eventów watcher'a.</param>
    public WorkspaceService(IFileSystem? fileSystem, IEventBus? eventBus)
    {
        _fileSystem = fileSystem ?? new DiskFileSystem();
        _eventBus = eventBus;
    }

    /// <summary>
    /// Otwiera folder roboczy i ustawia <see cref="RootPath"/>.
    /// </summary>
    /// <param name="path">Ścieżka do folderu.</param>
    /// <exception cref="ArgumentException">Gdy <paramref name="path"/> jest puste.</exception>
    /// <exception cref="DirectoryNotFoundException">Gdy folder nie istnieje.</exception>
    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Folder path cannot be empty.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!_fileSystem.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Folder '{fullPath}' does not exist.");
        }

        try
        {
            using var _ = _fileSystem.EnumerateDirectory(fullPath).GetEnumerator();
        }
        catch
        {
            throw new DirectoryNotFoundException($"Folder '{fullPath}' does not exist.");
        }

        RootPath = fullPath;

        FolderOpened?.Invoke(fullPath);

        if (_eventBus is not null)
        {
            _eventBus.Publish(new WorkspaceOpenedEvent(fullPath));
            RestartWatcher(fullPath);
        }
    }

    /// <summary>
    /// Odczytuje zawartość pliku i emituje <see cref="FileOpened"/>.
    /// </summary>
    /// <param name="path">Ścieżka absolutna lub względna do <see cref="RootPath"/>.</param>
    /// <returns>Treść pliku.</returns>
    public string ReadFile(string path)
    {
        var fullPath = ResolvePath(path);
        var content = _fileSystem.ReadFile(fullPath);
        FileOpened?.Invoke(fullPath);
        return content;
    }

    /// <summary>
    /// Zapisuje zawartość do pliku.
    /// </summary>
    /// <param name="path">Ścieżka absolutna lub względna do <see cref="RootPath"/>.</param>
    /// <param name="content">Treść do zapisu.</param>
    public void WriteFile(string path, string content)
    {
        var fullPath = ResolvePath(path);
        _recentWrites[fullPath] = DateTimeOffset.UtcNow;
        _fileSystem.WriteFile(fullPath, content);
        FileSaved?.Invoke(fullPath);
    }

    /// <summary>
    /// Zwraca surową treść <c>junexis.workspace.json</c> lub <see langword="null"/>, jeśli nie istnieje.
    /// </summary>
    /// <returns>Surowa treść konfiguracji lub <see langword="null"/>.</returns>
    public string? GetWorkspaceConfigRaw()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            return null;
        }

        var fullPath = ResolvePath(WorkspaceConfig.FileName);
        if (!_fileSystem.Exists(fullPath))
        {
            return null;
        }

        return _fileSystem.ReadFile(fullPath);
    }

    /// <summary>
    /// Zapisuje surową treść <c>junexis.workspace.json</c> w katalogu <see cref="RootPath"/>.
    /// </summary>
    /// <param name="content">Treść do zapisu.</param>
    public void SetWorkspaceConfigRaw(string content)
    {
        var fullPath = ResolvePath(WorkspaceConfig.FileName);
        _recentWrites[fullPath] = DateTimeOffset.UtcNow;
        _fileSystem.WriteFile(fullPath, content ?? string.Empty);
    }

    /// <summary>
    /// Zwalnia zasoby watcher'a plików, jeśli został uruchomiony.
    /// </summary>
    public void Dispose()
    {
        StopWatcher();
        GC.SuppressFinalize(this);
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("File path cannot be empty.", nameof(path));
        }

        if (!string.IsNullOrWhiteSpace(RootPath))
        {
            var root = Path.GetFullPath(RootPath);

            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(root, path));

            if (!IsWithinRoot(root, fullPath))
            {
                throw new UnauthorizedAccessException($"Path '{fullPath}' is outside workspace root '{root}'.");
            }

            return fullPath;
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        throw new InvalidOperationException("Workspace is not opened. Call OpenFolder first.");
    }

    private static bool IsWithinRoot(string rootPath, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        rootPath = Path.GetFullPath(rootPath);
        fullPath = Path.GetFullPath(fullPath);

        if (string.Equals(fullPath, rootPath, comparison))
        {
            return true;
        }

        var rootWithSeparator = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSeparator, comparison);
    }

    private void RestartWatcher(string rootPath)
    {
        StopWatcher();

        _watchSubscription = _fileSystem.Watch(rootPath, includeSubdirectories: true, OnFileSystemChange);
    }

    private void StopWatcher()
    {
        if (_watchSubscription is null)
        {
            return;
        }

        _watchSubscription.Dispose();
        _watchSubscription = null;
    }

    private void OnFileSystemChange(FileSystemChange change)
    {
        if (_eventBus is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(change.Path))
        {
            return;
        }

        if (IsRecentlyWrittenByIde(change.Path))
        {
            return;
        }

        switch (change.Kind)
        {
            case FileSystemChangeKind.Changed:
                _eventBus.Publish(new FileChangedExternally(change.Path));
                break;
            case FileSystemChangeKind.Deleted:
                _eventBus.Publish(new FileDeletedExternally(change.Path));
                break;
            case FileSystemChangeKind.Renamed:
                if (!string.IsNullOrWhiteSpace(change.OldPath)
                    && !IsRecentlyWrittenByIde(change.OldPath)
                    && !string.Equals(change.OldPath, change.Path, StringComparison.OrdinalIgnoreCase))
                {
                    _eventBus.Publish(new FileRenamedExternally(change.OldPath, change.Path));
                }
                break;
        }
    }

    private bool IsRecentlyWrittenByIde(string fullPath)
    {
        if (!_recentWrites.TryGetValue(fullPath, out var ts))
        {
            return false;
        }

        var age = DateTimeOffset.UtcNow - ts;
        if (age <= WriteSuppressionWindow)
        {
            return true;
        }

        _recentWrites.TryRemove(fullPath, out _);
        return false;
    }
}
