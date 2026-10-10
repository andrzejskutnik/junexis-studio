using System.ComponentModel;
using System.Runtime.CompilerServices;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// View-model for history, branches, stash, and explicit remote Git operations.
/// </summary>
public sealed class GitRepositoryViewModel : INotifyPropertyChanged
{
    private readonly GitWorkspaceVcsService _service;
    private IReadOnlyList<GitCommitInfo> _history = [];
    private IReadOnlyList<GitBranchInfo> _branches = [];
    private IReadOnlyList<GitStashEntry> _stashes = [];
    private IReadOnlyList<GitRemoteInfo> _remotes = [];
    private IReadOnlyList<VcsOperationEvidence> _evidence = [];
    private GitCommitInfo? _selectedCommit;
    private GitBranchInfo? _selectedBranch;
    private GitBranchInfo? _compareBranch;
    private GitStashEntry? _selectedStash;
    private GitRemoteInfo? _selectedRemote;
    private string _newBranchName = "";
    private string _stashMessage = "";
    private string _remoteBranch = "";
    private string _details = "Select a commit and load details.";
    private string _comparison = "Select two branches to compare.";
    private string _status = "Repository panel ready.";
    private bool _setUpstream;
    private bool _isBusy;
    private CancellationTokenSource? _operationCancellation;

    /// <summary>Creates a repository view-model.</summary>
    /// <param name="service">Workspace-scoped Git service.</param>
    public GitRepositoryViewModel(GitWorkspaceVcsService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets commit history.</summary>
    public IReadOnlyList<GitCommitInfo> History
    {
        get => _history;
        private set => SetField(ref _history, value);
    }

    /// <summary>Gets local and remote-tracking branches.</summary>
    public IReadOnlyList<GitBranchInfo> Branches
    {
        get => _branches;
        private set => SetField(ref _branches, value);
    }

    /// <summary>Gets stash entries.</summary>
    public IReadOnlyList<GitStashEntry> Stashes
    {
        get => _stashes;
        private set => SetField(ref _stashes, value);
    }

    /// <summary>Gets configured remotes.</summary>
    public IReadOnlyList<GitRemoteInfo> Remotes
    {
        get => _remotes;
        private set => SetField(ref _remotes, value);
    }

    /// <summary>Gets privacy-bounded mutation evidence, newest first.</summary>
    public IReadOnlyList<VcsOperationEvidence> Evidence
    {
        get => _evidence;
        private set => SetField(ref _evidence, value);
    }

    /// <summary>Gets or sets the selected commit.</summary>
    public GitCommitInfo? SelectedCommit
    {
        get => _selectedCommit;
        set => SetField(ref _selectedCommit, value);
    }

    /// <summary>Gets or sets the selected branch.</summary>
    public GitBranchInfo? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (SetField(ref _selectedBranch, value) && value is not null)
                RemoteBranch = value.Name;
        }
    }

    /// <summary>Gets or sets the branch compared with the selected branch.</summary>
    public GitBranchInfo? CompareBranch
    {
        get => _compareBranch;
        set => SetField(ref _compareBranch, value);
    }

    /// <summary>Gets or sets the selected stash.</summary>
    public GitStashEntry? SelectedStash
    {
        get => _selectedStash;
        set => SetField(ref _selectedStash, value);
    }

    /// <summary>Gets or sets the selected remote.</summary>
    public GitRemoteInfo? SelectedRemote
    {
        get => _selectedRemote;
        set => SetField(ref _selectedRemote, value);
    }

    /// <summary>Gets or sets the new local branch name.</summary>
    public string NewBranchName
    {
        get => _newBranchName;
        set => SetField(ref _newBranchName, value ?? "");
    }

    /// <summary>Gets or sets the stash message.</summary>
    public string StashMessage
    {
        get => _stashMessage;
        set => SetField(ref _stashMessage, value ?? "");
    }

    /// <summary>Gets or sets the explicit branch used by pull or push.</summary>
    public string RemoteBranch
    {
        get => _remoteBranch;
        set => SetField(ref _remoteBranch, value ?? "");
    }

    /// <summary>Gets or sets whether push configures upstream.</summary>
    public bool SetUpstream
    {
        get => _setUpstream;
        set => SetField(ref _setUpstream, value);
    }

    /// <summary>Gets formatted commit details.</summary>
    public string Details
    {
        get => _details;
        private set => SetField(ref _details, value);
    }

    /// <summary>Gets formatted branch comparison.</summary>
    public string Comparison
    {
        get => _comparison;
        private set => SetField(ref _comparison, value);
    }

    /// <summary>Gets the last operation status.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>Indicates that a repository operation is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                OnPropertyChanged(nameof(CanCancel));
        }
    }

    /// <summary>Indicates that the current operation can be cancelled.</summary>
    public bool CanCancel => IsBusy;

    /// <summary>Reloads history, branches, stash, and remotes.</summary>
    public Task RefreshAsync() => RunAsync(async token =>
    {
        var history = _service.GetHistoryAsync(100, token);
        var branches = _service.GetBranchesAsync(token);
        var stashes = _service.GetStashesAsync(token);
        var remotes = _service.GetRemotesAsync(token);
        await Task.WhenAll(history, branches, stashes, remotes);
        History = await history;
        Branches = await branches;
        Stashes = await stashes;
        Remotes = await remotes;
        Evidence = _service.GetEvidence();
        SelectedBranch ??= Branches.FirstOrDefault(branch => branch.IsCurrent);
        SelectedRemote ??= Remotes.FirstOrDefault();
        Status = $"Loaded {History.Count} commits, {Branches.Count} branches, "
            + $"{Stashes.Count} stashes, {Remotes.Count} remotes.";
    });

    /// <summary>Loads details for the selected commit.</summary>
    public Task LoadCommitDetailsAsync() => RunAsync(async token =>
    {
        if (SelectedCommit is null)
        {
            Status = "Select a commit first.";
            return;
        }
        var details = await _service.GetCommitDetailsAsync(SelectedCommit.Hash, token);
        Details = details is null ? "Commit details are unavailable." : FormatDetails(details);
        Status = details is null ? "Unable to load commit details." : $"Loaded {SelectedCommit.ShortHash}.";
    });

    /// <summary>Creates a local branch at the selected commit or current HEAD.</summary>
    public Task CreateBranchAsync() => RunMutationAsync(token =>
        _service.CreateBranchAsync(NewBranchName, SelectedCommit?.Hash, token));

    /// <summary>Switches to the selected local branch when the worktree is clean.</summary>
    public Task SwitchBranchAsync() => RunMutationAsync(token =>
        SelectedBranch is null || SelectedBranch.IsRemote
            ? Task.FromResult(new GitOperationResult(false, "Select a local branch."))
            : _service.SwitchBranchAsync(SelectedBranch.Name, token));

    /// <summary>Compares the selected branch with the comparison branch.</summary>
    public Task CompareBranchesAsync() => RunAsync(async token =>
    {
        if (SelectedBranch is null || CompareBranch is null)
        {
            Status = "Select a base branch and a comparison branch.";
            return;
        }
        var result = await _service.CompareBranchesAsync(
            SelectedBranch.Name,
            CompareBranch.Name,
            token);
        Comparison = result is null
            ? "Unable to compare the selected refs."
            : $"{result.BaseReference}…{result.ComparedReference}: "
                + $"ahead {result.Ahead}, behind {result.Behind}"
                + (string.IsNullOrWhiteSpace(result.Statistics)
                    ? ""
                    : Environment.NewLine + result.Statistics);
        Status = result is null ? "Branch comparison failed." : "Branch comparison loaded.";
    });

    /// <summary>Creates a stash for tracked and untracked changes.</summary>
    public Task CreateStashAsync() => RunMutationAsync(token =>
        _service.CreateStashAsync(StashMessage, token));

    /// <summary>Applies the selected stash and keeps it.</summary>
    public Task ApplyStashAsync() => RunMutationAsync(token =>
        SelectedStash is null
            ? Task.FromResult(new GitOperationResult(false, "Select a stash."))
            : _service.ApplyStashAsync(SelectedStash.Reference, token));

    /// <summary>Applies and drops the selected stash.</summary>
    public Task PopStashAsync() => RunMutationAsync(token =>
        SelectedStash is null
            ? Task.FromResult(new GitOperationResult(false, "Select a stash."))
            : _service.PopStashAsync(SelectedStash.Reference, token));

    /// <summary>Fetches the selected remote.</summary>
    public Task FetchAsync() => RunMutationAsync(token =>
        SelectedRemote is null
            ? Task.FromResult(new GitOperationResult(false, "Select a remote."))
            : _service.FetchAsync(SelectedRemote.Name, token));

    /// <summary>Fast-forward pulls the explicit branch from the selected remote.</summary>
    public Task PullAsync() => RunMutationAsync(token =>
        SelectedRemote is null || string.IsNullOrWhiteSpace(RemoteBranch)
            ? Task.FromResult(new GitOperationResult(false, "Select a remote and enter a branch."))
            : _service.PullAsync(SelectedRemote.Name, RemoteBranch, token));

    /// <summary>Pushes the explicit branch to the selected remote.</summary>
    public Task PushAsync() => RunMutationAsync(token =>
        SelectedRemote is null || string.IsNullOrWhiteSpace(RemoteBranch)
            ? Task.FromResult(new GitOperationResult(false, "Select a remote and enter a branch."))
            : _service.PushAsync(SelectedRemote.Name, RemoteBranch, SetUpstream, token));

    /// <summary>Cancels the active repository operation.</summary>
    public void Cancel() => _operationCancellation?.Cancel();

    private async Task RunMutationAsync(
        Func<CancellationToken, Task<GitOperationResult>> operation)
    {
        await RunAsync(async token =>
        {
            var result = await operation(token);
            Status = result.Message
                + (result.HasConflicts ? " Conflicts require resolution in Changes." : "");
            await ReloadListsAsync(token);
        });
    }

    private async Task ReloadListsAsync(CancellationToken token)
    {
        History = await _service.GetHistoryAsync(100, token);
        Branches = await _service.GetBranchesAsync(token);
        Stashes = await _service.GetStashesAsync(token);
        Remotes = await _service.GetRemotesAsync(token);
        Evidence = _service.GetEvidence();
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        _operationCancellation = new CancellationTokenSource();
        _operationCancellation.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await operation(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Repository operation cancelled.";
        }
        catch (Exception ex)
        {
            Status = $"Repository operation failed: {ex.Message}";
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private static string FormatDetails(GitCommitDetails details)
    {
        var commit = details.Commit;
        var changed = details.ChangedPaths.Count == 0
            ? "(no changed paths)"
            : string.Join(
                Environment.NewLine,
                details.ChangedPaths.Select(path =>
                {
                    var rename = path.OriginalPath is null
                        ? string.Empty
                        : path.OriginalPath + " → ";
                    return $"{path.Status}  {rename}{path.Path}";
                }));
        return $"{commit.Hash}{Environment.NewLine}"
            + $"{commit.Author} <{commit.AuthorEmail}>  {commit.AuthoredAt:O}{Environment.NewLine}"
            + $"{commit.Subject}{Environment.NewLine}{Environment.NewLine}"
            + $"{details.Body}{Environment.NewLine}{Environment.NewLine}"
            + $"{details.Statistics}{Environment.NewLine}{Environment.NewLine}{changed}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
