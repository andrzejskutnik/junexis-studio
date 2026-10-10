using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Represents a single entry in the git commit log.
/// </summary>
/// <param name="Hash">Abbreviated commit hash (7 characters).</param>
/// <param name="Subject">Short commit message (subject line).</param>
public sealed record GitLogEntry(string Hash, string Subject);

/// <summary>Configured Git author identity used for commits.</summary>
/// <param name="Name">Git <c>user.name</c>.</param>
/// <param name="Email">Git <c>user.email</c>.</param>
public sealed record GitAuthor(string? Name, string? Email);

/// <summary>
/// Parses output of <c>git log --oneline</c> into <see cref="GitLogEntry"/> records.
/// </summary>
public static class GitLogParser
{
    /// <summary>
    /// Parses a single <c>git log --oneline</c> line.
    /// Format: <c>{hash} {subject}</c>.
    /// </summary>
    /// <param name="line">Single line from git log --oneline output.</param>
    /// <returns>Parsed entry or <see langword="null"/> if the line is empty/invalid.</returns>
    public static GitLogEntry? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var spaceIdx = line.IndexOf(' ');
        if (spaceIdx <= 0) return null;

        var hash = line[..spaceIdx];
        var subject = line[(spaceIdx + 1)..].TrimEnd();
        return new GitLogEntry(hash, subject);
    }

    /// <summary>
    /// Parses full <c>git log --oneline</c> output (multi-line).
    /// </summary>
    /// <param name="output">Complete output text.</param>
    /// <returns>All parsed log entries.</returns>
    public static IReadOnlyList<GitLogEntry> Parse(string output)
    {
        if (string.IsNullOrEmpty(output)) return Array.Empty<GitLogEntry>();

        var result = new List<GitLogEntry>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = ParseLine(line);
            if (entry is not null) result.Add(entry);
        }
        return result;
    }
}

/// <summary>Result of parsing one NUL-delimited porcelain v2 status stream.</summary>
/// <param name="Files">Changed paths reported by Git.</param>
/// <param name="HeadOid">Current HEAD object ID.</param>
/// <param name="Branch">Current local branch, or <see langword="null"/> for detached HEAD.</param>
/// <param name="IsDetachedHead">Whether HEAD is detached.</param>
/// <param name="Upstream">Configured upstream branch.</param>
/// <param name="Ahead">Number of commits ahead of upstream.</param>
/// <param name="Behind">Number of commits behind upstream.</param>
public sealed record GitStatusResult(
    IReadOnlyList<VcsFileState> Files,
    string? HeadOid,
    string? Branch,
    bool IsDetachedHead,
    string? Upstream,
    int Ahead,
    int Behind);

/// <summary>
/// Parses NUL-delimited <c>git status --porcelain=v2 --branch -z</c> output.
/// The legacy v1 helpers remain available only for extension compatibility.
/// </summary>
public static class GitStatusParser
{
    /// <summary>Parses a complete porcelain v2 status stream.</summary>
    /// <param name="output">Raw status output including NUL separators.</param>
    /// <returns>Branch metadata and file states.</returns>
    public static GitStatusResult ParseV2(string? output)
    {
        if (string.IsNullOrEmpty(output))
            return new GitStatusResult([], null, null, false, null, 0, 0);

        var records = SplitRecords(output);
        var files = new List<VcsFileState>();
        string? oid = null;
        string? branch = null;
        string? upstream = null;
        var detached = false;
        var ahead = 0;
        var behind = 0;

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                oid = record[13..];
                continue;
            }
            if (record.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                var head = record[14..];
                detached = string.Equals(head, "(detached)", StringComparison.Ordinal);
                branch = detached ? null : head;
                continue;
            }
            if (record.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstream = record[18..];
                continue;
            }
            if (record.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                ParseAheadBehind(record[12..], out ahead, out behind);
                continue;
            }

            switch (record.Length == 0 ? '\0' : record[0])
            {
                case '1':
                    ParseOrdinary(record, files);
                    break;
                case '2':
                    var originalPath = index + 1 < records.Count ? records[++index] : null;
                    ParseRenameOrCopy(record, originalPath, files);
                    break;
                case 'u':
                    ParseConflict(record, files);
                    break;
                case '?':
                    if (record.Length >= 3)
                        files.Add(new VcsFileState(
                            record[2..],
                            null,
                            VcsFileStatus.Untracked));
                    break;
            }
        }

        return new GitStatusResult(files, oid, branch, detached, upstream, ahead, behind);
    }

    /// <summary>
    /// Parses a single legacy porcelain v1 line.
    /// </summary>
    /// <param name="line">Line of porcelain v1 output.</param>
    /// <returns>A list of zero to two compatibility changes.</returns>
    public static IReadOnlyList<VcsFileChange> ParseLine(string line)
    {
        if (string.IsNullOrEmpty(line) || line.Length < 4)
            return [];

        var index = line[0];
        var workTree = line[1];
        var path = line[3..].Trim();
        var arrowIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
        if (arrowIndex >= 0)
            path = path[(arrowIndex + 4)..];

        if (IsConflict(index, workTree))
            return [new VcsFileChange(path, VcsFileStatus.Conflict)];
        if (index == '?' && workTree == '?')
            return [new VcsFileChange(path, VcsFileStatus.Untracked)];

        var result = new List<VcsFileChange>(2);
        if (MapStatus(index) is { } staged)
            result.Add(new VcsFileChange(path, staged, true));
        if (MapStatus(workTree) is { } unstaged)
            result.Add(new VcsFileChange(path, unstaged));
        return result;
    }

    /// <summary>Parses complete legacy porcelain v1 output.</summary>
    /// <param name="output">Line-delimited status output.</param>
    /// <returns>Compatibility change entries.</returns>
    public static IReadOnlyList<VcsFileChange> Parse(string? output)
    {
        if (string.IsNullOrEmpty(output))
            return [];
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(ParseLine)
            .ToArray();
    }

    private static IReadOnlyList<string> SplitRecords(string output) =>
        output.Contains('\0')
            ? output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            : output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static void ParseOrdinary(string record, List<VcsFileState> files)
    {
        var fields = record.Split(' ', 9);
        if (fields.Length < 9 || fields[1].Length != 2)
            return;
        AddState(fields[8], fields[1], null, files);
    }

    private static void ParseRenameOrCopy(
        string record,
        string? originalPath,
        List<VcsFileState> files)
    {
        var fields = record.Split(' ', 10);
        if (fields.Length < 10 || fields[1].Length != 2)
            return;
        AddState(fields[9], fields[1], originalPath, files);
    }

    private static void ParseConflict(string record, List<VcsFileState> files)
    {
        var fields = record.Split(' ', 11);
        if (fields.Length >= 11)
            files.Add(new VcsFileState(fields[10], null, VcsFileStatus.Conflict));
    }

    private static void AddState(
        string path,
        string xy,
        string? originalPath,
        List<VcsFileState> files)
    {
        files.Add(new VcsFileState(
            path,
            MapStatus(xy[0]),
            MapStatus(xy[1]))
        {
            OriginalPath = originalPath,
        });
    }

    private static void ParseAheadBehind(string value, out int ahead, out int behind)
    {
        ahead = 0;
        behind = 0;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith('+'))
                int.TryParse(part.AsSpan(1), out ahead);
            else if (part.StartsWith('-'))
                int.TryParse(part.AsSpan(1), out behind);
        }
    }

    private static bool IsConflict(char index, char workTree) =>
        index == 'U'
        || workTree == 'U'
        || (index == 'A' && workTree == 'A')
        || (index == 'D' && workTree == 'D');

    private static VcsFileStatus? MapStatus(char value) => value switch
    {
        'M' or 'T' => VcsFileStatus.Modified,
        'A' => VcsFileStatus.Added,
        'D' => VcsFileStatus.Deleted,
        'R' => VcsFileStatus.Renamed,
        'C' => VcsFileStatus.Copied,
        _ => null,
    };
}

/// <summary>
/// Git CLI provider: implements VCS operations (status/stage/unstage/commit) by invoking
/// the <c>git</c> executable via <see cref="IProcessService"/>. Parses porcelain output.
/// </summary>
public sealed class GitCliProvider
{
    private readonly IProcessService _processes;
    private readonly IOutput? _output;
    private readonly string _workingDirectory;

    /// <summary>
    /// Output channel used for Git operations logging.
    /// </summary>
    public const string Channel = "VCS";

    /// <summary>
    /// Initializes a new <see cref="GitCliProvider"/>.
    /// </summary>
    /// <param name="processes">Process service to run git commands.</param>
    /// <param name="workingDirectory">Working directory (repo root).</param>
    /// <param name="output">Optional output for logging.</param>
    /// <exception cref="ArgumentNullException">When processes or workingDirectory is null.</exception>
    public GitCliProvider(IProcessService processes, string workingDirectory, IOutput? output = null)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        _output = output;
    }

    /// <summary>
    /// Checks if git is available (runs <c>git --version</c>).
    /// </summary>
    /// <returns>True if git is available.</returns>
    public async Task<bool> IsGitAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await RunGitAsync(["--version"], ct).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the working directory is a git repository.
    /// </summary>
    /// <returns>True if inside a git repo.</returns>
    public async Task<bool> IsRepositoryAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await RunGitAsync(
                ["rev-parse", "--is-inside-work-tree"],
                ct).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the current branch name.
    /// </summary>
    /// <returns>Branch name or null if detached/error.</returns>
    public async Task<string?> GetCurrentBranchAsync(CancellationToken ct = default)
    {
        var (result, stdout) = await RunGitWithOutputAsync(
            ["rev-parse", "--abbrev-ref", "HEAD"],
            ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? stdout.Trim() : null;
    }

    /// <summary>Gets branch metadata and file states using porcelain v2.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Parsed repository status.</returns>
    public async Task<GitStatusResult> GetStatusAsync(CancellationToken ct = default)
    {
        var (result, stdout) = await RunGitWithOutputAsync(
            ["status", "--porcelain=v2", "--branch", "-z"],
            ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            _output?.Write(Channel, $"git status failed (exit {result.ExitCode}).", OutputSeverity.Warning);
            return new GitStatusResult([], null, null, false, null, 0, 0);
        }
        return GitStatusParser.ParseV2(stdout);
    }

    /// <summary>Gets compatibility staged and unstaged changes from porcelain v2.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Flattened file changes.</returns>
    public async Task<IReadOnlyList<VcsFileChange>> GetChangesAsync(CancellationToken ct = default)
    {
        var status = await GetStatusAsync(ct).ConfigureAwait(false);
        return status.Files.SelectMany(file =>
        {
            var changes = new List<VcsFileChange>(2);
            if (file.IndexStatus is { } index)
                changes.Add(new VcsFileChange(file.Path, index, true));
            if (file.WorkTreeStatus is { } workTree)
                changes.Add(new VcsFileChange(file.Path, workTree));
            return changes;
        }).ToArray();
    }

    /// <summary>Detects an in-progress merge, rebase, or related Git operation.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The detected operation, or <see cref="VcsRepositoryOperation.None"/>.</returns>
    public async Task<VcsRepositoryOperation> GetRepositoryOperationAsync(
        CancellationToken ct = default)
    {
        var (result, stdout) = await RunGitWithOutputAsync(
            ["rev-parse", "--absolute-git-dir"],
            ct).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return VcsRepositoryOperation.None;

        var gitDirectory = stdout.Trim();
        if (Directory.Exists(Path.Combine(gitDirectory, "rebase-merge"))
            || Directory.Exists(Path.Combine(gitDirectory, "rebase-apply")))
            return VcsRepositoryOperation.Rebase;
        if (File.Exists(Path.Combine(gitDirectory, "MERGE_HEAD")))
            return VcsRepositoryOperation.Merge;
        if (File.Exists(Path.Combine(gitDirectory, "CHERRY_PICK_HEAD")))
            return VcsRepositoryOperation.CherryPick;
        if (File.Exists(Path.Combine(gitDirectory, "REVERT_HEAD")))
            return VcsRepositoryOperation.Revert;
        if (File.Exists(Path.Combine(gitDirectory, "BISECT_LOG")))
            return VcsRepositoryOperation.Bisect;
        return VcsRepositoryOperation.None;
    }

    /// <summary>
    /// Stages a file via <c>git add</c>.
    /// </summary>
    /// <param name="path">File path to stage.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task StageAsync(string path, CancellationToken ct = default)
    {
        var result = await RunGitAsync(["add", "--", path], ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            _output?.Write(Channel, $"git add failed for '{path}' (exit {result.ExitCode}).", OutputSeverity.Error);
        else
            _output?.Write(Channel, $"Staged: {path}.");
    }

    /// <summary>
    /// Unstages a file via <c>git restore --staged</c>.
    /// </summary>
    /// <param name="path">File path to unstage.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UnstageAsync(string path, CancellationToken ct = default)
    {
        var result = await RunGitAsync(
            ["restore", "--staged", "--", path],
            ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            _output?.Write(Channel, $"git restore --staged failed for '{path}' (exit {result.ExitCode}).", OutputSeverity.Error);
        else
            _output?.Write(Channel, $"Unstaged: {path}.");
    }

    /// <summary>
    /// Commits staged files via <c>git commit -m "..."</c>.
    /// </summary>
    /// <param name="message">Commit message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Commit result with the hash on success.</returns>
    public async Task<VcsCommitResult> CommitAsync(string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new VcsCommitResult(false, Error: "Commit message cannot be empty.");

        var (result, stdout, stderr) = await RunGitCaptureAsync(
            ["commit", "-m", message],
            ct).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            _output?.Write(Channel, $"git commit failed (exit {result.ExitCode}).", OutputSeverity.Error);
            var detail = string.IsNullOrWhiteSpace(stderr)
                ? $"git commit exited with code {result.ExitCode}."
                : stderr.Trim();
            return new VcsCommitResult(false, Error: detail);
        }

        // Try to get commit hash from output (first line usually: [branch hash] message)
        var hash = ExtractCommitHash(stdout);
        _output?.Write(Channel, $"Committed: {hash ?? "(unknown)"}.");
        return new VcsCommitResult(true, CommitId: hash);
    }

    /// <summary>Gets the configured local or inherited Git author identity.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Configured name and email; missing values are <see langword="null"/>.</returns>
    public async Task<GitAuthor> GetAuthorAsync(CancellationToken ct = default)
    {
        var nameTask = RunGitWithOutputAsync(["config", "--get", "user.name"], ct);
        var emailTask = RunGitWithOutputAsync(["config", "--get", "user.email"], ct);
        await Task.WhenAll(nameTask, emailTask).ConfigureAwait(false);
        var name = await nameTask.ConfigureAwait(false);
        var email = await emailTask.ConfigureAwait(false);
        return new GitAuthor(
            name.Result.ExitCode == 0 ? NullIfWhiteSpace(name.StdOut) : null,
            email.Result.ExitCode == 0 ? NullIfWhiteSpace(email.StdOut) : null);
    }

    /// <summary>Gets a summary of exactly the changes currently staged in the index.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Human-readable cached diff statistics.</returns>
    public async Task<string> GetCachedDiffStatisticsAsync(CancellationToken ct = default)
    {
        var result = await GetCachedDiffStatisticsResultAsync(ct).ConfigureAwait(false);
        return result.Text;
    }

    internal async Task<(bool Success, string Text)> GetCachedDiffStatisticsResultAsync(
        CancellationToken ct = default)
    {
        var (result, stdout) = await RunGitWithOutputAsync(
            ["diff", "--cached", "--stat"],
            ct).ConfigureAwait(false);
        return (result.ExitCode == 0, result.ExitCode == 0 ? stdout.Trim() : string.Empty);
    }

    /// <summary>Gets the current HEAD object ID.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HEAD object ID, or <see langword="null"/> for an unborn or unavailable HEAD.</returns>
    public async Task<string?> GetHeadAsync(CancellationToken ct = default)
    {
        var (result, stdout) = await RunGitWithOutputAsync(
            ["rev-parse", "--verify", "HEAD"],
            ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? NullIfWhiteSpace(stdout) : null;
    }

    /// <summary>
    /// Gets a unified diff for a file using <c>git diff</c>.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the file.</param>
    /// <param name="target">Which comparison to perform.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="DiffResult"/> with parsed hunks or an error.</returns>
    public async Task<DiffResult> GetDiffAsync(string filePath, DiffTarget target = DiffTarget.WorkingTreeVsHead, CancellationToken ct = default)
    {
        IReadOnlyList<string> args = target switch
        {
            DiffTarget.WorkingTreeVsIndex => ["diff", "--", filePath],
            DiffTarget.IndexVsHead => ["diff", "--cached", "--", filePath],
            _ => ["diff", "HEAD", "--", filePath],
        };

        var (result, stdout) = await RunGitWithOutputAsync(args, ct).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            _output?.Write(Channel, $"git diff failed for '{filePath}' (exit {result.ExitCode}).", OutputSeverity.Error);
            return new DiffResult(filePath, target, Array.Empty<DiffHunk>(), Error: $"git diff exited with {result.ExitCode}.");
        }

        if (string.IsNullOrWhiteSpace(stdout))
            return new DiffResult(filePath, target, Array.Empty<DiffHunk>());

        if (stdout.Contains("Binary files", StringComparison.OrdinalIgnoreCase))
            return new DiffResult(filePath, target, Array.Empty<DiffHunk>(), IsBinary: true);

        var hunks = UnifiedDiffParser.Parse(stdout);
        return new DiffResult(filePath, target, hunks);
    }

    /// <summary>
    /// Gets commit log entries via <c>git log --oneline -n {limit}</c>.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return (default: 20).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Parsed log entries, newest first.</returns>
    public async Task<IReadOnlyList<GitLogEntry>> GetLogAsync(int limit = 20, CancellationToken ct = default)
    {
        if (limit <= 0) return Array.Empty<GitLogEntry>();

        var (result, stdout) = await RunGitWithOutputAsync(
            ["log", "--oneline", "-n", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            _output?.Write(Channel, $"git log failed (exit {result.ExitCode}).", OutputSeverity.Warning);
            return Array.Empty<GitLogEntry>();
        }
        return GitLogParser.Parse(stdout);
    }

    /// <summary>Gets rich commit history with author, parents, and decorations.</summary>
    /// <param name="limit">Maximum number of commits.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Newest commits first.</returns>
    public async Task<IReadOnlyList<GitCommitInfo>> GetHistoryAsync(
        int limit = 100,
        CancellationToken ct = default)
    {
        if (limit <= 0)
            return [];
        var format = "--format=%H%x1f%h%x1f%s%x1f%an%x1f%ae%x1f%aI%x1f%P%x1f%D%x1e";
        var (result, stdout) = await RunGitWithOutputAsync(
            ["log", format, "-n", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return [];
        return ParseHistory(stdout);
    }

    /// <summary>Gets metadata, body, statistics, and changed paths for a commit.</summary>
    /// <param name="commit">Commit object ID or unambiguous ref.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Commit details, or <see langword="null"/> when unavailable.</returns>
    public async Task<GitCommitDetails?> GetCommitDetailsAsync(
        string commit,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);
        var metadataTask = RunGitWithOutputAsync(
            ["show", "-s", "--format=%H%x1f%h%x1f%s%x1f%an%x1f%ae%x1f%aI%x1f%P%x1f%D", commit],
            ct);
        var bodyTask = RunGitWithOutputAsync(["show", "-s", "--format=%B", commit], ct);
        var statisticsTask = RunGitWithOutputAsync(["show", "--format=", "--stat", commit], ct);
        var pathsTask = RunGitWithOutputAsync(
            ["diff-tree", "--root", "--no-commit-id", "--name-status", "-r", "-z", "-M", "-C", commit],
            ct);
        await Task.WhenAll(metadataTask, bodyTask, statisticsTask, pathsTask).ConfigureAwait(false);
        var metadata = await metadataTask.ConfigureAwait(false);
        if (metadata.Result.ExitCode != 0
            || ParseCommitInfo(metadata.StdOut.Trim()) is not { } info)
            return null;
        var body = await bodyTask.ConfigureAwait(false);
        var statistics = await statisticsTask.ConfigureAwait(false);
        var paths = await pathsTask.ConfigureAwait(false);
        return new GitCommitDetails(
            info,
            body.Result.ExitCode == 0 ? body.StdOut.Trim() : string.Empty,
            statistics.Result.ExitCode == 0 ? statistics.StdOut.Trim() : string.Empty,
            paths.Result.ExitCode == 0 ? ParseChangedPaths(paths.StdOut) : []);
    }

    /// <summary>Lists local and remote-tracking branches.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Branches sorted by Git.</returns>
    public async Task<IReadOnlyList<GitBranchInfo>> GetBranchesAsync(CancellationToken ct = default)
    {
        var format = "--format=%(refname:short)\t%(objectname)\t%(upstream:short)\t%(HEAD)\t%(refname)";
        var (result, stdout) = await RunGitWithOutputAsync(
            ["for-each-ref", format, "refs/heads", "refs/remotes"],
            ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return [];
        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseBranch)
            .Where(branch => branch is not null)
            .Cast<GitBranchInfo>()
            .ToArray();
    }

    /// <summary>Creates a local branch after Git validates its name.</summary>
    /// <param name="name">New branch name.</param>
    /// <param name="startPoint">Optional start ref; defaults to HEAD.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public async Task<GitOperationResult> CreateBranchAsync(
        string name,
        string? startPoint = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new(false, "Branch name cannot be empty.");
        var check = await RunGitAsync(["check-ref-format", "--branch", name], ct).ConfigureAwait(false);
        if (check.ExitCode != 0)
            return new(false, $"Invalid branch name: {name}.");
        var args = new List<string> { "branch", "--", name };
        if (!string.IsNullOrWhiteSpace(startPoint))
            args.Add(startPoint);
        return await RunMutationAsync(args, $"Created branch '{name}'.", ct).ConfigureAwait(false);
    }

    /// <summary>Switches to an existing local branch.</summary>
    /// <param name="name">Local branch name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> SwitchBranchAsync(
        string name,
        CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(name)
            ? Task.FromResult(new GitOperationResult(false, "Branch name cannot be empty."))
            : RunMutationAsync(["switch", "--", name], $"Switched to '{name}'.", ct);

    /// <summary>Compares two refs by commit counts and three-dot diff statistics.</summary>
    /// <param name="baseReference">Base ref.</param>
    /// <param name="comparedReference">Compared ref.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Comparison, or <see langword="null"/> when refs are invalid.</returns>
    public async Task<GitBranchComparison?> CompareBranchesAsync(
        string baseReference,
        string comparedReference,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(comparedReference);
        var countsTask = RunGitWithOutputAsync(
            ["rev-list", "--left-right", "--count", $"{baseReference}...{comparedReference}"],
            ct);
        var statTask = RunGitWithOutputAsync(
            ["diff", "--stat", $"{baseReference}...{comparedReference}"],
            ct);
        await Task.WhenAll(countsTask, statTask).ConfigureAwait(false);
        var counts = await countsTask.ConfigureAwait(false);
        var stat = await statTask.ConfigureAwait(false);
        if (counts.Result.ExitCode != 0 || stat.Result.ExitCode != 0)
            return null;
        var values = counts.StdOut.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);
        if (values.Length < 2
            || !int.TryParse(values[0], out var behind)
            || !int.TryParse(values[1], out var ahead))
            return null;
        return new GitBranchComparison(
            baseReference,
            comparedReference,
            ahead,
            behind,
            stat.StdOut.Trim());
    }

    /// <summary>Lists stash entries without reading their patch content.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Newest stash entries first.</returns>
    public async Task<IReadOnlyList<GitStashEntry>> GetStashesAsync(CancellationToken ct = default)
    {
        var (result, stdout) = await RunGitWithOutputAsync(
            ["stash", "list", "--format=%gd%x1f%gs%x1f%cI%x1e"],
            ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return [];
        return stdout.Split('\x1e', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseStash)
            .Where(stash => stash is not null)
            .Cast<GitStashEntry>()
            .ToArray();
    }

    /// <summary>Creates a stash including untracked files.</summary>
    /// <param name="message">User-visible stash message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> CreateStashAsync(
        string message,
        CancellationToken ct = default) =>
        RunMutationAsync(
            ["stash", "push", "-u", "-m",
                string.IsNullOrWhiteSpace(message) ? "Junexis Studio stash" : message.Trim()],
            "Created stash.",
            ct);

    /// <summary>Applies a selected stash while keeping the stash entry.</summary>
    /// <param name="reference">Selected stash reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> ApplyStashAsync(
        string reference,
        CancellationToken ct = default) =>
        RunMutationAsync(["stash", "apply", "--index", reference], $"Applied {reference}.", ct);

    /// <summary>Applies and drops a selected stash.</summary>
    /// <param name="reference">Selected stash reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> PopStashAsync(
        string reference,
        CancellationToken ct = default) =>
        RunMutationAsync(["stash", "pop", "--index", reference], $"Popped {reference}.", ct);

    /// <summary>Lists configured remotes with credential-redacted URLs.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Configured remotes.</returns>
    public async Task<IReadOnlyList<GitRemoteInfo>> GetRemotesAsync(CancellationToken ct = default)
    {
        var (result, stdout) = await RunGitWithOutputAsync(["remote"], ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return [];
        var remotes = new List<GitRemoteInfo>();
        foreach (var name in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fetch = await RunGitWithOutputAsync(["remote", "get-url", name], ct).ConfigureAwait(false);
            var push = await RunGitWithOutputAsync(
                ["remote", "get-url", "--push", name],
                ct).ConfigureAwait(false);
            remotes.Add(new GitRemoteInfo(
                name.Trim(),
                fetch.Result.ExitCode == 0 ? RedactRemoteUrl(fetch.StdOut.Trim()) : null,
                push.Result.ExitCode == 0 ? RedactRemoteUrl(push.StdOut.Trim()) : null));
        }
        return remotes;
    }

    /// <summary>Fetches refs from one explicitly selected remote.</summary>
    /// <param name="remote">Remote name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> FetchAsync(string remote, CancellationToken ct = default) =>
        RunMutationAsync(["fetch", "--prune", "--", remote], $"Fetched '{remote}'.", ct);

    /// <summary>Fast-forward pulls one explicit remote branch.</summary>
    /// <param name="remote">Remote name.</param>
    /// <param name="branch">Remote branch name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> PullAsync(
        string remote,
        string branch,
        CancellationToken ct = default) =>
        RunMutationAsync(
            ["pull", "--ff-only", "--", remote, branch],
            $"Pulled '{remote}/{branch}' with fast-forward only.",
            ct);

    /// <summary>Pushes one explicit local branch to a selected remote.</summary>
    /// <param name="remote">Remote name.</param>
    /// <param name="branch">Local branch name.</param>
    /// <param name="setUpstream">Whether to set upstream explicitly.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    public Task<GitOperationResult> PushAsync(
        string remote,
        string branch,
        bool setUpstream,
        CancellationToken ct = default)
    {
        IReadOnlyList<string> args = setUpstream
            ? ["push", "--set-upstream", "--", remote, branch]
            : ["push", "--", remote, branch];
        return RunMutationAsync(args, $"Pushed '{branch}' to '{remote}'.", ct);
    }

    /// <summary>
    /// Extracts commit hash from git commit output (e.g. "[main abc1234] message").
    /// </summary>
    internal static string? ExtractCommitHash(string output)
    {
        // Pattern: [branch hash] message
        var start = output.IndexOf('[');
        var end = output.IndexOf(']');
        if (start < 0 || end < 0 || end <= start) return null;

        var bracket = output[(start + 1)..end]; // "branch hash"
        var spaceIdx = bracket.LastIndexOf(' ');
        return spaceIdx >= 0 ? bracket[(spaceIdx + 1)..] : null;
    }

    private async Task<ProcessResult> RunGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        var request = CreateGitRequest(arguments);
        return await _processes.RunAsync(request, null, ct).ConfigureAwait(false);
    }

    private async Task<(ProcessResult Result, string StdOut)> RunGitWithOutputAsync(
        IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        var lines = new List<string>();
        void Listener(ProcessOutputLine line)
        {
            if (line.Kind == ProcessOutputKind.StdOut)
                lines.Add(line.Text);
        }

        var request = CreateGitRequest(arguments);
        var result = await _processes.RunAsync(request, Listener, ct).ConfigureAwait(false);
        return (result, string.Join('\n', lines));
    }

    private async Task<(ProcessResult Result, string StdOut, string StdErr)> RunGitCaptureAsync(
        IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        var stdout = new List<string>();
        var stderr = new List<string>();
        void Listener(ProcessOutputLine line)
        {
            if (line.Kind == ProcessOutputKind.StdOut)
                stdout.Add(line.Text);
            else
                stderr.Add(line.Text);
        }

        var result = await _processes.RunAsync(
            CreateGitRequest(arguments),
            Listener,
            ct).ConfigureAwait(false);
        return (result, string.Join('\n', stdout), string.Join('\n', stderr));
    }

    private ProcessRequest CreateGitRequest(IReadOnlyList<string> arguments) =>
        new("git", arguments, _workingDirectory)
        {
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["GCM_INTERACTIVE"] = "Never",
            },
        };

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<GitOperationResult> RunMutationAsync(
        IReadOnlyList<string> arguments,
        string successMessage,
        CancellationToken ct)
    {
        var (result, _, stderr) = await RunGitCaptureAsync(arguments, ct).ConfigureAwait(false);
        if (result.ExitCode == 0)
            return new(true, successMessage);
        var message = string.IsNullOrWhiteSpace(stderr)
            ? $"Git exited with code {result.ExitCode}."
            : stderr.Trim();
        return new(false, message, ContainsConflict(message));
    }

    private static IReadOnlyList<GitCommitInfo> ParseHistory(string output) =>
        output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries)
            .Select(record => ParseCommitInfo(record.Trim()))
            .Where(commit => commit is not null)
            .Cast<GitCommitInfo>()
            .ToArray();

    private static GitCommitInfo? ParseCommitInfo(string record)
    {
        var fields = record.Split('\x1f');
        if (fields.Length < 8
            || !DateTimeOffset.TryParse(
                fields[5],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var authoredAt))
            return null;
        return new GitCommitInfo(
            fields[0],
            fields[1],
            fields[2],
            fields[3],
            fields[4],
            authoredAt,
            fields[6].Split(' ', StringSplitOptions.RemoveEmptyEntries),
            fields[7].Trim());
    }

    private static IReadOnlyList<GitChangedPath> ParseChangedPaths(string output)
    {
        var tokens = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<GitChangedPath>();
        for (var index = 0; index < tokens.Length;)
        {
            var status = tokens[index++].TrimStart('\r', '\n');
            if (index >= tokens.Length)
                break;
            if ((status.StartsWith('R') || status.StartsWith('C'))
                && index + 1 < tokens.Length)
            {
                var original = tokens[index++];
                result.Add(new GitChangedPath(status, tokens[index++], original));
            }
            else
            {
                result.Add(new GitChangedPath(status, tokens[index++]));
            }
        }
        return result;
    }

    private static GitBranchInfo? ParseBranch(string line)
    {
        var fields = line.TrimEnd('\r').Split('\t');
        if (fields.Length < 5)
            return null;
        return new GitBranchInfo(
            fields[0],
            fields[3] == "*",
            fields[4].StartsWith("refs/remotes/", StringComparison.Ordinal),
            fields[1],
            string.IsNullOrWhiteSpace(fields[2]) ? null : fields[2]);
    }

    private static GitStashEntry? ParseStash(string record)
    {
        var fields = record.Trim().Split('\x1f');
        if (fields.Length < 3
            || !DateTimeOffset.TryParse(
                fields[2],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var createdAt))
            return null;
        return new GitStashEntry(fields[0], fields[1], createdAt);
    }

    private static string RedactRemoteUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return value;
        return new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri.ToString();
    }

    private static bool ContainsConflict(string message) =>
        message.Contains("conflict", StringComparison.OrdinalIgnoreCase)
        || message.Contains("CONFLICT", StringComparison.Ordinal);
}

/// <summary>
/// Registers non-blocking Git commands around the workspace-scoped VCS service.
/// </summary>
public sealed class GitProviderExtension : IJunexisExtension
{
    private GitWorkspaceVcsService? _ownedService;
    private GitVcsEvidenceService? _ownedEvidence;
    private GitRepositoryViewModel? _repositoryViewModel;

    /// <inheritdoc />
    public string Id => "junexis.vcs.git";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<GitWorkspaceVcsService>(out var service)
            || service is null)
        {
            GitVcsEvidenceService evidence;
            if (!context.Capabilities.TryGet<GitVcsEvidenceService>(out var configuredEvidence)
                || configuredEvidence is null)
            {
                evidence = new GitVcsEvidenceService(context.EventBus, context.Audit);
                _ownedEvidence = evidence;
                context.Capabilities.Register<GitVcsEvidenceService>(evidence);
                context.Capabilities.Register<IVcsEvidenceStore>(evidence);
            }
            else
            {
                evidence = configuredEvidence;
            }
            service = new GitWorkspaceVcsService(
                context.Workspace,
                context.Processes,
                context.EventBus,
                context.Output,
                evidence);
            _ownedService = service;
            context.Capabilities.Register<GitWorkspaceVcsService>(service);
            context.Capabilities.Register<IVcsService>(service);
        }

        var diffService = new Core.DiffService(context.Output)
        {
            DiffBackend = (path, target, ct) => service.GetDiffAsync(path, target, ct)
        };
        context.Capabilities.Register<IDiffService>(diffService);

        _repositoryViewModel = new GitRepositoryViewModel(service);
        context.Panels.RegisterPanel(
            "junexis.vcs.repository",
            PanelType.Tool,
            () => _repositoryViewModel,
            title: "Repository",
            dockGroup: DockGroup.Left);

        context.Commands.Register(
            "vcs.git.refresh",
            () => _ = ExecuteSafelyAsync(
                context,
                "refresh",
                token => service.RefreshAsync(token)));

        context.Commands.Register(
            id: "vcs.git.diff",
            execute: () => _ = ShowDiffAsync(context, diffService),
            menuPath: null,
            displayName: "Show Diff",
            category: "VCS",
            contextTarget: "editor");

        context.Commands.Register(
            id: "vcs.git.log",
            execute: () => _ = ShowLogAsync(context, service),
            menuPath: null,
            displayName: "Show Log",
            category: "VCS",
            contextTarget: "vcs");

        context.Commands.Register(
            id: "vcs.git.repository",
            execute: () =>
            {
                context.EventBus.Publish(new ActivatePanelRequested("junexis.vcs.repository"));
                _ = _repositoryViewModel.RefreshAsync();
            },
            menuPath: null,
            displayName: "Repository",
            category: "VCS",
            contextTarget: "junexis.vcs.repository");

        if (!string.IsNullOrWhiteSpace(context.Workspace.RootPath))
            _ = ExecuteSafelyAsync(context, "initial refresh", token => service.RefreshAsync(token));
        else
            context.Output.Write("VCS", "No workspace root — Git waits for Workspace → Open Folder.");
        context.Output.Write("VCS", "GitProviderExtension loaded (async workspace backend).");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _repositoryViewModel?.Cancel();
        _repositoryViewModel = null;
        _ownedService?.Dispose();
        _ownedService = null;
        _ownedEvidence?.Dispose();
        _ownedEvidence = null;
    }

    private static async Task ShowDiffAsync(IHostContext context, IDiffService diffService)
    {
        var path = context.ActiveDocument.ActivePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            context.Output.Write("VCS", "Diff: brak aktywnego pliku.");
            return;
        }

        try
        {
            var result = await diffService.GetDiffAsync(path).ConfigureAwait(false);
            if (result.Error is not null)
            {
                context.Output.Write("VCS", $"Diff error: {result.Error}", OutputSeverity.Error);
                return;
            }

            var fileName = Path.GetFileName(path);
            if (result.IsBinary)
            {
                context.Output.Write("VCS", $"[{fileName}] Binary file — diff not available.");
                return;
            }
            if (!result.HasChanges)
            {
                context.Output.Write("VCS", $"[{fileName}] Brak zmian.");
                return;
            }

            context.Output.Write("VCS", $"--- diff: {fileName} ({result.Target}) ---");
            foreach (var hunk in result.Hunks)
            {
                context.Output.Write(
                    "VCS",
                    $"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@");
                foreach (var line in hunk.Lines)
                {
                    var prefix = line.Kind switch
                    {
                        DiffLineKind.Added => "+",
                        DiffLineKind.Removed => "-",
                        _ => " ",
                    };
                    context.Output.Write("VCS", $"{prefix}{line.Content}");
                }
            }
        }
        catch (Exception ex)
        {
            context.Output.Write("VCS", $"Diff failed: {ex.Message}", OutputSeverity.Error);
        }
    }

    private static async Task ShowLogAsync(
        IHostContext context,
        GitWorkspaceVcsService service)
    {
        try
        {
            var log = await service.GetLogAsync().ConfigureAwait(false);
            if (log.Count == 0)
            {
                context.Output.Write("VCS", "git log: brak commitów.");
                return;
            }
            context.Output.Write("VCS", $"--- git log (ostatnie {log.Count}) ---");
            foreach (var entry in log)
                context.Output.Write("VCS", $"{entry.Hash}  {entry.Subject}");
        }
        catch (Exception ex)
        {
            context.Output.Write("VCS", $"Git log failed: {ex.Message}", OutputSeverity.Error);
        }
    }

    private static async Task ExecuteSafelyAsync(
        IHostContext context,
        string operation,
        Func<CancellationToken, Task> action)
    {
        try
        {
            await action(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Output.Write(
                "VCS",
                $"Git {operation} failed: {ex.Message}",
                OutputSeverity.Error);
        }
    }
}
