using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Domyślna implementacja <see cref="IFileSystem"/> oparta o dysk lokalny.
/// </summary>
public sealed class DiskFileSystem : IFileSystem
{
    /// <inheritdoc />
    public string ReadFile(string path) => File.ReadAllText(path);

    /// <inheritdoc />
    public void WriteFile(string path, string content) => File.WriteAllText(path, content ?? string.Empty);

    /// <inheritdoc />
    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectory(string path)
        => Directory.EnumerateFileSystemEntries(path);

    /// <inheritdoc />
    public IDisposable Watch(string rootPath, bool includeSubdirectories, Action<FileSystemChange> onChange)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("rootPath cannot be empty.", nameof(rootPath));
        }

        if (onChange is null)
        {
            throw new ArgumentNullException(nameof(onChange));
        }

        var watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        FileSystemEventHandler onChanged = (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.FullPath))
            {
                onChange(new FileSystemChange(FileSystemChangeKind.Changed, e.FullPath));
            }
        };

        FileSystemEventHandler onDeleted = (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.FullPath))
            {
                onChange(new FileSystemChange(FileSystemChangeKind.Deleted, e.FullPath));
            }
        };

        RenamedEventHandler onRenamed = (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.FullPath) && !string.IsNullOrWhiteSpace(e.OldFullPath))
            {
                onChange(new FileSystemChange(FileSystemChangeKind.Renamed, e.FullPath, e.OldFullPath));
            }
        };

        watcher.Changed += onChanged;
        watcher.Deleted += onDeleted;
        watcher.Renamed += onRenamed;

        return new DisposableAction(() =>
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= onChanged;
            watcher.Deleted -= onDeleted;
            watcher.Renamed -= onRenamed;
            watcher.Dispose();
        });
    }

    private sealed class DisposableAction : IDisposable
    {
        private Action? _dispose;

        public DisposableAction(Action dispose)
        {
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        }

        public void Dispose()
        {
            var d = Interlocked.Exchange(ref _dispose, null);
            d?.Invoke();
        }
    }
}
