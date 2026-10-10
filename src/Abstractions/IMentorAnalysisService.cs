namespace Junexis.Studio.Abstractions;

/// <summary>Controls how prominently Mentor presents deterministic analysis.</summary>
public enum MentorPresenceMode
{
    /// <summary>Shows analysis only in the dedicated panel and compact badges.</summary>
    Quiet,

    /// <summary>Shows concise analysis at relevant decision points.</summary>
    Balanced,

    /// <summary>Shows the same verdicts with additional educational context.</summary>
    Teaching
}

/// <summary>Describes whether a Mentor result belongs to the active compiler build.</summary>
public enum MentorAnalysisFreshness
{
    /// <summary>The result does not provide enough identity to establish freshness.</summary>
    Unknown,

    /// <summary>The Mentor and compiler build identifiers match.</summary>
    Current,

    /// <summary>The result belongs to a different compiler build.</summary>
    Stale
}

/// <summary>Describes how a finding changed relative to the previous current analysis.</summary>
public enum MentorFindingChangeKind
{
    /// <summary>The finding did not exist in the previous analysis.</summary>
    Added,

    /// <summary>The same stable finding exists, but its content changed.</summary>
    Changed,

    /// <summary>The finding existed previously and is absent now.</summary>
    Resolved,

    /// <summary>The finding and its content are unchanged.</summary>
    Unchanged
}

/// <summary>A deterministic Mentor finding associated with a stable Junexis symbol.</summary>
/// <param name="Code">Stable Mentor rule code.</param>
/// <param name="Severity">Finding severity.</param>
/// <param name="Message">Human-readable deterministic explanation.</param>
/// <param name="RelatedSymbolId">Optional stable symbol identifier.</param>
/// <param name="Source">Optional source location resolved by Mentor.</param>
/// <param name="Explanation">Optional deterministic rationale for the rule.</param>
/// <param name="RecommendedAction">Optional deterministic next action.</param>
public sealed record MentorFinding(
    string Code,
    string Severity,
    string Message,
    string? RelatedSymbolId,
    AnalysisSourceLocation? Source,
    string? Explanation = null,
    string? RecommendedAction = null);

/// <summary>Compact deterministic summary of a Mentor analysis.</summary>
/// <param name="TaskCount">Number of task nodes.</param>
/// <param name="PipelineCount">Number of pipeline nodes.</param>
/// <param name="NarrativeCount">Number of Narrative nodes.</param>
/// <param name="PlatformCount">Number of distinct task platforms.</param>
/// <param name="FindingCount">Number of findings.</param>
/// <param name="InsightCount">Number of computed insights.</param>
public sealed record MentorAnalysisSummary(
    int TaskCount,
    int PipelineCount,
    int NarrativeCount,
    int PlatformCount,
    int FindingCount,
    int InsightCount);

/// <summary>A deterministic architectural observation computed by Mentor.</summary>
/// <param name="Kind">Stable insight kind.</param>
/// <param name="Summary">Human-readable observation.</param>
/// <param name="Score">Optional numeric score.</param>
/// <param name="RelatedSymbolIds">Stable symbols participating in the observation.</param>
public sealed record MentorInsight(
    string Kind,
    string Summary,
    double? Score,
    IReadOnlyList<string> RelatedSymbolIds);

/// <summary>An architectural pattern recognized by Mentor.</summary>
/// <param name="PatternId">Stable pattern identifier.</param>
/// <param name="Name">Human-readable pattern name.</param>
/// <param name="Confidence">Recognition confidence from zero to one.</param>
/// <param name="Rationale">Deterministic reason for recognizing the pattern.</param>
/// <param name="ParticipantSymbolIds">Stable symbols participating in the pattern.</param>
public sealed record MentorPattern(
    string PatternId,
    string Name,
    double Confidence,
    string Rationale,
    IReadOnlyList<string> ParticipantSymbolIds);

/// <summary>A Semantic Conformance scenario that has not reached its declared target.</summary>
/// <param name="Id">Stable scenario identifier.</param>
/// <param name="AchievedLevel">Highest evidenced semantic level.</param>
/// <param name="TargetLevel">Required semantic level.</param>
public sealed record MentorConformanceGap(string Id, string AchievedLevel, string TargetLevel);

/// <summary>Non-blocking Mentor opinion about a Semantic Conformance report.</summary>
/// <param name="Status">Report status such as satisfied or partial.</param>
/// <param name="TargetLevel">Default target level declared by the report.</param>
/// <param name="ScenarioCount">Number of evaluated scenarios.</param>
/// <param name="TargetSatisfiedCount">Number of scenarios meeting their individual targets.</param>
/// <param name="GapCount">Number of scenarios below target.</param>
/// <param name="Gaps">Stable descriptions of scenarios below target.</param>
/// <param name="Blocking">Always false for M8.9 presentation.</param>
/// <param name="Bound">Whether the report is cryptographically bound to build and bundle identities.</param>
/// <param name="BuildId">Bound compiler Build ID when available.</param>
/// <param name="BundleId">Bound System Bundle ID when available.</param>
/// <param name="BindingSha256">Deterministic identity binding hash when available.</param>
/// <param name="Opinion">Concise deterministic Mentor interpretation.</param>
public sealed record MentorConformanceOpinion(
    string Status,
    string TargetLevel,
    int ScenarioCount,
    int TargetSatisfiedCount,
    int GapCount,
    IReadOnlyList<MentorConformanceGap> Gaps,
    bool Blocking,
    string Opinion,
    bool Bound = false,
    string? BuildId = null,
    string? BundleId = null,
    string? BindingSha256 = null);

/// <summary>Result of Mentor analysis for one compiler build.</summary>
/// <param name="BuildId">Build identifier shared with the analysis snapshot.</param>
/// <param name="SnapshotPath">Snapshot analyzed by Mentor.</param>
/// <param name="Findings">Deterministic findings.</param>
/// <param name="Summary">Optional project summary available in Mentor schema 1.1.</param>
/// <param name="Insights">Computed architectural insights.</param>
/// <param name="Patterns">Recognized architectural patterns.</param>
/// <param name="Conformance">Optional non-blocking Semantic Conformance opinion.</param>
public sealed record MentorAnalysisResult(
    string BuildId,
    string SnapshotPath,
    IReadOnlyList<MentorFinding> Findings,
    MentorAnalysisSummary? Summary = null,
    IReadOnlyList<MentorInsight>? Insights = null,
    IReadOnlyList<MentorPattern>? Patterns = null,
    string SchemaVersion = "1.0",
    MentorConformanceOpinion? Conformance = null);

/// <summary>A finding together with its change relative to the previous current result.</summary>
/// <param name="Kind">Detected change kind.</param>
/// <param name="Current">Current finding, or null when it was resolved.</param>
/// <param name="Previous">Previous finding, or null when it was added.</param>
public sealed record MentorFindingChange(
    MentorFindingChangeKind Kind,
    MentorFinding? Current,
    MentorFinding? Previous);

/// <summary>Deterministic difference between two consecutive current Mentor results.</summary>
/// <param name="Changes">Finding changes ordered by stable identity.</param>
public sealed record MentorFindingDelta(IReadOnlyList<MentorFindingChange> Changes)
{
    /// <summary>Number of newly introduced findings.</summary>
    public int AddedCount => Changes.Count(change => change.Kind == MentorFindingChangeKind.Added);

    /// <summary>Number of findings whose content changed.</summary>
    public int ChangedCount => Changes.Count(change => change.Kind == MentorFindingChangeKind.Changed);

    /// <summary>Number of findings no longer present.</summary>
    public int ResolvedCount => Changes.Count(change => change.Kind == MentorFindingChangeKind.Resolved);

    /// <summary>Number of unchanged findings.</summary>
    public int UnchangedCount => Changes.Count(change => change.Kind == MentorFindingChangeKind.Unchanged);
}

/// <summary>Mentor result enriched with presentation-level trust metadata.</summary>
/// <param name="Result">Versioned deterministic Mentor result.</param>
/// <param name="AnalyzedAtUtc">Time at which Studio completed the analysis.</param>
/// <param name="Freshness">Relationship between the result and active compiler build.</param>
/// <param name="Delta">Difference from the previous current result.</param>
/// <param name="ProjectPolicy">Optional project decisions and suppression evidence applied to the result.</param>
public sealed record MentorAnalysisEnvelope(
    MentorAnalysisResult Result,
    DateTimeOffset AnalyzedAtUtc,
    MentorAnalysisFreshness Freshness,
    MentorFindingDelta Delta,
    MentorProjectPolicyEvaluation? ProjectPolicy = null);

/// <summary>Executes Mentor analysis through a versioned process boundary.</summary>
public interface IMentorAnalysisService
{
    /// <summary>Analyzes a compiler snapshot and returns deterministic findings.</summary>
    /// <param name="snapshotPath">Path to `junexis.analysis.json`.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Mentor analysis result.</returns>
    Task<MentorAnalysisResult> AnalyzeAsync(
        string snapshotPath,
        CancellationToken cancellationToken = default);
}
