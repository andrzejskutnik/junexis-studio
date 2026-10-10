using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Runs deterministic Mentor analysis before code generation without gating builds.</summary>
public sealed class MentorBuildPreflightService : IMentorBuildPreflightService
{
    private readonly IMentorAnalysisService _mentor;
    private readonly IEventBus _events;
    private readonly IOutput _output;
    private readonly IPreferencesService? _preferences;
    private readonly IAnalysisSnapshotService? _analysis;
    private readonly IMentorProjectPolicyService? _projectPolicy;
    private readonly Dictionary<string, MentorAnalysisResult> _lastCurrentByProject =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _baselineGate = new();

    /// <summary>Creates the host-level Mentor build coordinator.</summary>
    public MentorBuildPreflightService(
        IMentorAnalysisService mentor,
        IEventBus events,
        IOutput output,
        IPreferencesService? preferences = null,
        IAnalysisSnapshotService? analysis = null,
        IMentorProjectPolicyService? projectPolicy = null)
    {
        _mentor = mentor ?? throw new ArgumentNullException(nameof(mentor));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _preferences = preferences;
        _analysis = analysis;
        _projectPolicy = projectPolicy;
    }

    /// <inheritdoc />
    public async Task<MentorBuildPreflightOutcome> RunAsync(
        CompilerPreflightResult preflight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        if (!preflight.Success
            || string.IsNullOrWhiteSpace(preflight.BuildId)
            || string.IsNullOrWhiteSpace(preflight.AnalysisSnapshotPath))
        {
            return new MentorBuildPreflightOutcome(
                false, null, "Mentor skipped: compiler analysis is unavailable.");
        }

        try
        {
            var result = await _mentor.AnalyzeAsync(
                preflight.AnalysisSnapshotPath,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(result.BuildId, preflight.BuildId, StringComparison.Ordinal))
                throw new InvalidDataException("Mentor result does not match the compiler preflight build.");
            var snapshot = _analysis is null
                ? null
                : await _analysis.LoadAsync(
                    preflight.AnalysisSnapshotPath,
                    cancellationToken).ConfigureAwait(false);
            if (snapshot is not null
                && !string.Equals(snapshot.BuildId, preflight.BuildId, StringComparison.Ordinal))
                throw new InvalidDataException("Analysis snapshot does not match the compiler preflight build.");
            var policyEvaluation = _projectPolicy is null
                ? null
                : await _projectPolicy.ApplyAsync(result, cancellationToken).ConfigureAwait(false);
            if (policyEvaluation is not null)
                result = policyEvaluation.Result;

            var projectKey = ProjectKey(preflight);
            MentorFindingDelta delta;
            lock (_baselineGate)
            {
                _lastCurrentByProject.TryGetValue(projectKey, out var previous);
                delta = MentorFindingDeltaCalculator.Calculate(
                    previous?.Findings,
                    result.Findings);
                _lastCurrentByProject[projectKey] = result;
            }
            var envelope = new MentorAnalysisEnvelope(
                result,
                DateTimeOffset.UtcNow,
                MentorAnalysisFreshness.Current,
                delta,
                policyEvaluation);
            var message = FormatSummary(envelope);
            var presence = policyEvaluation?.Policy.Presence
                ?? _preferences?.Current.Mentor.Presence
                ?? MentorPresenceMode.Teaching;
            if (presence != MentorPresenceMode.Quiet)
            {
                _output.Write("Mentor", message, OutputSeverity.Info);
                WriteRelevantChanges(envelope, presence);
            }
            _events.Publish(new MentorBuildPreflightCompletedEvent(envelope, snapshot));
            _events.Publish(new MentorAnalysisCompletedEvent(
                result.BuildId,
                result.Findings.Count));
            return new MentorBuildPreflightOutcome(true, envelope, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Mentor preflight unavailable; build continues: {ex.Message}";
            _output.Write("Mentor", message, OutputSeverity.Warning);
            return new MentorBuildPreflightOutcome(false, null, message);
        }
    }

    private static string ProjectKey(CompilerPreflightResult preflight)
    {
        var candidate = preflight.ProjectRoot;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = Path.GetDirectoryName(
                Path.GetDirectoryName(preflight.AnalysisSnapshotPath!));
        }
        return string.IsNullOrWhiteSpace(candidate)
            ? preflight.AnalysisSnapshotPath!
            : Path.GetFullPath(candidate);
    }

    private string FormatSummary(MentorAnalysisEnvelope envelope)
    {
        var delta = envelope.Delta;
        var suppressed = envelope.ProjectPolicy?.AppliedSuppressions.Count ?? 0;
        return $"Preflight: {envelope.Result.Findings.Count} finding(s); "
               + $"+{delta.AddedCount} new, ~{delta.ChangedCount} changed, "
               + $"{delta.ResolvedCount} resolved; {suppressed} suppressed by project policy. "
               + "Full report: Analysis panel.";
    }

    private void WriteRelevantChanges(
        MentorAnalysisEnvelope envelope,
        MentorPresenceMode presence)
    {
        foreach (var change in envelope.Delta.Changes
                     .Where(change => change.Kind is MentorFindingChangeKind.Added
                         or MentorFindingChangeKind.Changed)
                     .Select(change => change.Current!)
                     .Where(finding => finding.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)
                         || finding.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase))
                     .Take(3))
        {
            _output.Write("Mentor",
                $"{change.Code} [{change.Severity}] {change.Message}",
                change.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)
                    ? OutputSeverity.Error
                    : OutputSeverity.Warning);
            if (presence == MentorPresenceMode.Teaching)
            {
                var learning = string.Join(" ",
                    new[] { change.Explanation, change.RecommendedAction }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(learning))
                    _output.Write("Mentor", $"Learn: {learning}", OutputSeverity.Info);
            }
        }
    }
}
