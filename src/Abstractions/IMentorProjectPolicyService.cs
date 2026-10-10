namespace Junexis.Studio.Abstractions;

/// <summary>An explicit architectural decision recorded in the project Mentor policy.</summary>
/// <param name="Id">Stable project-local decision identifier.</param>
/// <param name="Title">Concise decision title.</param>
/// <param name="Rationale">Required explanation of why the decision was made.</param>
/// <param name="RelatedSymbolIds">Optional stable compiler symbols governed by the decision.</param>
public sealed record MentorProjectDecision(
    string Id,
    string Title,
    string Rationale,
    IReadOnlyList<string> RelatedSymbolIds);

/// <summary>A narrow, justified suppression recorded in the project Mentor policy.</summary>
/// <param name="Id">Stable project-local suppression identifier.</param>
/// <param name="RuleCode">Exact Mentor rule code.</param>
/// <param name="SymbolId">Exact stable compiler symbol identifier.</param>
/// <param name="Rationale">Required explanation of why the finding is accepted.</param>
/// <param name="ExpiresOn">Optional inclusive expiration date.</param>
public sealed record MentorProjectSuppression(
    string Id,
    string RuleCode,
    string SymbolId,
    string Rationale,
    DateOnly? ExpiresOn);

/// <summary>Validated contents of <c>.junexis/mentor.yml</c>.</summary>
/// <param name="SchemaVersion">Policy schema version.</param>
/// <param name="Presence">Optional project-level Mentor presence override.</param>
/// <param name="Decisions">Explicit architectural decisions.</param>
/// <param name="Suppressions">Narrow finding suppressions.</param>
public sealed record MentorProjectPolicy(
    string SchemaVersion,
    MentorPresenceMode? Presence,
    IReadOnlyList<MentorProjectDecision> Decisions,
    IReadOnlyList<MentorProjectSuppression> Suppressions);

/// <summary>Evidence that one configured suppression matched one concrete finding.</summary>
/// <param name="Suppression">Validated suppression that was applied.</param>
/// <param name="Finding">Concrete finding hidden from the active result.</param>
public sealed record MentorAppliedSuppression(
    MentorProjectSuppression Suppression,
    MentorFinding Finding);

/// <summary>Result of applying a project policy to one deterministic Mentor result.</summary>
/// <param name="Result">Mentor result containing only visible findings.</param>
/// <param name="Policy">Validated policy, or an empty policy when absent or invalid.</param>
/// <param name="PolicyPath">Expected absolute policy path.</param>
/// <param name="IsConfigured">Whether the policy file exists.</param>
/// <param name="IsValid">Whether the complete configured policy passed validation.</param>
/// <param name="AppliedSuppressions">Exact suppressions applied to this result.</param>
/// <param name="ValidationErrors">Deterministic validation errors; no suppression is applied when non-empty.</param>
public sealed record MentorProjectPolicyEvaluation(
    MentorAnalysisResult Result,
    MentorProjectPolicy Policy,
    string PolicyPath,
    bool IsConfigured,
    bool IsValid,
    IReadOnlyList<MentorAppliedSuppression> AppliedSuppressions,
    IReadOnlyList<string> ValidationErrors);

/// <summary>Loads, validates, and applies the project-owned Mentor policy.</summary>
public interface IMentorProjectPolicyService
{
    /// <summary>
    /// Applies <c>.junexis/mentor.yml</c> to a Mentor result. Invalid policy
    /// never hides a finding.
    /// </summary>
    /// <param name="result">Deterministic raw Mentor result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Visible result together with policy validation and suppression evidence.</returns>
    Task<MentorProjectPolicyEvaluation> ApplyAsync(
        MentorAnalysisResult result,
        CancellationToken cancellationToken = default);
}
