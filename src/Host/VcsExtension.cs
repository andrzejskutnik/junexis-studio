using Junexis.Studio.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Junexis.Studio.Host;

/// <summary>
/// View-model for the VCS Changes panel. Exposes staged/unstaged file lists,
/// stage/unstage actions and commit flow backed by <see cref="IVcsService"/>.
/// Implements <see cref="INotifyPropertyChanged"/> for Avalonia data binding.
/// </summary>
public sealed class VcsChangesViewModel : INotifyPropertyChanged
{
    private readonly IVcsService _vcs;
    private readonly IEventBus _eventBus;
    private readonly SynchronizationContext? _synchronizationContext;
    private IReadOnlyList<VcsFileChange> _conflicts = [];
    private IReadOnlyList<VcsFileChange> _changes = [];
    private IReadOnlyList<VcsFileChange> _untracked = [];
    private IReadOnlyList<VcsFileChange> _unstaged = Array.Empty<VcsFileChange>();
    private IReadOnlyList<VcsFileChange> _staged = Array.Empty<VcsFileChange>();
    private string _diffTitle = "Diff preview";
    private string _diffPreview = "Select a changed file and choose a diff.";
    private string _commitMessage = "";
    private bool _isBusy;
    private bool _largeCommitConfirmationPending;
    private string _commitSummary = "Commit preflight has not run.";
    private CancellationTokenSource? _commitCancellation;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Current list of unstaged file changes.
    /// Refreshed by calling <see cref="Refresh"/>.
    /// </summary>
    public IReadOnlyList<VcsFileChange> Unstaged
    {
        get => _unstaged;
        private set { _unstaged = value; OnPropertyChanged(); }
    }

    /// <summary>Gets unresolved merge conflicts.</summary>
    public IReadOnlyList<VcsFileChange> Conflicts
    {
        get => _conflicts;
        private set { _conflicts = value; OnPropertyChanged(); }
    }

    /// <summary>Gets tracked working-tree changes excluding conflicts.</summary>
    public IReadOnlyList<VcsFileChange> Changes
    {
        get => _changes;
        private set { _changes = value; OnPropertyChanged(); }
    }

    /// <summary>Gets untracked working-tree paths.</summary>
    public IReadOnlyList<VcsFileChange> Untracked
    {
        get => _untracked;
        private set { _untracked = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Current list of staged file changes.
    /// Refreshed by calling <see cref="Refresh"/>.
    /// </summary>
    public IReadOnlyList<VcsFileChange> Staged
    {
        get => _staged;
        private set { _staged = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCommit)); }
    }

    /// <summary>
    /// The commit message entered by the user.
    /// Cleared automatically after a successful <see cref="Commit"/>.
    /// </summary>
    public string CommitMessage
    {
        get => _commitMessage;
        set
        {
            _commitMessage = value ?? "";
            _largeCommitConfirmationPending = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCommit));
            OnPropertyChanged(nameof(CommitButtonLabel));
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when a non-empty commit message has been entered
    /// and at least one file is staged.
    /// </summary>
    public bool CanCommit => !IsBusy
        && !string.IsNullOrWhiteSpace(CommitMessage)
        && Staged.Count > 0
        && Conflicts.Count == 0;

    /// <summary>Gets a preflight or commit outcome summary shown above the commit action.</summary>
    public string CommitSummary
    {
        get => _commitSummary;
        private set { _commitSummary = value; OnPropertyChanged(); }
    }

    /// <summary>Gets the commit button label, including explicit large-scope confirmation.</summary>
    public string CommitButtonLabel =>
        _largeCommitConfirmationPending ? "Confirm large commit" : "Commit";

    /// <summary>Indicates that the active commit process can be cancelled.</summary>
    public bool CanCancelCommit => IsBusy;

    /// <summary>Gets the active branch or a workspace status label.</summary>
    public string RepositoryLabel => _vcs.Snapshot.IsRepository
        ? FormatRepositoryLabel(_vcs.Snapshot)
        : string.IsNullOrWhiteSpace(_vcs.Snapshot.RootPath)
            ? "No folder open"
            : "Folder is not a Git repository (.git not found)";

    /// <summary>Gets the caption for the current diff preview.</summary>
    public string DiffTitle
    {
        get => _diffTitle;
        private set { _diffTitle = value; OnPropertyChanged(); }
    }

    /// <summary>Gets the textual diff preview for the selected file.</summary>
    public string DiffPreview
    {
        get => _diffPreview;
        private set { _diffPreview = value; OnPropertyChanged(); }
    }

    /// <summary>Indicates that a commit operation is currently running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
                return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCommit));
            OnPropertyChanged(nameof(CanCancelCommit));
        }
    }

    /// <summary>
    /// Initializes a new <see cref="VcsChangesViewModel"/>.
    /// </summary>
    /// <param name="vcs">VCS service providing change lists and staging operations.</param>
    /// <param name="eventBus">Event bus for subscribing to status changes.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="vcs"/> or <paramref name="eventBus"/> is null.</exception>
    public VcsChangesViewModel(IVcsService vcs, IEventBus eventBus)
    {
        _vcs = vcs ?? throw new ArgumentNullException(nameof(vcs));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _synchronizationContext = SynchronizationContext.Current;

        _eventBus.Subscribe<VcsStatusChangedEvent>(OnStatusChanged);
        Refresh();
    }

    /// <summary>
    /// Refreshes the staged/unstaged file lists from the VCS service.
    /// </summary>
    public void Refresh()
    {
        var all = _vcs.Snapshot.Changes;
        Conflicts = all
            .Where(change => change.Status == VcsFileStatus.Conflict)
            .DistinctBy(change => change.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Staged = all
            .Where(change => change.IsStaged && change.Status != VcsFileStatus.Conflict)
            .ToArray();
        Untracked = all
            .Where(change => !change.IsStaged && change.Status == VcsFileStatus.Untracked)
            .ToArray();
        Changes = all
            .Where(change => !change.IsStaged
                && change.Status is not VcsFileStatus.Untracked
                and not VcsFileStatus.Conflict)
            .ToArray();
        Unstaged = [.. Conflicts, .. Changes, .. Untracked];
        _largeCommitConfirmationPending = false;
        OnPropertyChanged(nameof(CommitButtonLabel));
        OnPropertyChanged(nameof(RepositoryLabel));
    }

    /// <summary>Refreshes the repository snapshot asynchronously.</summary>
    public async Task RefreshAsync()
    {
        await _vcs.RefreshAsync();
        Refresh();
    }

    /// <summary>
    /// Stages a single file.
    /// </summary>
    /// <param name="path">Relative path of the file to stage.</param>
    /// <exception cref="ArgumentException">When <paramref name="path"/> is empty.</exception>
    public void Stage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));
        _vcs.Stage(path);
        Refresh();
    }

    /// <summary>Stages a path in the repository and refreshes the panel.</summary>
    /// <param name="path">Repository-relative path.</param>
    public async Task StageAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _vcs.StageAsync(path);
        Refresh();
    }

    /// <summary>
    /// Unstages a single file.
    /// </summary>
    /// <param name="path">Relative path of the file to unstage.</param>
    /// <exception cref="ArgumentException">When <paramref name="path"/> is empty.</exception>
    public void Unstage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));
        _vcs.Unstage(path);
        Refresh();
    }

    /// <summary>Unstages a path in the repository and refreshes the panel.</summary>
    /// <param name="path">Repository-relative path.</param>
    public async Task UnstageAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _vcs.UnstageAsync(path);
        Refresh();
    }

    /// <summary>
    /// Stages all unstaged files.
    /// </summary>
    public void StageAll()
    {
        foreach (var file in Changes.Concat(Untracked).ToList())
            _vcs.Stage(file.Path);
        Refresh();
    }

    /// <summary>Stages every currently unstaged path through the asynchronous backend.</summary>
    public async Task StageAllAsync()
    {
        foreach (var file in Changes.Concat(Untracked).ToList())
            await _vcs.StageAsync(file.Path);
        Refresh();
    }

    /// <summary>Stages the distinct selected working-tree paths.</summary>
    /// <param name="changes">Selected tracked or untracked changes.</param>
    public async Task StageSelectedAsync(IEnumerable<VcsFileChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var path in changes.Select(change => change.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase))
            await _vcs.StageAsync(path);
        Refresh();
    }

    /// <summary>
    /// Unstages all staged files.
    /// </summary>
    public void UnstageAll()
    {
        foreach (var file in Staged.ToList())
            _vcs.Unstage(file.Path);
        Refresh();
    }

    /// <summary>Unstages every currently staged path through the asynchronous backend.</summary>
    public async Task UnstageAllAsync()
    {
        foreach (var file in Staged.ToList())
            await _vcs.UnstageAsync(file.Path);
        Refresh();
    }

    /// <summary>Unstages the distinct selected index paths.</summary>
    /// <param name="changes">Selected staged changes.</param>
    public async Task UnstageSelectedAsync(IEnumerable<VcsFileChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var path in changes.Select(change => change.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase))
            await _vcs.UnstageAsync(path);
        Refresh();
    }

    /// <summary>
    /// Commits all staged files using the current <see cref="CommitMessage"/>.
    /// On success, <see cref="CommitMessage"/> is cleared and lists are refreshed.
    /// </summary>
    /// <returns>The commit result from the VCS service.</returns>
    public VcsCommitResult Commit()
    {
        var result = _vcs.Commit(CommitMessage);
        if (result.Success)
            CommitMessage = "";
        Refresh();
        return result;
    }

    /// <summary>
    /// Commits staged changes away from the UI thread and prevents a concurrent commit.
    /// </summary>
    /// <returns>The result reported by the VCS backend.</returns>
    public async Task<VcsCommitResult> CommitAsync()
    {
        if (IsBusy)
            return new VcsCommitResult(false, Error: "A VCS operation is already running.");

        IsBusy = true;
        _commitCancellation = new CancellationTokenSource();
        try
        {
            var message = CommitMessage;
            var preflight = await _vcs.PrepareCommitAsync(
                message,
                _commitCancellation.Token);
            CommitSummary = FormatPreflight(preflight);
            if (!preflight.CanCommit)
                return new VcsCommitResult(false, Error: preflight.Error);
            if (preflight.RequiresLargeChangeConfirmation
                && !_largeCommitConfirmationPending)
            {
                _largeCommitConfirmationPending = true;
                OnPropertyChanged(nameof(CommitButtonLabel));
                return new VcsCommitResult(false, Error: "Large commit requires confirmation.")
                {
                    RequiresConfirmation = true,
                };
            }

            var result = await _vcs.CommitAsync(
                new VcsCommitRequest(
                    message,
                    ConfirmLargeChange: _largeCommitConfirmationPending),
                _commitCancellation.Token);
            if (result.Success)
                CommitMessage = "";
            CommitSummary = FormatCommitResult(result);
            Refresh();
            return result;
        }
        catch (OperationCanceledException)
        {
            var result = new VcsCommitResult(false, Error: "Commit was cancelled.")
            {
                IsUncertain = true,
            };
            CommitSummary = FormatCommitResult(result);
            return result;
        }
        finally
        {
            _commitCancellation?.Dispose();
            _commitCancellation = null;
            IsBusy = false;
        }
    }

    /// <summary>Requests cancellation of the active commit process and hooks.</summary>
    public void CancelCommit() => _commitCancellation?.Cancel();

    /// <summary>Loads a working-tree or staged diff into the panel preview.</summary>
    /// <param name="change">Selected file change.</param>
    /// <param name="target">Diff comparison to display.</param>
    public async Task ShowDiffAsync(VcsFileChange change, DiffTarget target)
    {
        ArgumentNullException.ThrowIfNull(change);
        DiffTitle = $"{change.Path} — {target}";
        DiffPreview = "Loading diff…";
        var result = await _vcs.GetDiffAsync(change.Path, target);
        DiffPreview = FormatDiff(result);
    }

    /// <summary>
    /// Unsubscribes from event bus. Called on extension shutdown.
    /// </summary>
    public void Dispose()
    {
        _eventBus.Unsubscribe<VcsStatusChangedEvent>(OnStatusChanged);
    }

    private void OnStatusChanged(VcsStatusChangedEvent _)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
            Refresh();
        else
            _synchronizationContext.Post(_ => Refresh(), null);
    }

    private static string FormatRepositoryLabel(VcsRepositorySnapshot snapshot)
    {
        var branch = snapshot.IsDetachedHead
            ? $"Detached HEAD @ {ShortOid(snapshot.HeadOid)}"
            : snapshot.Branch ?? "Unknown branch";
        var tracking = snapshot.Upstream is null
            ? string.Empty
            : $"  [{snapshot.Upstream}: ↑{snapshot.Ahead} ↓{snapshot.Behind}]";
        var operation = snapshot.Operation == VcsRepositoryOperation.None
            ? string.Empty
            : $"  • {snapshot.Operation} in progress";
        return branch + tracking + operation;
    }

    private static string ShortOid(string? oid) =>
        string.IsNullOrWhiteSpace(oid)
            ? "unknown"
            : oid[..Math.Min(7, oid.Length)];

    private static string FormatDiff(DiffResult result)
    {
        if (result.Error is not null)
            return $"Diff error: {result.Error}";
        if (result.IsBinary)
            return "Binary file — textual diff is unavailable.";
        if (!result.HasChanges)
            return "No changes for this comparison.";

        var lines = new List<string>();
        foreach (var hunk in result.Hunks)
        {
            lines.Add($"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@");
            lines.AddRange(hunk.Lines.Select(line =>
                (line.Kind switch
                {
                    DiffLineKind.Added => "+",
                    DiffLineKind.Removed => "-",
                    _ => " ",
                }) + line.Content));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatPreflight(VcsCommitPreflight preflight)
    {
        if (!preflight.CanCommit)
            return preflight.Error ?? "Commit preflight failed.";
        var author = $"{preflight.AuthorName} <{preflight.AuthorEmail}>";
        var scope = $"{preflight.StagedFileCount} staged file(s)";
        var large = preflight.RequiresLargeChangeConfirmation
            ? " — explicit confirmation required"
            : string.Empty;
        var statistics = string.IsNullOrWhiteSpace(preflight.Statistics)
            ? string.Empty
            : Environment.NewLine + preflight.Statistics;
        return $"{author} • {scope}{large}{statistics}";
    }

    private static string FormatCommitResult(VcsCommitResult result)
    {
        if (result.Success)
        {
            var uncertainty = result.IsUncertain ? " (verified after interruption)" : string.Empty;
            return $"Committed {result.CommitId ?? "(unknown)"}{uncertainty}.";
        }
        var uncertain = result.IsUncertain ? " Outcome requires repository refresh." : string.Empty;
        return (result.Error ?? "Commit failed.") + uncertain;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// VCS extension that registers a Changes panel (staged/unstaged lists)
/// and Stage/Unstage commands. Uses <see cref="IVcsService"/> obtained
/// from <see cref="ICapabilityRegistry"/>.
/// </summary>
public sealed class VcsExtension : IJunexisExtension
{
    private VcsChangesViewModel? _viewModel;

    /// <inheritdoc />
    public string Id => "junexis.vcs";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<IVcsService>(out var vcs) || vcs is null)
        {
            context.Output.Write("VCS", "IVcsService not available — VCS extension inactive.");
            return;
        }

        _viewModel = new VcsChangesViewModel(vcs, context.EventBus);

        // Register the Changes panel beside Debug in the right tool area.
        context.Panels.RegisterPanel(
            "junexis.vcs.changes",
            PanelType.Tool,
            () => _viewModel,
            title: "Changes",
            dockGroup: DockGroup.Right);

        // Register commands
        context.Commands.Register("vcs.stageAll", () => _ = _viewModel.StageAllAsync(),
            menuPath: null, displayName: "Stage All", category: "VCS", contextTarget: "junexis.vcs.changes");
        context.Commands.Register("vcs.unstageAll", () => _ = _viewModel.UnstageAllAsync(),
            menuPath: null, displayName: "Unstage All", category: "VCS", contextTarget: "junexis.vcs.changes");
        context.Commands.Register("vcs.refresh", () => _ = _viewModel.RefreshAsync(),
            menuPath: null, displayName: "Refresh", category: "VCS", contextTarget: "junexis.vcs.changes");

        context.Output.Write("VCS", "VcsExtension loaded — Changes panel registered.");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}
