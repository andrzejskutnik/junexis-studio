using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Maintains one asynchronous Git snapshot for the active workspace and performs
/// index mutations through <see cref="GitCliProvider"/>.
/// </summary>
public sealed class GitWorkspaceVcsService : IVcsService, IDisposable
{
    /// <summary>Number of staged files that requires explicit commit confirmation.</summary>
    public const int LargeCommitFileThreshold = 50;

    /// <summary>Number of inserted and deleted lines that requires explicit commit confirmation.</summary>
    public const int LargeCommitLineThreshold = 1000;

    /// <summary>Default upper bound for the commit process, including hooks and signing.</summary>
    public static readonly TimeSpan DefaultCommitTimeout = TimeSpan.FromMinutes(2);

    private readonly IWorkspace _workspace;
    private readonly IProcessService _processes;
    private readonly IEventBus _eventBus;
    private readonly IOutput _output;
    private readonly GitVcsEvidenceService? _evidence;
    private readonly Func<string?, bool> _repositoryContextDetector;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _saveDebounce;
    private VcsRepositorySnapshot _snapshot = VcsRepositorySnapshot.Empty;
    private bool _disposed;

    /// <summary>Creates a workspace-scoped Git service.</summary>
    /// <param name="workspace">Workspace whose root selects the repository.</param>
    /// <param name="processes">External process runner.</param>
    /// <param name="eventBus">Event bus for repository state publications.</param>
    /// <param name="output">VCS output channel.</param>
    /// <param name="evidence">Optional shared Git evidence recorder.</param>
    /// <param name="repositoryContextDetector">
    /// Optional detector verifying that the selected workspace folder is a repository root.
    /// </param>
    public GitWorkspaceVcsService(
        IWorkspace workspace,
        IProcessService processes,
        IEventBus eventBus,
        IOutput output,
        GitVcsEvidenceService? evidence = null,
        Func<string?, bool>? repositoryContextDetector = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _evidence = evidence;
        _repositoryContextDetector = repositoryContextDetector ?? GitRepositoryContext.HasGitMarker;
        _workspace.FolderOpened += OnFolderOpened;
        _workspace.FileSaved += OnFileSaved;
    }

    /// <inheritdoc />
    public VcsRepositorySnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <inheritdoc />
    public IReadOnlyList<VcsFileChange> GetChanges() => Snapshot.Changes;

    /// <summary>Gets the newest privacy-bounded Git evidence entries.</summary>
    /// <returns>Newest evidence first.</returns>
    public IReadOnlyList<VcsOperationEvidence> GetEvidence() =>
        _evidence?.GetEntries() ?? [];

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = _workspace.RootPath;
            if (string.IsNullOrWhiteSpace(root))
            {
                Publish(VcsRepositorySnapshot.Empty);
                return;
            }

            if (!_repositoryContextDetector(root))
            {
                Publish(new VcsRepositorySnapshot(root, false, null, []));
                return;
            }

            var provider = new GitCliProvider(_processes, root, _output);
            if (!await provider.IsGitAvailableAsync(cancellationToken).ConfigureAwait(false)
                || !await provider.IsRepositoryAsync(cancellationToken).ConfigureAwait(false))
            {
                Publish(new VcsRepositorySnapshot(root, false, null, []));
                return;
            }

            await RefreshCoreAsync(provider, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operation.Release();
        }
    }

    /// <inheritdoc />
    public async Task StageAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await ExecuteMutationAsync(
            "stage",
            [path],
            null,
            (provider, token) => provider.StageAsync(path, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnstageAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await ExecuteMutationAsync(
            "unstage",
            [path],
            null,
            (provider, token) => provider.UnstageAsync(path, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VcsCommitResult> CommitAsync(
        string message,
        CancellationToken cancellationToken = default) =>
        await CommitAsync(
            new VcsCommitRequest(message),
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<VcsCommitPreflight> PrepareCommitAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var provider = CreateProvider();
            if (provider is null)
                return InvalidPreflight("No Git repository is active.");

            await RefreshCoreAsync(provider, cancellationToken).ConfigureAwait(false);
            return await PrepareCommitCoreAsync(
                provider,
                message,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operation.Release();
        }
    }

    /// <inheritdoc />
    public async Task<VcsCommitResult> CommitAsync(
        VcsCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startedUtc = DateTimeOffset.UtcNow;
        var before = Snapshot;
        IReadOnlyList<string> stagedPaths = [];

        VcsCommitResult Complete(VcsCommitResult result, string resultCode)
        {
            RecordEvidence(
                "commit",
                startedUtc,
                before,
                Snapshot,
                stagedPaths,
                null,
                result.Success,
                resultCode);
            return result;
        }

        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var provider = CreateProvider();
            if (provider is null)
                return Complete(
                    new VcsCommitResult(false, Error: "No Git repository is active."),
                    "rejected");

            await RefreshCoreAsync(provider, cancellationToken).ConfigureAwait(false);
            before = Snapshot;
            stagedPaths = Snapshot.Files
                .Where(file => file.IndexStatus is not null)
                .Select(file => file.Path)
                .ToArray();
            var preflight = await PrepareCommitCoreAsync(
                provider,
                request.Message,
                cancellationToken).ConfigureAwait(false);
            if (!preflight.CanCommit)
                return Complete(new VcsCommitResult(false, Error: preflight.Error), "rejected");
            if (preflight.RequiresLargeChangeConfirmation && !request.ConfirmLargeChange)
            {
                return Complete(new VcsCommitResult(
                    false,
                    Error: "Large staged scope requires explicit confirmation.")
                {
                    RequiresConfirmation = true,
                }, "confirmation-required");
            }

            var headBefore = await provider.GetHeadAsync(cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.Timeout ?? DefaultCommitTimeout);
            VcsCommitResult processResult;
            try
            {
                processResult = await provider.CommitAsync(
                    request.Message,
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var headAfterCancellation = await provider.GetHeadAsync(
                    CancellationToken.None).ConfigureAwait(false);
                await RefreshCoreAsync(provider, CancellationToken.None).ConfigureAwait(false);
                if (HeadChanged(headBefore, headAfterCancellation))
                {
                    PublishCommitCreated(headAfterCancellation!, request.Message);
                    return Complete(new VcsCommitResult(true, headAfterCancellation)
                    {
                        IsUncertain = true,
                    }, "succeeded-uncertain");
                }

                return Complete(new VcsCommitResult(
                    false,
                    Error: cancellationToken.IsCancellationRequested
                        ? "Commit was cancelled; HEAD did not change."
                        : "Commit timed out; HEAD did not change.")
                {
                    IsUncertain = true,
                }, cancellationToken.IsCancellationRequested ? "cancelled" : "timed-out");
            }

            var headAfter = await provider.GetHeadAsync(CancellationToken.None).ConfigureAwait(false);
            await RefreshCoreAsync(provider, CancellationToken.None).ConfigureAwait(false);
            if (!processResult.Success)
                return Complete(processResult, "failed");
            if (!HeadChanged(headBefore, headAfter))
            {
                return Complete(new VcsCommitResult(
                    false,
                    Error: "Git reported success, but the repository HEAD did not change.")
                {
                    IsUncertain = true,
                }, "head-unchanged");
            }

            PublishCommitCreated(headAfter!, request.Message);
            return Complete(new VcsCommitResult(true, headAfter), "succeeded");
        }
        catch (OperationCanceledException)
        {
            Complete(new VcsCommitResult(false, Error: "Commit was cancelled."), "cancelled");
            throw;
        }
        catch
        {
            Complete(new VcsCommitResult(false, Error: "Commit failed."), "failed");
            throw;
        }
        finally
        {
            _operation.Release();
        }
    }

    /// <summary>Gets a diff through the provider bound to the current workspace.</summary>
    /// <param name="path">File path.</param>
    /// <param name="target">Comparison target.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed diff result.</returns>
    public Task<DiffResult> GetDiffAsync(
        string path,
        DiffTarget target = DiffTarget.WorkingTreeVsHead,
        CancellationToken cancellationToken = default)
    {
        var provider = CreateProvider();
        return provider is null
            ? Task.FromResult(new DiffResult(path, target, [], Error: "No Git repository is active."))
            : provider.GetDiffAsync(path, target, cancellationToken);
    }

    /// <summary>Gets recent commits from the active repository.</summary>
    /// <param name="limit">Maximum entry count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recent commit entries.</returns>
    public Task<IReadOnlyList<GitLogEntry>> GetLogAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var provider = CreateProvider();
        return provider is null
            ? Task.FromResult<IReadOnlyList<GitLogEntry>>([])
            : provider.GetLogAsync(limit, cancellationToken);
    }

    /// <summary>Gets rich commit history from the active repository.</summary>
    /// <param name="limit">Maximum number of commits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Newest commits first.</returns>
    public Task<IReadOnlyList<GitCommitInfo>> GetHistoryAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var provider = CreateProvider();
        return provider is null
            ? Task.FromResult<IReadOnlyList<GitCommitInfo>>([])
            : provider.GetHistoryAsync(limit, cancellationToken);
    }

    /// <summary>Gets details for one commit.</summary>
    /// <param name="commit">Commit object ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Commit details, or <see langword="null"/>.</returns>
    public Task<GitCommitDetails?> GetCommitDetailsAsync(
        string commit,
        CancellationToken cancellationToken = default)
    {
        var provider = CreateProvider();
        return provider is null
            ? Task.FromResult<GitCommitDetails?>(null)
            : provider.GetCommitDetailsAsync(commit, cancellationToken);
    }

    /// <summary>Lists local and remote-tracking branches.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Known branches.</returns>
    public Task<IReadOnlyList<GitBranchInfo>> GetBranchesAsync(
        CancellationToken cancellationToken = default)
    {
        var provider = CreateProvider();
        return provider is null
            ? Task.FromResult<IReadOnlyList<GitBranchInfo>>([])
            : provider.GetBranchesAsync(cancellationToken);
    }

    /// <summary>Creates a local branch without switching the worktree.</summary>
    /// <param name="name">New branch name.</param>
    /// <param name="startPoint">Optional start ref.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> CreateBranchAsync(
        string name,
        string? startPoint = null,
        CancellationToken cancellationToken = default) =>
        ExecuteRepositoryMutationAsync(
            "branch.create",
            null,
            name,
            (provider, token) => provider.CreateBranchAsync(name, startPoint, token),
            requireClean: false,
            cancellationToken);

    /// <summary>Switches branches only when the worktree and index are clean.</summary>
    /// <param name="name">Local branch name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> SwitchBranchAsync(
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteRepositoryMutationAsync(
            "branch.switch",
            null,
            name,
            (provider, token) => provider.SwitchBranchAsync(name, token),
            requireClean: true,
            cancellationToken);

    /// <summary>Compares two refs without mutating the repository.</summary>
    /// <param name="baseReference">Base ref.</param>
    /// <param name="comparedReference">Compared ref.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Comparison, or <see langword="null"/>.</returns>
    public Task<GitBranchComparison?> CompareBranchesAsync(
        string baseReference,
        string comparedReference,
        CancellationToken cancellationToken = default)
    {
        var provider = CreateProvider();
        return provider is null
            ? Task.FromResult<GitBranchComparison?>(null)
            : provider.CompareBranchesAsync(baseReference, comparedReference, cancellationToken);
    }

    /// <summary>Lists stash entries.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Newest stash entries first.</returns>
    public Task<IReadOnlyList<GitStashEntry>> GetStashesAsync(
        CancellationToken cancellationToken = default)
    {
        var provider = CreateProvider();
        return provider is null
            ? Task.FromResult<IReadOnlyList<GitStashEntry>>([])
            : provider.GetStashesAsync(cancellationToken);
    }

    /// <summary>Stashes tracked and untracked workspace changes.</summary>
    /// <param name="message">Stash message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> CreateStashAsync(
        string message,
        CancellationToken cancellationToken = default) =>
        ExecuteRepositoryMutationAsync(
            "stash.create",
            Snapshot.Files.Select(file => file.Path),
            null,
            (provider, token) => provider.CreateStashAsync(message, token),
            requireClean: false,
            cancellationToken,
            requireChanges: true);

    /// <summary>Applies a stash only onto a clean worktree.</summary>
    /// <param name="reference">Selected stash reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> ApplyStashAsync(
        string reference,
        CancellationToken cancellationToken = default) =>
        ExecuteRepositoryMutationAsync(
            "stash.apply",
            null,
            reference,
            (provider, token) => provider.ApplyStashAsync(reference, token),
            requireClean: true,
            cancellationToken);

    /// <summary>Pops a stash only onto a clean worktree.</summary>
    /// <param name="reference">Selected stash reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> PopStashAsync(
        string reference,
        CancellationToken cancellationToken = default) =>
        ExecuteRepositoryMutationAsync(
            "stash.pop",
            null,
            reference,
            (provider, token) => provider.PopStashAsync(reference, token),
            requireClean: true,
            cancellationToken);

    /// <summary>Lists configured remotes with redacted URLs.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Configured remotes.</returns>
    public Task<IReadOnlyList<GitRemoteInfo>> GetRemotesAsync(
        CancellationToken cancellationToken = default)
    {
        var provider = CreateProvider();
        return provider is null
            ? Task.FromResult<IReadOnlyList<GitRemoteInfo>>([])
            : provider.GetRemotesAsync(cancellationToken);
    }

    /// <summary>Fetches one explicitly selected remote.</summary>
    /// <param name="remote">Remote name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> FetchAsync(
        string remote,
        CancellationToken cancellationToken = default) =>
        ExecuteRepositoryMutationAsync(
            "fetch",
            null,
            remote,
            (provider, token) => provider.FetchAsync(remote, token),
            requireClean: false,
            cancellationToken);

    /// <summary>Fast-forward pulls an explicit remote branch onto a clean current branch.</summary>
    /// <param name="remote">Remote name.</param>
    /// <param name="branch">Remote branch name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> PullAsync(
        string remote,
        string branch,
        CancellationToken cancellationToken = default) =>
        ExecuteRepositoryMutationAsync(
            "pull",
            null,
            $"{remote}/{branch}",
            (provider, token) => provider.PullAsync(remote, branch, token),
            requireClean: true,
            cancellationToken,
            validation: snapshot =>
                snapshot.IsDetachedHead
                    ? "Pull is disabled for detached HEAD."
                    : !string.Equals(snapshot.Branch, branch, StringComparison.Ordinal)
                        ? $"Pull target '{branch}' must match the current branch '{snapshot.Branch}'."
                        : null);

    /// <summary>Pushes one explicit branch to a selected remote.</summary>
    /// <param name="remote">Remote name.</param>
    /// <param name="branch">Local branch name.</param>
    /// <param name="setUpstream">Whether to configure upstream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> PushAsync(
        string remote,
        string branch,
        bool setUpstream,
        CancellationToken cancellationToken = default) =>
        ExecuteRepositoryMutationAsync(
            "push",
            null,
            $"{remote}/{branch}",
            (provider, token) => provider.PushAsync(remote, branch, setUpstream, token),
            requireClean: false,
            cancellationToken);

    /// <inheritdoc />
    [Obsolete("Use StageAsync so the caller never blocks the UI thread.")]
    public void Stage(string path) =>
        throw new NotSupportedException("Synchronous Git staging is disabled.");

    /// <inheritdoc />
    [Obsolete("Use UnstageAsync so the caller never blocks the UI thread.")]
    public void Unstage(string path) =>
        throw new NotSupportedException("Synchronous Git unstaging is disabled.");

    /// <inheritdoc />
    [Obsolete("Use CommitAsync so the caller never blocks the UI thread.")]
    public VcsCommitResult Commit(string message) =>
        new(false, Error: "Synchronous Git commit is disabled.");

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _workspace.FolderOpened -= OnFolderOpened;
        _workspace.FileSaved -= OnFileSaved;
        _saveDebounce?.Cancel();
        _saveDebounce?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ExecuteMutationAsync(
        string operation,
        IEnumerable<string>? paths,
        string? target,
        Func<GitCliProvider, CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var before = Snapshot;
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var provider = CreateProvider()
                ?? throw new InvalidOperationException("No Git repository is active.");
            await RefreshCoreAsync(provider, cancellationToken).ConfigureAwait(false);
            before = Snapshot;
            await mutation(provider, cancellationToken).ConfigureAwait(false);
            await RefreshCoreAsync(provider, cancellationToken).ConfigureAwait(false);
            RecordEvidence(operation, startedUtc, before, Snapshot, paths, target, true, "succeeded");
        }
        catch (OperationCanceledException)
        {
            RecordEvidence(operation, startedUtc, before, Snapshot, paths, target, false, "cancelled");
            throw;
        }
        catch
        {
            RecordEvidence(operation, startedUtc, before, Snapshot, paths, target, false, "failed");
            throw;
        }
        finally
        {
            _operation.Release();
        }
    }

    private async Task<GitOperationResult> ExecuteRepositoryMutationAsync(
        string operation,
        IEnumerable<string>? paths,
        string? target,
        Func<GitCliProvider, CancellationToken, Task<GitOperationResult>> mutation,
        bool requireClean,
        CancellationToken cancellationToken,
        bool requireChanges = false,
        Func<VcsRepositorySnapshot, string?>? validation = null)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var before = Snapshot;
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var provider = CreateProvider();
            if (provider is null)
                return EvidenceResult(new(false, "No Git repository is active."), "rejected");
            await RefreshCoreAsync(provider, cancellationToken).ConfigureAwait(false);
            before = Snapshot;
            if (Snapshot.Operation != VcsRepositoryOperation.None)
                return EvidenceResult(new(false, $"Finish the active {Snapshot.Operation} operation first."), "rejected");
            if (Snapshot.Files.Any(file =>
                file.IndexStatus == VcsFileStatus.Conflict
                || file.WorkTreeStatus == VcsFileStatus.Conflict))
                return EvidenceResult(new(false, "Resolve repository conflicts first.", HasConflicts: true), "conflict");
            if (requireClean && Snapshot.Files.Count > 0)
                return EvidenceResult(new(false, "This operation requires a clean index and worktree."), "rejected");
            if (requireChanges && Snapshot.Files.Count == 0)
                return EvidenceResult(new(false, "There are no workspace changes to stash."), "rejected");
            if (validation?.Invoke(Snapshot) is { } validationError)
                return EvidenceResult(new(false, validationError), "rejected");

            var result = await mutation(provider, cancellationToken).ConfigureAwait(false);
            await RefreshCoreAsync(provider, CancellationToken.None).ConfigureAwait(false);
            var hasConflicts = Snapshot.Files.Any(file =>
                file.IndexStatus == VcsFileStatus.Conflict
                || file.WorkTreeStatus == VcsFileStatus.Conflict);
            var completed = result with { HasConflicts = result.HasConflicts || hasConflicts };
            return EvidenceResult(
                completed,
                completed.Success ? "succeeded" : completed.HasConflicts ? "conflict" : "failed");
        }
        catch (OperationCanceledException)
        {
            RecordEvidence(operation, startedUtc, before, Snapshot, paths, target, false, "cancelled");
            throw;
        }
        catch
        {
            RecordEvidence(operation, startedUtc, before, Snapshot, paths, target, false, "failed");
            throw;
        }
        finally
        {
            _operation.Release();
        }

        GitOperationResult EvidenceResult(GitOperationResult result, string resultCode)
        {
            RecordEvidence(operation, startedUtc, before, Snapshot, paths, target, result.Success, resultCode);
            return result;
        }
    }

    private async Task<VcsCommitPreflight> PrepareCommitCoreAsync(
        GitCliProvider provider,
        string message,
        CancellationToken cancellationToken)
    {
        if (ValidateCommitMessage(message) is { } messageError)
            return InvalidPreflight(messageError);
        if (Snapshot.Files.Any(file =>
            file.IndexStatus == VcsFileStatus.Conflict
            || file.WorkTreeStatus == VcsFileStatus.Conflict))
            return InvalidPreflight("Resolve all conflicts before committing.");

        var stagedCount = Snapshot.Files.Count(file => file.IndexStatus is not null);
        if (stagedCount == 0)
            return InvalidPreflight("There are no staged changes to commit.");

        var authorTask = provider.GetAuthorAsync(cancellationToken);
        var statisticsTask = provider.GetCachedDiffStatisticsResultAsync(cancellationToken);
        await Task.WhenAll(authorTask, statisticsTask).ConfigureAwait(false);
        var author = await authorTask.ConfigureAwait(false);
        var statisticsResult = await statisticsTask.ConfigureAwait(false);
        var statistics = statisticsResult.Text;
        if (!statisticsResult.Success)
            return InvalidPreflight("Unable to summarize the staged diff.");
        if (string.IsNullOrWhiteSpace(author.Name)
            || string.IsNullOrWhiteSpace(author.Email))
        {
            return new VcsCommitPreflight(
                false,
                "Git author is not configured. Set user.name and user.email.",
                stagedCount,
                statistics,
                author.Name,
                author.Email,
                false);
        }

        var isLarge = stagedCount >= LargeCommitFileThreshold
            || CountChangedLines(statistics) >= LargeCommitLineThreshold;
        return new VcsCommitPreflight(
            true,
            null,
            stagedCount,
            statistics,
            author.Name,
            author.Email,
            isLarge);
    }

    private static string? ValidateCommitMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Commit message cannot be empty.";
        if (message.Contains('\0'))
            return "Commit message cannot contain a NUL character.";
        if (message.Length > 10_000)
            return "Commit message cannot exceed 10,000 characters.";
        var subject = message.Split(['\r', '\n'], 2)[0].Trim();
        if (subject.Length == 0)
            return "Commit subject cannot be empty.";
        if (subject.Length > 72)
            return "Commit subject cannot exceed 72 characters.";
        return null;
    }

    private static VcsCommitPreflight InvalidPreflight(string error) =>
        new(false, error, 0, string.Empty, null, null, false);

    private static int CountChangedLines(string statistics)
    {
        var tokens = statistics.Split(
            [' ', ',', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);
        var total = 0;
        for (var index = 1; index < tokens.Length; index++)
        {
            if ((tokens[index].StartsWith("insertion", StringComparison.OrdinalIgnoreCase)
                    || tokens[index].StartsWith("deletion", StringComparison.OrdinalIgnoreCase))
                && int.TryParse(tokens[index - 1], out var count))
                total += count;
        }
        return total;
    }

    private static bool HeadChanged(string? before, string? after) =>
        !string.IsNullOrWhiteSpace(after)
        && !string.Equals(before, after, StringComparison.Ordinal);

    private void PublishCommitCreated(string commitId, string message) =>
        _eventBus.Publish(new VcsCommitCreatedEvent(commitId, message, DateTime.UtcNow));

    private void RecordEvidence(
        string operation,
        DateTimeOffset startedUtc,
        VcsRepositorySnapshot before,
        VcsRepositorySnapshot after,
        IEnumerable<string>? paths,
        string? target,
        bool success,
        string resultCode)
    {
        try
        {
            _evidence?.Record(
                operation,
                startedUtc,
                _workspace.RootPath,
                before,
                after,
                paths,
                target,
                success,
                resultCode);
        }
        catch (Exception ex)
        {
            _output.Write(
                "VCS",
                $"Evidence recording failed for '{operation}': {ex.GetType().Name}.",
                OutputSeverity.Warning);
        }
    }

    private async Task RefreshCoreAsync(
        GitCliProvider provider,
        CancellationToken cancellationToken)
    {
        var root = _workspace.RootPath;
        var statusTask = provider.GetStatusAsync(cancellationToken);
        var operationTask = provider.GetRepositoryOperationAsync(cancellationToken);
        await Task.WhenAll(statusTask, operationTask).ConfigureAwait(false);
        var status = await statusTask.ConfigureAwait(false);
        Publish(new VcsRepositorySnapshot(root, true, status.Branch, status.Files)
        {
            HeadOid = status.HeadOid,
            IsDetachedHead = status.IsDetachedHead,
            Upstream = status.Upstream,
            Ahead = status.Ahead,
            Behind = status.Behind,
            Operation = await operationTask.ConfigureAwait(false),
        });
    }

    private GitCliProvider? CreateProvider()
    {
        var root = _workspace.RootPath;
        return string.IsNullOrWhiteSpace(root) || !_repositoryContextDetector(root)
            ? null
            : new GitCliProvider(_processes, root, _output);
    }

    private void Publish(VcsRepositorySnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
        _eventBus.Publish(new VcsStatusChangedEvent());
        if (!string.IsNullOrWhiteSpace(snapshot.Branch))
            _eventBus.Publish(new VcsBranchChangedEvent(snapshot.Branch));
    }

    private void OnFolderOpened(string path) => _ = RefreshSafelyAsync(TimeSpan.Zero, _lifetime.Token);

    private void OnFileSaved(string path)
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var previous = Interlocked.Exchange(ref _saveDebounce, next);
        previous?.Cancel();
        previous?.Dispose();
        _ = RefreshSafelyAsync(TimeSpan.FromMilliseconds(250), next.Token);
    }

    private async Task RefreshSafelyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _output.Write("VCS", $"Git refresh failed: {ex.Message}", OutputSeverity.Warning);
        }
    }
}
