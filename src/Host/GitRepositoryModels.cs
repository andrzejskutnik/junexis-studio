namespace Junexis.Studio.Host;

/// <summary>Represents one commit in repository history.</summary>
/// <param name="Hash">Full commit object ID.</param>
/// <param name="ShortHash">Abbreviated object ID.</param>
/// <param name="Subject">Commit subject.</param>
/// <param name="Author">Author display name.</param>
/// <param name="AuthorEmail">Author email.</param>
/// <param name="AuthoredAt">Author timestamp.</param>
/// <param name="Parents">Parent object IDs.</param>
/// <param name="References">Decorated branch and tag references.</param>
public sealed record GitCommitInfo(
    string Hash,
    string ShortHash,
    string Subject,
    string Author,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    IReadOnlyList<string> Parents,
    string References);

/// <summary>Represents one path changed by a commit.</summary>
/// <param name="Status">Git name-status code.</param>
/// <param name="Path">Current repository-relative path.</param>
/// <param name="OriginalPath">Previous path for rename or copy.</param>
public sealed record GitChangedPath(string Status, string Path, string? OriginalPath = null);

/// <summary>Contains commit metadata, body, statistics, and changed paths.</summary>
/// <param name="Commit">Commit metadata.</param>
/// <param name="Body">Commit message body.</param>
/// <param name="Statistics">Diff statistics.</param>
/// <param name="ChangedPaths">Paths changed by the commit.</param>
public sealed record GitCommitDetails(
    GitCommitInfo Commit,
    string Body,
    string Statistics,
    IReadOnlyList<GitChangedPath> ChangedPaths);

/// <summary>Represents a local or remote-tracking branch.</summary>
/// <param name="Name">Short branch name.</param>
/// <param name="IsCurrent">Whether this is the checked-out branch.</param>
/// <param name="IsRemote">Whether this is a remote-tracking branch.</param>
/// <param name="HeadOid">Branch tip object ID.</param>
/// <param name="Upstream">Configured upstream branch.</param>
public sealed record GitBranchInfo(
    string Name,
    bool IsCurrent,
    bool IsRemote,
    string HeadOid,
    string? Upstream);

/// <summary>Describes ahead/behind counts and diff statistics between two refs.</summary>
/// <param name="BaseReference">Base branch or ref.</param>
/// <param name="ComparedReference">Compared branch or ref.</param>
/// <param name="Ahead">Commits unique to the compared reference.</param>
/// <param name="Behind">Commits unique to the base reference.</param>
/// <param name="Statistics">Three-dot diff statistics.</param>
public sealed record GitBranchComparison(
    string BaseReference,
    string ComparedReference,
    int Ahead,
    int Behind,
    string Statistics);

/// <summary>Represents one stash entry.</summary>
/// <param name="Reference">Stable stash reference such as <c>stash@{0}</c>.</param>
/// <param name="Message">Stash subject.</param>
/// <param name="CreatedAt">Stash creation timestamp.</param>
public sealed record GitStashEntry(
    string Reference,
    string Message,
    DateTimeOffset CreatedAt);

/// <summary>Represents one configured Git remote and its URLs.</summary>
/// <param name="Name">Remote name.</param>
/// <param name="FetchUrl">Fetch URL.</param>
/// <param name="PushUrl">Push URL.</param>
public sealed record GitRemoteInfo(string Name, string? FetchUrl, string? PushUrl);

/// <summary>Reports the result of a repository mutation.</summary>
/// <param name="Success">Whether Git completed successfully.</param>
/// <param name="Message">User-facing result or error.</param>
/// <param name="HasConflicts">Whether the resulting repository contains conflicts.</param>
public sealed record GitOperationResult(bool Success, string Message, bool HasConflicts = false);
