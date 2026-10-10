namespace Junexis.Studio.Abstractions;

/// <summary>
/// Privacy-bounded evidence describing one completed version-control mutation.
/// </summary>
/// <param name="OperationId">Unique identifier of the operation.</param>
/// <param name="Operation">Stable operation name.</param>
/// <param name="TimestampUtc">UTC completion time.</param>
/// <param name="DurationMilliseconds">Elapsed operation time.</param>
/// <param name="RepositoryName">Display name of the repository.</param>
/// <param name="RepositoryId">Stable, non-reversible identifier derived from the repository path.</param>
/// <param name="BranchBefore">Branch before the mutation.</param>
/// <param name="BranchAfter">Branch after the mutation.</param>
/// <param name="HeadBefore">HEAD before the mutation.</param>
/// <param name="HeadAfter">HEAD after the mutation.</param>
/// <param name="Paths">Bounded repository-relative paths selected by the user.</param>
/// <param name="Target">Sanitized branch, stash, or remote target.</param>
/// <param name="Success">Whether the requested mutation succeeded.</param>
/// <param name="ResultCode">Stable result classification without raw process output.</param>
/// <param name="BuildId">Optional correlated Junexis build.</param>
/// <param name="MentorFindingCount">Optional Mentor finding count for the correlated build.</param>
/// <param name="DiagnosticStoryId">Optional Diagnostic Story identifier for the correlated build.</param>
public sealed record VcsOperationEvidence(
    string OperationId,
    string Operation,
    DateTimeOffset TimestampUtc,
    long DurationMilliseconds,
    string RepositoryName,
    string RepositoryId,
    string? BranchBefore,
    string? BranchAfter,
    string? HeadBefore,
    string? HeadAfter,
    IReadOnlyList<string> Paths,
    string? Target,
    bool Success,
    string ResultCode,
    string? BuildId,
    int? MentorFindingCount,
    string? DiagnosticStoryId)
{
    /// <summary>Compact branch and HEAD transition.</summary>
    public string RepositoryState =>
        $"{BranchBefore ?? "(detached)"}@{HeadBefore ?? "(none)"} → "
        + $"{BranchAfter ?? "(detached)"}@{HeadAfter ?? "(none)"}";

    /// <summary>Compact selected-path summary.</summary>
    public string PathSummary =>
        Paths.Count == 0 ? "paths: none" : $"paths ({Paths.Count}): {string.Join(", ", Paths)}";

    /// <summary>Compact duration summary.</summary>
    public string DurationSummary => $"{DurationMilliseconds} ms";

    /// <summary>Compact deterministic Junexis correlation summary.</summary>
    public string CorrelationSummary
    {
        get
        {
            if (BuildId is null)
                return "Junexis correlation: none";
            var mentor = MentorFindingCount is null ? "Mentor pending" : $"Mentor {MentorFindingCount}";
            var story = DiagnosticStoryId is null ? "Story none" : $"Story {DiagnosticStoryId}";
            return $"build {BuildId}; {mentor}; {story}";
        }
    }
}

/// <summary>Event published after VCS evidence has been recorded.</summary>
/// <param name="Evidence">Recorded evidence.</param>
public sealed record VcsOperationEvidenceRecordedEvent(VcsOperationEvidence Evidence);

/// <summary>Signals a completed deterministic Mentor analysis.</summary>
/// <param name="BuildId">Build analyzed by Mentor.</param>
/// <param name="FindingCount">Number of deterministic findings.</param>
public sealed record MentorAnalysisCompletedEvent(string BuildId, int FindingCount);

/// <summary>Signals a loaded Diagnostic Story associated with a compiler build.</summary>
/// <param name="BuildId">Build used to enrich and resolve the story.</param>
/// <param name="TargetEventId">Narrative target event.</param>
/// <param name="NarrativeId">Optional Narrative identifier.</param>
/// <param name="MentorFindingCount">Mentor findings linked to the story.</param>
public sealed record DiagnosticStoryLoadedEvent(
    string? BuildId,
    ulong TargetEventId,
    string? NarrativeId,
    int MentorFindingCount);

/// <summary>Read-only access to the bounded VCS evidence history.</summary>
public interface IVcsEvidenceStore
{
    /// <summary>Returns newest evidence first.</summary>
    /// <returns>An immutable evidence snapshot.</returns>
    IReadOnlyList<VcsOperationEvidence> GetEntries();
}
