namespace Junexis.Studio.Abstractions;

/// <summary>
/// Represents the status of a file in the VCS (version control system).
/// </summary>
public enum VcsFileStatus
{
    /// <summary>File is untracked.</summary>
    Untracked,
    /// <summary>File has been modified.</summary>
    Modified,
    /// <summary>File has been added (new).</summary>
    Added,
    /// <summary>File has been deleted.</summary>
    Deleted,
    /// <summary>File has been renamed.</summary>
    Renamed,
    /// <summary>File has been copied.</summary>
    Copied,
    /// <summary>File has a merge conflict.</summary>
    Conflict
}

/// <summary>
/// Represents a changed file in the VCS working tree or staging area.
/// </summary>
/// <param name="Path">Relative path of the file.</param>
/// <param name="Status">VCS status of the file.</param>
/// <param name="IsStaged">Whether the change is staged for commit.</param>
public sealed record VcsFileChange(string Path, VcsFileStatus Status, bool IsStaged = false);

/// <summary>Represents index and working-tree status for one repository path.</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="IndexStatus">Status staged in the index, or <see langword="null"/>.</param>
/// <param name="WorkTreeStatus">Unstaged working-tree status, or <see langword="null"/>.</param>
public sealed record VcsFileState(
    string Path,
    VcsFileStatus? IndexStatus,
    VcsFileStatus? WorkTreeStatus)
{
    /// <summary>Gets the previous repository-relative path for a rename or copy.</summary>
    public string? OriginalPath { get; init; }
}

/// <summary>Describes an in-progress repository operation detected by Git.</summary>
public enum VcsRepositoryOperation
{
    /// <summary>No repository operation is in progress.</summary>
    None,
    /// <summary>A merge is in progress.</summary>
    Merge,
    /// <summary>A rebase is in progress.</summary>
    Rebase,
    /// <summary>A cherry-pick is in progress.</summary>
    CherryPick,
    /// <summary>A revert is in progress.</summary>
    Revert,
    /// <summary>A bisect operation is in progress.</summary>
    Bisect
}

/// <summary>Immutable repository state shared by VCS consumers.</summary>
/// <param name="RootPath">Repository working directory, when available.</param>
/// <param name="IsRepository">Whether the workspace is a Git repository.</param>
/// <param name="Branch">Current branch or detached HEAD description.</param>
/// <param name="Files">Changed paths with independent index and working-tree states.</param>
public sealed record VcsRepositorySnapshot(
    string? RootPath,
    bool IsRepository,
    string? Branch,
    IReadOnlyList<VcsFileState> Files)
{
    /// <summary>Returns an empty snapshot for a workspace without an active repository.</summary>
    public static VcsRepositorySnapshot Empty { get; } = new(null, false, null, []);

    /// <summary>Gets the current commit object ID when reported by Git.</summary>
    public string? HeadOid { get; init; }

    /// <summary>Indicates that HEAD is detached from a local branch.</summary>
    public bool IsDetachedHead { get; init; }

    /// <summary>Gets the configured upstream branch, when available.</summary>
    public string? Upstream { get; init; }

    /// <summary>Gets the number of commits ahead of the upstream branch.</summary>
    public int Ahead { get; init; }

    /// <summary>Gets the number of commits behind the upstream branch.</summary>
    public int Behind { get; init; }

    /// <summary>Gets the merge, rebase, or other repository operation in progress.</summary>
    public VcsRepositoryOperation Operation { get; init; }

    /// <summary>Projects file states into the compatibility staged/unstaged change list.</summary>
    public IReadOnlyList<VcsFileChange> Changes =>
        Files.SelectMany(file =>
        {
            var changes = new List<VcsFileChange>(2);
            if (file.IndexStatus is { } index)
                changes.Add(new VcsFileChange(file.Path, index, IsStaged: true));
            if (file.WorkTreeStatus is { } workTree)
                changes.Add(new VcsFileChange(file.Path, workTree, IsStaged: false));
            return changes;
        }).ToArray();
}

/// <summary>
/// Result of a VCS commit operation.
/// </summary>
/// <param name="Success">Whether the commit succeeded.</param>
/// <param name="CommitId">The commit hash/ID, if successful.</param>
/// <param name="Error">Error message, if failed.</param>
public sealed record VcsCommitResult(bool Success, string? CommitId = null, string? Error = null)
{
    /// <summary>Indicates that the same request must be repeated with explicit large-change confirmation.</summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>Indicates that cancellation or timeout prevented a definitive commit outcome.</summary>
    public bool IsUncertain { get; init; }
}

/// <summary>Describes the validated staged scope before a commit starts.</summary>
/// <param name="CanCommit">Whether validation allows the commit to start.</param>
/// <param name="Error">Validation error, when the commit cannot start.</param>
/// <param name="StagedFileCount">Number of distinct staged paths.</param>
/// <param name="Statistics">Human-readable output of <c>git diff --cached --stat</c>.</param>
/// <param name="AuthorName">Configured Git author name.</param>
/// <param name="AuthorEmail">Configured Git author email.</param>
/// <param name="RequiresLargeChangeConfirmation">Whether the scope crosses the confirmation threshold.</param>
public sealed record VcsCommitPreflight(
    bool CanCommit,
    string? Error,
    int StagedFileCount,
    string Statistics,
    string? AuthorName,
    string? AuthorEmail,
    bool RequiresLargeChangeConfirmation);

/// <summary>Represents an explicitly authorized commit request.</summary>
/// <param name="Message">Commit message.</param>
/// <param name="ConfirmLargeChange">Whether the user explicitly confirmed a large staged scope.</param>
/// <param name="Timeout">Maximum commit process duration.</param>
public sealed record VcsCommitRequest(
    string Message,
    bool ConfirmLargeChange = false,
    TimeSpan? Timeout = null);

/// <summary>
/// Domain event: a VCS commit was created.
/// </summary>
/// <param name="CommitId">The commit hash/ID.</param>
/// <param name="Message">The commit message.</param>
/// <param name="TimestampUtc">UTC timestamp of the commit.</param>
public sealed record VcsCommitCreatedEvent(string CommitId, string Message, DateTime TimestampUtc);

/// <summary>
/// Domain event: VCS status has changed (files changed, staged, etc.).
/// </summary>
public sealed record VcsStatusChangedEvent;

/// <summary>
/// Domain event: the current VCS branch has changed (or was read on startup).
/// </summary>
/// <param name="BranchName">The active branch name, e.g. "main".</param>
public sealed record VcsBranchChangedEvent(string BranchName);

/// <summary>
/// Minimal contract for a VCS (version control system) service.
/// Supports staging, committing, and status queries without binding to a specific VCS backend.
/// </summary>
public interface IVcsService
{
    /// <summary>Gets the latest immutable repository snapshot without running Git.</summary>
    VcsRepositorySnapshot Snapshot =>
        new(null, true, null, ToFileStates(GetChanges()));

    /// <summary>Reloads repository state from the backend.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Gets the list of changed files (staged and unstaged).
    /// </summary>
    /// <returns>A list of file changes.</returns>
    IReadOnlyList<VcsFileChange> GetChanges();

    /// <summary>
    /// Stages a file for commit.
    /// </summary>
    /// <param name="path">Relative path of the file to stage.</param>
    void Stage(string path);

    /// <summary>Stages a path in the real repository index.</summary>
    /// <param name="path">Repository-relative path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StageAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Stage(path), cancellationToken);

    /// <summary>
    /// Unstages a previously staged file.
    /// </summary>
    /// <param name="path">Relative path of the file to unstage.</param>
    void Unstage(string path);

    /// <summary>Removes a path from the real repository index.</summary>
    /// <param name="path">Repository-relative path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UnstageAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Unstage(path), cancellationToken);

    /// <summary>
    /// Creates a commit from the currently staged files.
    /// </summary>
    /// <param name="message">The commit message. Must not be empty.</param>
    /// <returns>The result of the commit operation.</returns>
    VcsCommitResult Commit(string message);

    /// <summary>Commits exactly the changes already present in the index.</summary>
    /// <param name="message">Commit message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Commit result.</returns>
    Task<VcsCommitResult> CommitAsync(string message, CancellationToken cancellationToken = default) =>
        Task.Run(() => Commit(message), cancellationToken);

    /// <summary>Validates author configuration and the exact staged scope without committing.</summary>
    /// <param name="message">Candidate commit message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Commit readiness and staged diff summary.</returns>
    Task<VcsCommitPreflight> PrepareCommitAsync(
        string message,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new VcsCommitPreflight(
            true,
            null,
            Snapshot.Changes.Count(change => change.IsStaged),
            string.Empty,
            null,
            null,
            false));

    /// <summary>Commits an explicitly validated request.</summary>
    /// <param name="request">Commit message, confirmation, and timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Commit result.</returns>
    Task<VcsCommitResult> CommitAsync(
        VcsCommitRequest request,
        CancellationToken cancellationToken = default) =>
        CommitAsync(request.Message, cancellationToken);

    /// <summary>Gets a parsed diff for one repository path.</summary>
    /// <param name="path">Repository-relative or absolute file path.</param>
    /// <param name="target">Comparison target.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A parsed diff result or an unsupported-operation error.</returns>
    Task<DiffResult> GetDiffAsync(
        string path,
        DiffTarget target = DiffTarget.WorkingTreeVsHead,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DiffResult(
            path,
            target,
            [],
            Error: "Diff is not supported by this VCS provider."));

    private static IReadOnlyList<VcsFileState> ToFileStates(IReadOnlyList<VcsFileChange> changes) =>
        changes.GroupBy(change => change.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => new VcsFileState(
                group.Key,
                group.FirstOrDefault(change => change.IsStaged)?.Status,
                group.FirstOrDefault(change => !change.IsStaged)?.Status))
            .ToArray();
}
