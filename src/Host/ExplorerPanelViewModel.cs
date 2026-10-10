using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using System.ComponentModel;
using System.Linq;

namespace Junexis.Studio.Host;

/// <summary>
/// Element listy ostatnio otwieranych folderów na ekranie powitalnym Explorera.
/// </summary>
/// <param name="DisplayName">Krótka nazwa folderu.</param>
/// <param name="FullPath">Pełna ścieżka folderu.</param>
/// <param name="IsGitRepository">Czy folder zawiera bezpośredni znacznik <c>.git</c>.</param>
public sealed record RecentWorkspaceItem(
    string DisplayName,
    string FullPath,
    bool IsGitRepository);

/// <summary>
/// View model panelu Explorer (v0.1): prezentuje drzewo plików z <see cref="IWorkspace.RootPath"/>
/// i publikuje <see cref="FileOpenRequested"/> po wybraniu pliku.
/// Implementuje <see cref="INotifyPropertyChanged"/> aby umożliwić odświeżanie widoku.
/// </summary>
public sealed class ExplorerPanelViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly IOutput _output;
    private readonly IWorkspaceItemService _workspaceItems;
    private readonly IVersionControlService? _versionControl;
    private readonly IVcsService? _vcs;
    private readonly SynchronizationContext? _synchronizationContext;
    private IReadOnlyList<ExplorerNode> _nodes = Array.Empty<ExplorerNode>();
    private ExplorerNode? _selectedNode;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Tworzy view model Explorera.
    /// </summary>
    /// <param name="workspace">Workspace (root + IO).</param>
    /// <param name="eventBus">EventBus do publikowania zdarzeń domenowych.</param>
    /// <param name="output">Output do logowania problemów.</param>
    /// <param name="versionControl">Opcjonalny serwis VCS.</param>
    /// <param name="workspaceItems">Opcjonalna usługa operacji na elementach workspace.</param>
    /// <param name="vcs">Opcjonalny wspólny, asynchroniczny serwis VCS.</param>
    /// <param name="recentFolders">Opcjonalna lista ostatnio otwieranych folderów.</param>
    public ExplorerPanelViewModel(
        IWorkspace workspace,
        IEventBus eventBus,
        IOutput output,
        IVersionControlService? versionControl,
        IWorkspaceItemService? workspaceItems = null,
        IVcsService? vcs = null,
        IReadOnlyList<string>? recentFolders = null)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _versionControl = versionControl;
        _vcs = vcs;
        _synchronizationContext = SynchronizationContext.Current;
        _workspaceItems = workspaceItems ?? new WorkspaceItemService(eventBus, output);
        RecentWorkspaces = BuildRecentWorkspaces(recentFolders);
        Workspace.FolderOpened += OnFolderOpened;
        if (_vcs is not null)
            _eventBus.Subscribe<VcsStatusChangedEvent>(OnVcsStatusChanged);

        _nodes = BuildNodes(workspace.RootPath);
    }

    /// <summary>
    /// Workspace używany do wyznaczenia root i IO.
    /// </summary>
    public IWorkspace Workspace { get; }

    /// <summary>
    /// Snapshot drzewa plików. Odświeżany przez <see cref="Refresh"/>.
    /// </summary>
    public IReadOnlyList<ExplorerNode> Nodes
    {
        get => _nodes;
        private set
        {
            _nodes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Nodes)));
        }
    }

    /// <summary>
    /// Aktualnie wybrany węzeł w drzewie (ustawiany przez widok).
    /// </summary>
    public ExplorerNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            _selectedNode = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNode)));
        }
    }

    /// <summary>
    /// Odbudowuje drzewo plików z dysku, zachowując stan rozwinięcia węzłów.
    /// </summary>
    public void Refresh()
    {
        var expanded = CollectExpandedPaths(_nodes);
        Nodes = BuildNodes(Workspace.RootPath);
        RestoreExpandedPaths(_nodes, expanded);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasOpenFolder)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmptyWorkspace)));
    }

    /// <summary>
    /// Tworzy nowy plik w katalogu wybranego węzła (lub w katalogu root workspace).
    /// Po utworzeniu odświeża drzewo i publikuje <see cref="FileOpenRequested"/>.
    /// </summary>
    /// <param name="name">Opcjonalna nazwa pliku. Jeśli null, używana jest unikalna nazwa "NewFile.txt".</param>
    /// <returns>Pełna ścieżka do stworzonego pliku lub <see langword="null"/> gdy się nie powiodło.</returns>
    public string? NewFileInSelection(string? name = null)
    {
        var fullPath = _workspaceItems.CreateFile(Workspace.RootPath, _selectedNode?.FullPath, _selectedNode?.IsDirectory ?? false, name);
        if (fullPath is not null)
        {
            Refresh();
            _eventBus.Publish(new FileOpenRequested(fullPath));
        }

        return fullPath;
    }

    /// <summary>
    /// Tworzy nowy katalog w katalogu wybranego węzła (lub w katalogu root workspace).
    /// Po utworzeniu odświeża drzewo.
    /// </summary>
    /// <param name="name">Opcjonalna nazwa katalogu. Jeśli null, używana jest unikalna nazwa "NewFolder".</param>
    /// <returns>Pełna ścieżka do stworzonego katalogu lub <see langword="null"/> gdy się nie powiodło.</returns>
    public string? NewFolderInSelection(string? name = null)
    {
        var fullPath = _workspaceItems.CreateFolder(Workspace.RootPath, _selectedNode?.FullPath, _selectedNode?.IsDirectory ?? false, name);
        if (fullPath is not null)
        {
            Refresh();
        }

        return fullPath;
    }

    /// <summary>
    /// Usuwa zaznaczony węzeł (plik lub pusty katalog).
    /// Po usunięciu odświeża drzewo.
    /// </summary>
    /// <returns><see langword="true"/> gdy usunięto pomyślnie.</returns>
    public bool DeleteSelectedNode()
    {
        if (_selectedNode is null)
        {
            _output.Write("Explorer", "Delete: brak wybranego węzła.", OutputSeverity.Warning);
            return false;
        }

        if (_workspaceItems.Delete(_selectedNode.FullPath, _selectedNode.IsDirectory))
        {
            SelectedNode = null;
            Refresh();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Zmienia nazwę zaznaczonego węzła (pliku lub katalogu).
    /// Po sukcesie odświeża drzewo i publikuje <see cref="FileRenamedEvent"/>.
    /// </summary>
    /// <param name="newName">Nowa nazwa (samo imię, bez ścieżki).</param>
    /// <returns><see langword="true"/> gdy zmiana nazwy się powiodła.</returns>
    public bool RenameSelectedNode(string newName)
    {
        if (_selectedNode is null)
        {
            _output.Write("Explorer", "Rename: brak wybranego węzła.", OutputSeverity.Warning);
            return false;
        }

        if (_workspaceItems.Rename(_selectedNode.FullPath, _selectedNode.IsDirectory, newName))
        {
            SelectedNode = null;
            Refresh();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Przenosi zaznaczony węzeł (plik lub katalog) do wskazanego katalogu docelowego.
    /// Po sukcesie odświeża drzewo i publikuje <see cref="FileRenamedEvent"/>.
    /// </summary>
    /// <param name="destinationDir">Ścieżka katalogu docelowego (musi istnieć).</param>
    /// <returns><see langword="true"/> gdy przeniesienie się powiodło.</returns>
    public bool MoveSelectedNode(string destinationDir)
    {
        if (_selectedNode is null)
        {
            _output.Write("Explorer", "Move: brak wybranego węzła.", OutputSeverity.Warning);
            return false;
        }

        if (_workspaceItems.Move(_selectedNode.FullPath, _selectedNode.IsDirectory, destinationDir))
        {
            SelectedNode = null;
            Refresh();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Prosi o otwarcie pliku reprezentowanego przez węzeł.
    /// </summary>
    /// <param name="node">Węzeł pliku.</param>
    public void RequestOpen(ExplorerNode node)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));
        if (node.IsDirectory) return;
        _eventBus.Publish(new FileOpenRequested(node.FullPath));
    }

    /// <summary>Single-click preview: publishes <see cref="FilePreviewRequested"/> for non-directory nodes.</summary>
    public void RequestPreview(ExplorerNode node)
    {
        if (node is null || node.IsDirectory) return;
        _eventBus.Publish(new FilePreviewRequested(node.FullPath));
    }

    /// <summary>
    /// Informuje, czy użytkownik ma obecnie otwarty folder workspace.
    /// </summary>
    public bool HasOpenFolder =>
        !string.IsNullOrWhiteSpace(Workspace.RootPath) && Directory.Exists(Workspace.RootPath);

    /// <summary>
    /// Informuje, czy Explorer powinien pokazać ekran zachęcający do otwarcia folderu.
    /// </summary>
    public bool IsEmptyWorkspace => !HasOpenFolder;

    /// <summary>
    /// Istniejące ostatnio otwierane foldery prezentowane na ekranie powitalnym.
    /// </summary>
    public IReadOnlyList<RecentWorkspaceItem> RecentWorkspaces { get; }

    /// <summary>
    /// Informuje, czy ekran powitalny ma elementy w sekcji ostatnich folderów.
    /// </summary>
    public bool HasRecentWorkspaces => RecentWorkspaces.Count > 0;

    /// <inheritdoc />
    public void Dispose()
    {
        Workspace.FolderOpened -= OnFolderOpened;
        if (_vcs is not null)
            _eventBus.Unsubscribe<VcsStatusChangedEvent>(OnVcsStatusChanged);
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<ExplorerNode> BuildNodes(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return Array.Empty<ExplorerNode>();
        }

        try
        {
            IReadOnlyDictionary<string, VersionControlFileStatus>? statusMap = null;

            if (_vcs?.Snapshot is { IsRepository: true } snapshot)
            {
                statusMap = BuildStatusMap(snapshot, rootPath);
            }
            else if (_versionControl is not null && _versionControl.IsRepository(rootPath))
            {
                statusMap = BuildStatusMap(_versionControl, rootPath);
            }

            var rootChildren = BuildDirectoryChildren(rootPath, statusMap);
            var rootName = new DirectoryInfo(rootPath).Name;
            return new[] { new ExplorerNode(rootName, rootPath, isDirectory: true, rootChildren, isExpanded: true) };
        }
        catch (Exception ex)
        {
            _output.Write("Explorer", $"Failed to build tree: {ex.Message}");
            return Array.Empty<ExplorerNode>();
        }
    }

    private static HashSet<string> CollectExpandedPaths(IReadOnlyList<ExplorerNode> nodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
            CollectExpandedPathsCore(node, result);
        return result;
    }

    private static void CollectExpandedPathsCore(ExplorerNode node, HashSet<string> result)
    {
        if (node.IsExpanded)
            result.Add(node.FullPath);
        foreach (var child in node.Children)
            CollectExpandedPathsCore(child, result);
    }

    private static void RestoreExpandedPaths(IReadOnlyList<ExplorerNode> nodes, HashSet<string> expanded)
    {
        foreach (var node in nodes)
            RestoreExpandedPathsCore(node, expanded);
    }

    private static void RestoreExpandedPathsCore(ExplorerNode node, HashSet<string> expanded)
    {
        if (expanded.Contains(node.FullPath))
            node.IsExpanded = true;
        foreach (var child in node.Children)
            RestoreExpandedPathsCore(child, expanded);
    }

    private IReadOnlyDictionary<string, VersionControlFileStatus> BuildStatusMap(IVersionControlService versionControl, string rootPath)
    {
        var entries = versionControl.GetRepositoryStatus();
        var map = new Dictionary<string, VersionControlFileStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var full = Path.GetFullPath(Path.Combine(rootPath, entry.Path));
            map[full] = entry.Status;
        }

        return map;
    }

    private static IReadOnlyList<RecentWorkspaceItem> BuildRecentWorkspaces(
        IReadOnlyList<string>? recentFolders)
    {
        return WorkspaceWelcomeState.Create(workspaceRoot: null, recentFolders).RecentWorkspaces;
    }

    private static IReadOnlyDictionary<string, VersionControlFileStatus> BuildStatusMap(
        VcsRepositorySnapshot snapshot,
        string rootPath)
    {
        var map = new Dictionary<string, VersionControlFileStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in snapshot.Files)
        {
            var full = Path.GetFullPath(Path.Combine(rootPath, file.Path));
            map[full] = file.IndexStatus is not null
                ? VersionControlFileStatus.Staged
                : file.WorkTreeStatus == VcsFileStatus.Untracked
                    ? VersionControlFileStatus.Untracked
                    : VersionControlFileStatus.Modified;
        }
        return map;
    }

    private static IReadOnlyList<ExplorerNode> BuildDirectoryChildren(
        string directoryPath,
        IReadOnlyDictionary<string, VersionControlFileStatus>? statusMap)
    {
        var children = new List<ExplorerNode>();

        foreach (var dir in Directory.EnumerateDirectories(directoryPath))
        {
            var name = Path.GetFileName(dir);
            children.Add(new ExplorerNode(name, dir, isDirectory: true, BuildDirectoryChildren(dir, statusMap)));
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath))
        {
            var name = Path.GetFileName(file);
            VersionControlFileStatus? status = null;

            if (statusMap is not null)
            {
                var full = Path.GetFullPath(file);
                status = statusMap.TryGetValue(full, out var s) ? s : VersionControlFileStatus.Committed;
            }

            children.Add(new ExplorerNode(name, file, isDirectory: false, Array.Empty<ExplorerNode>(), status));
        }

        return children;
    }

    private void OnVcsStatusChanged(VcsStatusChangedEvent _)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
            Refresh();
        else
            _synchronizationContext.Post(_ => Refresh(), null);
    }

    private void OnFolderOpened(string _)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
            Refresh();
        else
            _synchronizationContext.Post(_ => Refresh(), null);
    }
}
