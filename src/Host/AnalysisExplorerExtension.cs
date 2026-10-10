using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>View model for symbols, communications, and topology of the latest Junexis build.</summary>
public sealed class AnalysisExplorerViewModel : INotifyPropertyChanged
{
    private readonly IAnalysisSnapshotService _analysis;
    private readonly IEventBus _eventBus;
    private readonly IOutput _output;
    private readonly IMentorAnalysisService? _mentor;
    private readonly IMentorRefactoringService? _refactorings;
    private readonly IMentorProjectPolicyService? _projectPolicy;
    private readonly IPreferencesService? _preferences;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly ConcurrentDictionary<string, MentorAnalysisEnvelope> _preflightResults =
        new(StringComparer.Ordinal);
    private MentorAnalysisResult? _lastCurrentMentorResult;
    private int _loadVersion;
    private IReadOnlyList<AnalysisSymbol> _symbols = [];
    private IReadOnlyList<AnalysisRelation> _relations = [];
    private IReadOnlyList<MentorFinding> _findings = [];
    private IReadOnlyList<MentorInsight> _insights = [];
    private IReadOnlyList<MentorPattern> _patterns = [];
    private IReadOnlyList<MentorConformanceGap> _conformanceGaps = [];
    private IReadOnlyList<MentorProjectDecision> _projectDecisions = [];
    private IReadOnlyList<MentorProjectSuppression> _projectSuppressions = [];
    private IReadOnlyList<MentorAppliedSuppression> _appliedSuppressions = [];
    private MentorAnalysisEnvelope? _mentorEnvelope;
    private AnalysisSnapshot? _snapshot;
    private MentorFinding? _selectedFinding;
    private MentorRefactoringProposal? _refactoringProposal;
    private MentorRefactoringSession? _refactoringSession;
    private MentorPresenceMode _mentorPresence = MentorPresenceMode.Teaching;
    private string _statusText = "Build a Junexis project to load analysis.";
    private string _topologyText = "Topology unavailable.";
    private string _mentorStatusText = "Mentor analysis unavailable.";
    private string _mentorPresenceText = "Mentor · Teaching";
    private string _mentorFreshnessText = "Freshness · Unknown";
    private string _mentorDeltaText = "No comparison available.";
    private string _conformanceStatusText = "Semantic Conformance evidence unavailable.";
    private string _refactoringStatusText = "Select a supported Mentor finding to preview a safe refactoring.";
    private string _projectPolicyStatusText = "Project Mentor policy unavailable.";

    /// <summary>Creates the Analysis panel view model.</summary>
    /// <param name="analysis">Analysis snapshot reader.</param>
    /// <param name="eventBus">Studio event bus used for source navigation.</param>
    /// <param name="output">Studio output log.</param>
    /// <param name="mentor">Optional Mentor CLI adapter.</param>
    /// <param name="preferences">Optional Studio preferences used for Mentor presence.</param>
    /// <param name="refactorings">Optional safe Mentor refactoring boundary.</param>
    /// <param name="projectPolicy">Optional project decisions and suppressions service.</param>
    public AnalysisExplorerViewModel(
        IAnalysisSnapshotService analysis,
        IEventBus eventBus,
        IOutput output,
        IMentorAnalysisService? mentor = null,
        IPreferencesService? preferences = null,
        IMentorRefactoringService? refactorings = null,
        IMentorProjectPolicyService? projectPolicy = null)
    {
        _analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _mentor = mentor;
        _preferences = preferences;
        _refactorings = refactorings;
        _projectPolicy = projectPolicy;
        _synchronizationContext = SynchronizationContext.Current;
        ApplyPresence();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stable Junexis symbols ordered by kind and name.</summary>
    public IReadOnlyList<AnalysisSymbol> Symbols
    {
        get => _symbols;
        private set => SetField(ref _symbols, value);
    }

    /// <summary>Stable communication relations ordered by pipeline and identifier.</summary>
    public IReadOnlyList<AnalysisRelation> Relations
    {
        get => _relations;
        private set => SetField(ref _relations, value);
    }

    /// <summary>Deterministic findings produced by Mentor for the current build.</summary>
    public IReadOnlyList<MentorFinding> Findings
    {
        get => _findings;
        private set => SetField(ref _findings, value);
    }

    /// <summary>Computed architectural insights produced by Mentor.</summary>
    public IReadOnlyList<MentorInsight> Insights
    {
        get => _insights;
        private set => SetField(ref _insights, value);
    }

    /// <summary>Architectural patterns recognized by Mentor.</summary>
    public IReadOnlyList<MentorPattern> Patterns
    {
        get => _patterns;
        private set => SetField(ref _patterns, value);
    }

    /// <summary>Semantic Conformance scenarios that remain below their declared target.</summary>
    public IReadOnlyList<MentorConformanceGap> ConformanceGaps
    {
        get => _conformanceGaps;
        private set => SetField(ref _conformanceGaps, value);
    }

    /// <summary>Validated architectural decisions from the project Mentor policy.</summary>
    public IReadOnlyList<MentorProjectDecision> ProjectDecisions
    {
        get => _projectDecisions;
        private set => SetField(ref _projectDecisions, value);
    }

    /// <summary>Validated suppressions configured by the project.</summary>
    public IReadOnlyList<MentorProjectSuppression> ProjectSuppressions
    {
        get => _projectSuppressions;
        private set => SetField(ref _projectSuppressions, value);
    }

    /// <summary>Configured suppressions that matched findings in the current build.</summary>
    public IReadOnlyList<MentorAppliedSuppression> AppliedSuppressions
    {
        get => _appliedSuppressions;
        private set => SetField(ref _appliedSuppressions, value);
    }

    /// <summary>Latest Mentor result together with freshness and finding delta.</summary>
    public MentorAnalysisEnvelope? MentorEnvelope
    {
        get => _mentorEnvelope;
        private set => SetField(ref _mentorEnvelope, value);
    }

    /// <summary>Configured Mentor presentation mode.</summary>
    public MentorPresenceMode MentorPresence
    {
        get => _mentorPresence;
        private set => SetField(ref _mentorPresence, value);
    }

    /// <summary>Status of the latest analysis load.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>Compact topology summary of the current build.</summary>
    public string TopologyText
    {
        get => _topologyText;
        private set => SetField(ref _topologyText, value);
    }

    /// <summary>Status of Mentor analysis for the current build.</summary>
    public string MentorStatusText
    {
        get => _mentorStatusText;
        private set => SetField(ref _mentorStatusText, value);
    }

    /// <summary>Compact, explicit label identifying Mentor and its presence mode.</summary>
    public string MentorPresenceText
    {
        get => _mentorPresenceText;
        private set => SetField(ref _mentorPresenceText, value);
    }

    /// <summary>Human-readable freshness, schema, build, and analysis time.</summary>
    public string MentorFreshnessText
    {
        get => _mentorFreshnessText;
        private set => SetField(ref _mentorFreshnessText, value);
    }

    /// <summary>Compact difference from the previous current Mentor result.</summary>
    public string MentorDeltaText
    {
        get => _mentorDeltaText;
        private set => SetField(ref _mentorDeltaText, value);
    }

    /// <summary>Compact non-blocking Semantic Conformance opinion from Mentor.</summary>
    public string ConformanceStatusText
    {
        get => _conformanceStatusText;
        private set => SetField(ref _conformanceStatusText, value);
    }

    /// <summary>Finding selected for navigation or a safe refactoring preview.</summary>
    public MentorFinding? SelectedFinding
    {
        get => _selectedFinding;
        set
        {
            if (!SetField(ref _selectedFinding, value))
                return;
            OnPropertyChanged(nameof(CanPreviewRefactoring));
        }
    }

    /// <summary>Exact patch currently presented for review.</summary>
    public string RefactoringPatch => _refactoringProposal?.Patch ?? string.Empty;

    /// <summary>Expected effect and explicit assumptions for the current preview.</summary>
    public string RefactoringEvidenceText => _refactoringProposal is null
        ? string.Empty
        : $"{_refactoringProposal.ExpectedEffect}{Environment.NewLine}"
          + string.Join(
              Environment.NewLine,
              _refactoringProposal.Assumptions.Select(assumption => $"Assumption: {assumption}"));

    /// <summary>Whether an exact patch and its assumptions are currently visible.</summary>
    public bool HasRefactoringPreview => _refactoringProposal is not null;

    /// <summary>Current safe-refactoring lifecycle status.</summary>
    public string RefactoringStatusText
    {
        get => _refactoringStatusText;
        private set => SetField(ref _refactoringStatusText, value);
    }

    /// <summary>Validation and application status of <c>.junexis/mentor.yml</c>.</summary>
    public string ProjectPolicyStatusText
    {
        get => _projectPolicyStatusText;
        private set => SetField(ref _projectPolicyStatusText, value);
    }

    /// <summary>Whether the selected finding has a deterministic refactoring valid for the current snapshot.</summary>
    public bool CanPreviewRefactoring =>
        _refactorings is not null
        && _snapshot is not null
        && MentorEnvelope?.Freshness == MentorAnalysisFreshness.Current
        && HasSafeRefactoring(_snapshot, SelectedFinding);

    /// <summary>Whether an exact patch is ready to apply.</summary>
    public bool CanApplyRefactoring =>
        _refactoringProposal is not null
        && _refactoringSession is null;

    /// <summary>Whether compiler preflight can be run for the applied patch.</summary>
    public bool CanValidateRefactoring =>
        _refactoringSession?.State == MentorRefactoringState.Applied;

    /// <summary>Whether the compiler-validated patch can be explicitly kept.</summary>
    public bool CanKeepRefactoring =>
        _refactoringSession?.State == MentorRefactoringState.Validated;

    /// <summary>Whether the applied patch can still be restored safely.</summary>
    public bool CanRevertRefactoring =>
        _refactoringSession?.State is MentorRefactoringState.Applied
            or MentorRefactoringState.Validated
            or MentorRefactoringState.ValidationFailed;

    /// <summary>Loads a compiler analysis snapshot and refreshes the panel.</summary>
    /// <param name="snapshotPath">Path published by the compiler build result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task LoadAsync(string snapshotPath, CancellationToken cancellationToken = default)
    {
        var loadVersion = Interlocked.Increment(ref _loadVersion);
        ApplyPresence();
        try
        {
            var snapshot = await _analysis.LoadAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
            if (loadVersion != Volatile.Read(ref _loadVersion))
                return;
            Update(() =>
            {
                _snapshot = snapshot;
                SelectedFinding = null;
                _refactoringProposal = null;
                _refactoringSession = null;
                RefactoringStatusText = "Select a supported Mentor finding to preview a safe refactoring.";
                NotifyRefactoringState();
                Symbols = snapshot.Symbols
                    .OrderBy(symbol => symbol.Kind, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                Relations = snapshot.Relations
                    .OrderBy(relation => relation.PipelineSymbolId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(relation => relation.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var shortBuildId = snapshot.BuildId[..Math.Min(12, snapshot.BuildId.Length)];
                StatusText = $"{Symbols.Count} symbol(s), {Relations.Count} communication(s), build {shortBuildId}.";
                TopologyText = $"Cycles: {(snapshot.Topology.HasCycles ? "yes" : "no")}; max depth: {snapshot.Topology.MaxDepth}; SCC: {snapshot.Topology.StronglyConnectedComponents.Count}.";
            });

            if (_preflightResults.TryGetValue(snapshot.BuildId, out var preflightEnvelope))
            {
                ApplyMentorEnvelope(preflightEnvelope);
            }
            else if (_mentor is not null)
            {
                try
                {
                    var mentorResult = await _mentor.AnalyzeAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
                    if (loadVersion != Volatile.Read(ref _loadVersion))
                        return;
                    var policyEvaluation = _projectPolicy is null
                        ? null
                        : await _projectPolicy.ApplyAsync(mentorResult, cancellationToken).ConfigureAwait(false);
                    if (policyEvaluation is not null)
                        mentorResult = policyEvaluation.Result;
                    var freshness = DetermineFreshness(snapshot.BuildId, mentorResult.BuildId);
                    var delta = freshness == MentorAnalysisFreshness.Current
                        ? MentorFindingDeltaCalculator.Calculate(_lastCurrentMentorResult?.Findings, mentorResult.Findings)
                        : new MentorFindingDelta([]);
                    var analyzedAtUtc = DateTimeOffset.UtcNow;
                    var envelope = new MentorAnalysisEnvelope(
                        mentorResult,
                        analyzedAtUtc,
                        freshness,
                        delta,
                        policyEvaluation);
                    ApplyMentorEnvelope(envelope);
                    if (freshness == MentorAnalysisFreshness.Current)
                    {
                        _eventBus.Publish(new MentorAnalysisCompletedEvent(
                            mentorResult.BuildId,
                            mentorResult.Findings.Count));
                    }
                    else
                    {
                        _output.Write(
                            "Mentor",
                            $"Mentor result is {freshness.ToString().ToLowerInvariant()}; active build '{snapshot.BuildId}', result build '{mentorResult.BuildId}'.",
                            OutputSeverity.Warning);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Update(() =>
                    {
                        Findings = [];
                        Insights = [];
                        Patterns = [];
                        ConformanceGaps = [];
                        ProjectDecisions = [];
                        ProjectSuppressions = [];
                        AppliedSuppressions = [];
                        MentorEnvelope = null;
                        MentorFreshnessText = "Freshness · Unknown";
                        MentorDeltaText = "No comparison available.";
                        ConformanceStatusText = "Semantic Conformance evidence unavailable.";
                        MentorStatusText = $"Mentor unavailable: {ex.Message}";
                        ProjectPolicyStatusText = "Project Mentor policy unavailable.";
                    });
                    _output.Write("Mentor", MentorStatusText, OutputSeverity.Warning);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Update(() => StatusText = "Analysis loading cancelled.");
        }
        catch (Exception ex)
        {
            Update(() =>
            {
                Symbols = [];
                Relations = [];
                Findings = [];
                Insights = [];
                Patterns = [];
                ConformanceGaps = [];
                ProjectDecisions = [];
                ProjectSuppressions = [];
                AppliedSuppressions = [];
                MentorEnvelope = null;
                _snapshot = null;
                SelectedFinding = null;
                _refactoringProposal = null;
                _refactoringSession = null;
                StatusText = $"Cannot load analysis: {ex.Message}";
                TopologyText = "Topology unavailable.";
                MentorStatusText = "Mentor analysis unavailable.";
                MentorFreshnessText = "Freshness · Unknown";
                MentorDeltaText = "No comparison available.";
                ConformanceStatusText = "Semantic Conformance evidence unavailable.";
                RefactoringStatusText = "Safe refactoring unavailable until current analysis loads.";
                ProjectPolicyStatusText = "Project Mentor policy unavailable.";
                NotifyRefactoringState();
            });
            _output.Write("Analysis", StatusText, OutputSeverity.Error);
        }
    }

    /// <summary>Loads a compiler preflight snapshot with its already computed Mentor result.</summary>
    public Task LoadPreflightAsync(
        MentorAnalysisEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _preflightResults[envelope.Result.BuildId] = envelope;
        return LoadAsync(envelope.Result.SnapshotPath, cancellationToken);
    }

    /// <summary>Navigates to the declaration of a selected symbol when source metadata exists.</summary>
    /// <param name="symbol">Selected analysis symbol.</param>
    public void NavigateTo(AnalysisSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        Navigate(symbol.Source);
    }

    /// <summary>Navigates to a selected communication or its source task as fallback.</summary>
    /// <param name="relation">Selected analysis relation.</param>
    public void NavigateTo(AnalysisRelation relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        var source = relation.Source ?? Symbols.FirstOrDefault(symbol => symbol.Id == relation.SourceSymbolId)?.Source;
        Navigate(source);
    }

    /// <summary>Navigates from a Mentor finding to its source or related symbol declaration.</summary>
    /// <param name="finding">Selected Mentor finding.</param>
    public void NavigateTo(MentorFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var source = finding.Source;
        if (source is null && finding.RelatedSymbolId is not null)
            source = Symbols.FirstOrDefault(symbol => symbol.Id == finding.RelatedSymbolId)?.Source;
        Navigate(source);
    }

    /// <summary>Creates the exact patch for the selected supported finding.</summary>
    public void PreviewSelectedRefactoring()
    {
        if (!CanPreviewRefactoring || _snapshot is null || SelectedFinding is null || _refactorings is null)
            return;
        try
        {
            _refactoringProposal = _refactorings.CreateProposal(_snapshot, SelectedFinding);
            _refactoringSession = null;
            RefactoringStatusText =
                $"{_refactoringProposal.Title}. Review the exact patch and assumptions before applying.";
            NotifyRefactoringState();
        }
        catch (Exception ex)
        {
            RefactoringStatusText = $"Cannot create refactoring preview: {ex.Message}";
            _output.Write("Mentor", RefactoringStatusText, OutputSeverity.Warning);
        }
    }

    /// <summary>Applies the exact reviewed patch without keeping it automatically.</summary>
    public async Task ApplyRefactoringAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyRefactoring || _refactoringProposal is null || _refactorings is null)
            return;
        await RunRefactoringStepAsync(
            () => _refactorings.ApplyAsync(_refactoringProposal, cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Validates the applied patch through compiler preflight.</summary>
    public async Task ValidateRefactoringAsync(CancellationToken cancellationToken = default)
    {
        if (!CanValidateRefactoring || _refactoringSession is null || _refactorings is null)
            return;
        await RunRefactoringStepAsync(
            () => _refactorings.ValidateAsync(_refactoringSession, cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Explicitly keeps a compiler-validated patch.</summary>
    public void KeepRefactoring()
    {
        if (!CanKeepRefactoring || _refactoringSession is null || _refactorings is null)
            return;
        try
        {
            _refactoringSession = _refactorings.Keep(_refactoringSession);
            RefactoringStatusText = _refactoringSession.Message;
            NotifyRefactoringState();
        }
        catch (Exception ex)
        {
            RefactoringStatusText = $"Cannot keep refactoring: {ex.Message}";
        }
    }

    /// <summary>Restores the exact original source when no later edit would be lost.</summary>
    public async Task RevertRefactoringAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRevertRefactoring || _refactoringSession is null || _refactorings is null)
            return;
        await RunRefactoringStepAsync(
            () => _refactorings.RevertAsync(_refactoringSession, cancellationToken)).ConfigureAwait(false);
    }

    private void Navigate(AnalysisSourceLocation? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.File))
        {
            _output.Write("Analysis", "Selected item has no Junexis source location.", OutputSeverity.Warning);
            return;
        }

        _eventBus.Publish(new NavigateToLocationRequested(
            source.File,
            Math.Max(1, source.Line),
            Math.Max(1, source.Column)));
    }

    private static bool HasSafeRefactoring(
        AnalysisSnapshot snapshot,
        MentorFinding? finding)
    {
        if (string.Equals(finding?.Code, "M1002", StringComparison.Ordinal))
            return true;
        return string.Equals(finding?.Code, "M1012", StringComparison.Ordinal)
            && snapshot.Symbols.Count(symbol =>
                string.Equals(symbol.Kind, "pipeline", StringComparison.OrdinalIgnoreCase)) == 1;
    }

    private void Update(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
            action();
        else
            _synchronizationContext.Post(_ => action(), null);
    }

    private void ApplyPresence()
    {
        var presence = _preferences?.Current.Mentor?.Presence ?? MentorPresenceMode.Teaching;
        Update(() =>
        {
            MentorPresence = presence;
            MentorPresenceText = $"Mentor · {presence}";
        });
    }

    private void ApplyMentorEnvelope(MentorAnalysisEnvelope envelope)
    {
        var mentorResult = envelope.Result;
        if (envelope.Freshness == MentorAnalysisFreshness.Current)
            _lastCurrentMentorResult = mentorResult;
        Update(() =>
        {
            MentorEnvelope = envelope;
            Findings = mentorResult.Findings
                .OrderByDescending(finding => SeverityOrder(finding.Severity))
                .ThenBy(finding => finding.Code, StringComparer.Ordinal)
                .ToArray();
            Insights = mentorResult.Insights ?? [];
            Patterns = mentorResult.Patterns ?? [];
            ConformanceGaps = mentorResult.Conformance?.Gaps ?? [];
            ApplyProjectPolicy(envelope.ProjectPolicy);
            MentorFreshnessText = FormatFreshness(envelope);
            MentorDeltaText = FormatDelta(envelope);
            ConformanceStatusText = mentorResult.Conformance is { } conformance
                ? $"{conformance.Status.ToUpperInvariant()} · {conformance.TargetSatisfiedCount}/{conformance.ScenarioCount} target(s) · {conformance.GapCount} non-blocking gap(s) · "
                  + (conformance.Bound
                      ? $"BOUND build {conformance.BuildId![..12]} / bundle {conformance.BundleId![..12]}. "
                      : "UNBOUND evidence. ")
                  + conformance.Opinion
                : "Semantic Conformance evidence unavailable.";
            MentorStatusText = mentorResult.Summary is { } summary
                ? $"Mentor: {summary.TaskCount} task(s), {Findings.Count} finding(s), {Insights.Count} insight(s), {Patterns.Count} pattern(s)."
                : $"Mentor: {Findings.Count} deterministic finding(s).";
            NotifyRefactoringState();
        });
    }

    private void ApplyProjectPolicy(MentorProjectPolicyEvaluation? evaluation)
    {
        if (evaluation is null)
        {
            ProjectDecisions = [];
            ProjectSuppressions = [];
            AppliedSuppressions = [];
            ProjectPolicyStatusText = "Project Mentor policy service unavailable.";
            return;
        }
        if (!evaluation.IsConfigured)
        {
            ProjectDecisions = [];
            ProjectSuppressions = [];
            AppliedSuppressions = [];
            ProjectPolicyStatusText = $"No project policy. Expected: {evaluation.PolicyPath}";
            return;
        }
        if (!evaluation.IsValid)
        {
            ProjectDecisions = [];
            ProjectSuppressions = [];
            AppliedSuppressions = [];
            ProjectPolicyStatusText =
                $"Invalid project policy; no finding was hidden. {string.Join(" ", evaluation.ValidationErrors)}";
            return;
        }

        ProjectDecisions = evaluation.Policy.Decisions;
        ProjectSuppressions = evaluation.Policy.Suppressions;
        AppliedSuppressions = evaluation.AppliedSuppressions;
        ProjectPolicyStatusText =
            $"Policy 1.0 · {ProjectDecisions.Count} decision(s) · "
            + $"{ProjectSuppressions.Count} suppression(s) · "
            + $"{AppliedSuppressions.Count} applied in this build.";
        if (evaluation.Policy.Presence is { } presence)
        {
            MentorPresence = presence;
            MentorPresenceText = $"Mentor · {presence} · project";
        }
    }

    private static MentorAnalysisFreshness DetermineFreshness(string snapshotBuildId, string resultBuildId)
    {
        if (string.IsNullOrWhiteSpace(snapshotBuildId) || string.IsNullOrWhiteSpace(resultBuildId))
            return MentorAnalysisFreshness.Unknown;
        return string.Equals(snapshotBuildId, resultBuildId, StringComparison.Ordinal)
            ? MentorAnalysisFreshness.Current
            : MentorAnalysisFreshness.Stale;
    }

    private static string FormatFreshness(MentorAnalysisEnvelope envelope)
    {
        var result = envelope.Result;
        var shortBuildId = string.IsNullOrWhiteSpace(result.BuildId)
            ? "unknown"
            : result.BuildId[..Math.Min(12, result.BuildId.Length)];
        return $"{envelope.Freshness} · schema {result.SchemaVersion} · build {shortBuildId} · {envelope.AnalyzedAtUtc:HH:mm:ss} UTC";
    }

    private static string FormatDelta(MentorAnalysisEnvelope envelope)
    {
        if (envelope.Freshness != MentorAnalysisFreshness.Current)
            return "Delta unavailable for a non-current result.";
        var delta = envelope.Delta;
        if (delta.Changes.Count == 0)
            return "No findings and no changes.";
        if (delta.AddedCount == 0 && delta.ChangedCount == 0 && delta.ResolvedCount == 0)
            return $"{delta.UnchangedCount} unchanged.";
        return $"+{delta.AddedCount} new · ~{delta.ChangedCount} changed · ✓{delta.ResolvedCount} resolved";
    }

    private async Task RunRefactoringStepAsync(Func<Task<MentorRefactoringSession>> step)
    {
        try
        {
            var session = await step().ConfigureAwait(false);
            Update(() =>
            {
                _refactoringSession = session;
                RefactoringStatusText = session.Message;
                NotifyRefactoringState();
            });
        }
        catch (OperationCanceledException)
        {
            Update(() => RefactoringStatusText = "Mentor refactoring cancelled.");
        }
        catch (Exception ex)
        {
            Update(() => RefactoringStatusText = $"Mentor refactoring stopped safely: {ex.Message}");
            _output.Write("Mentor", RefactoringStatusText, OutputSeverity.Warning);
        }
    }

    private void NotifyRefactoringState()
    {
        OnPropertyChanged(nameof(RefactoringPatch));
        OnPropertyChanged(nameof(RefactoringEvidenceText));
        OnPropertyChanged(nameof(HasRefactoringPreview));
        OnPropertyChanged(nameof(CanPreviewRefactoring));
        OnPropertyChanged(nameof(CanApplyRefactoring));
        OnPropertyChanged(nameof(CanValidateRefactoring));
        OnPropertyChanged(nameof(CanKeepRefactoring));
        OnPropertyChanged(nameof(CanRevertRefactoring));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static int SeverityOrder(string severity) => severity.ToLowerInvariant() switch
    {
        "error" => 3,
        "warning" => 2,
        _ => 1
    };
}

internal static class MentorFindingDeltaCalculator
{
    internal static MentorFindingDelta Calculate(
        IReadOnlyList<MentorFinding>? previous,
        IReadOnlyList<MentorFinding> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var previousByIdentity = (previous ?? [])
            .GroupBy(Identity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var currentByIdentity = current
            .GroupBy(Identity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var identities = previousByIdentity.Keys
            .Concat(currentByIdentity.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(identity => identity, StringComparer.Ordinal);
        var changes = new List<MentorFindingChange>();

        foreach (var identity in identities)
        {
            previousByIdentity.TryGetValue(identity, out var previousFinding);
            currentByIdentity.TryGetValue(identity, out var currentFinding);
            changes.Add((previousFinding, currentFinding) switch
            {
                (null, not null) => new MentorFindingChange(
                    MentorFindingChangeKind.Added,
                    currentFinding,
                    null),
                (not null, null) => new MentorFindingChange(
                    MentorFindingChangeKind.Resolved,
                    null,
                    previousFinding),
                (not null, not null) when previousFinding == currentFinding => new MentorFindingChange(
                    MentorFindingChangeKind.Unchanged,
                    currentFinding,
                    previousFinding),
                _ => new MentorFindingChange(
                    MentorFindingChangeKind.Changed,
                    currentFinding,
                    previousFinding)
            });
        }

        return new MentorFindingDelta(changes);
    }

    private static string Identity(MentorFinding finding)
    {
        var stableSubject = !string.IsNullOrWhiteSpace(finding.RelatedSymbolId)
            ? finding.RelatedSymbolId
            : finding.Source is { } source
                ? $"{source.File}:{source.Line}:{source.Column}"
                : finding.Message;
        return $"{finding.Code}\u001f{stableSubject}";
    }
}

internal sealed class AnalysisExplorerExtension : IJunexisExtension
{
    private IEventBus? _eventBus;
    private AnalysisExplorerViewModel? _viewModel;

    public string Id => "junexis.analysis.explorer";

    public void Initialize(IHostContext context)
    {
        if (!context.Capabilities.TryGet<IAnalysisSnapshotService>(out var analysis) || analysis is null)
        {
            context.Output.Write("Analysis", "IAnalysisSnapshotService unavailable — Analysis panel inactive.");
            return;
        }

        _eventBus = context.EventBus;
        context.Capabilities.TryGet<IMentorAnalysisService>(out var mentor);
        context.Capabilities.TryGet<IMentorRefactoringService>(out var refactorings);
        context.Capabilities.TryGet<IMentorProjectPolicyService>(out var projectPolicy);
        _viewModel = new AnalysisExplorerViewModel(
            analysis,
            context.EventBus,
            context.Output,
            mentor,
            context.Preferences,
            refactorings,
            projectPolicy);
        _eventBus.Subscribe<AnalysisSnapshotUpdatedEvent>(OnAnalysisUpdated);
        _eventBus.Subscribe<MentorBuildPreflightCompletedEvent>(OnMentorPreflightCompleted);
        context.Panels.RegisterPanel(
            Id,
            PanelType.Tool,
            () => _viewModel,
            title: "Analysis",
            dockGroup: DockGroup.Left);
    }

    public void Shutdown()
    {
        _eventBus?.Unsubscribe<AnalysisSnapshotUpdatedEvent>(OnAnalysisUpdated);
        _eventBus?.Unsubscribe<MentorBuildPreflightCompletedEvent>(OnMentorPreflightCompleted);
        _eventBus = null;
        _viewModel = null;
    }

    private void OnAnalysisUpdated(AnalysisSnapshotUpdatedEvent update)
    {
        if (_viewModel is not null)
            _ = _viewModel.LoadAsync(update.SnapshotPath);
    }

    private void OnMentorPreflightCompleted(MentorBuildPreflightCompletedEvent update)
    {
        if (_viewModel is not null)
            _ = _viewModel.LoadPreflightAsync(update.Envelope);
    }
}
