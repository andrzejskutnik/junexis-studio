using Junexis.Studio.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Junexis.Studio.Host;

/// <summary>
/// View model panelu Problems: prezentuje snapshot diagnostyk z <see cref="IDiagnosticService"/>
/// i publikuje <see cref="NavigateToLocationRequested"/> po aktywacji wpisu.
/// </summary>
public sealed class ProblemsPanelViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDiagnosticService _diagnostics;
    private readonly IEventBus _eventBus;
    private readonly IAnalysisSnapshotService? _analysis;
    private readonly IPreferencesService? _preferences;
    private IReadOnlyList<Diagnostic> _items = Array.Empty<Diagnostic>();
    private IReadOnlyList<ProblemItemViewModel> _problemItems = [];
    private AnalysisSnapshot? _snapshot;
    private MentorAnalysisEnvelope? _mentor;
    private string? _diagnosticsBuildId;
    private int _analysisLoadVersion;
    private ProblemItemViewModel? _selectedItem;

    /// <summary>
    /// Tworzy view model i podpina się pod <see cref="IDiagnosticService.DiagnosticsChanged"/>.
    /// </summary>
    /// <param name="diagnostics">Serwis diagnostyki.</param>
    /// <param name="eventBus">Event bus do publikacji nawigacji.</param>
    /// <param name="analysis">Optional compiler analysis snapshot reader.</param>
    /// <param name="preferences">Optional Mentor presentation preferences.</param>
    public ProblemsPanelViewModel(
        IDiagnosticService diagnostics,
        IEventBus eventBus,
        IAnalysisSnapshotService? analysis = null,
        IPreferencesService? preferences = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _analysis = analysis;
        _preferences = preferences;

        _diagnostics.DiagnosticsChanged += OnDiagnosticsChanged;
        _eventBus.Subscribe<MentorBuildPreflightCompletedEvent>(OnMentorPreflightCompleted);
        _eventBus.Subscribe<AnalysisSnapshotUpdatedEvent>(OnAnalysisSnapshotUpdated);
        _eventBus.Subscribe<DiagnosticsUpdatedEvent>(OnDiagnosticsUpdated);
        Refresh();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Snapshot diagnostyk do wyświetlenia.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics
    {
        get => _items;
        private set => SetField(ref _items, value);
    }

    /// <summary>Diagnostics enriched with optional, visually separate Mentor context.</summary>
    public IReadOnlyList<ProblemItemViewModel> Items
    {
        get => _problemItems;
        private set => SetField(ref _problemItems, value);
    }

    /// <summary>Selected compiler diagnostic controlling the contextual Mentor section.</summary>
    public ProblemItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetField(ref _selectedItem, value))
                return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedMentorContext)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMentorLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMentorEvidence)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMentorExplanation)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMentorNextStep)));
        }
    }

    /// <summary>Whether the selected diagnostic has current evidence-bounded Mentor context.</summary>
    public bool HasSelectedMentorContext => SelectedItem?.HasMentorContext == true;

    /// <summary>Explicit Mentor and finding label for the selected diagnostic.</summary>
    public string SelectedMentorLabel => SelectedItem?.MentorLabel ?? string.Empty;

    /// <summary>Compiler evidence used to associate the selected diagnostic with a symbol.</summary>
    public string SelectedMentorEvidence => SelectedItem?.MentorEvidence ?? string.Empty;

    /// <summary>Deterministic explanation for the selected diagnostic context.</summary>
    public string SelectedMentorExplanation => SelectedItem?.MentorExplanation ?? string.Empty;

    /// <summary>Smallest safe next step for the selected diagnostic context.</summary>
    public string SelectedMentorNextStep => SelectedItem?.MentorNextStep ?? string.Empty;

    /// <summary>
    /// Publikuje żądanie nawigacji do lokacji wskazanej przez diagnostykę.
    /// </summary>
    /// <param name="diagnostic">Diagnostyka do nawigacji.</param>
    public void NavigateTo(Diagnostic diagnostic)
    {
        if (diagnostic is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(diagnostic.FilePath))
        {
            return;
        }

        var line = diagnostic.Line.GetValueOrDefault(1);
        if (line < 1) line = 1;

        var col = diagnostic.Column.GetValueOrDefault(1);
        if (col < 1) col = 1;

        _eventBus.Publish(new NavigateToLocationRequested(diagnostic.FilePath, line, col));
    }

    /// <summary>Opens the canonical full Mentor report.</summary>
    public void OpenMentorReport() =>
        _eventBus.Publish(new ActivatePanelRequested("junexis.analysis.explorer"));

    /// <inheritdoc />
    public void Dispose()
    {
        _diagnostics.DiagnosticsChanged -= OnDiagnosticsChanged;
        _eventBus.Unsubscribe<MentorBuildPreflightCompletedEvent>(OnMentorPreflightCompleted);
        _eventBus.Unsubscribe<AnalysisSnapshotUpdatedEvent>(OnAnalysisSnapshotUpdated);
        _eventBus.Unsubscribe<DiagnosticsUpdatedEvent>(OnDiagnosticsUpdated);
    }

    private void OnDiagnosticsChanged()
    {
        Refresh();
    }

    private void Refresh()
    {
        var selectedDiagnostic = SelectedItem?.Diagnostic;
        Diagnostics = _diagnostics.GetDiagnostics();
        Items = Diagnostics.Select(diagnostic => new ProblemItemViewModel(
            diagnostic,
            MentorDiagnosticContextResolver.Resolve(
                diagnostic,
                _snapshot,
                _mentor,
                _diagnosticsBuildId),
            _preferences?.Current.Mentor.Presence ?? MentorPresenceMode.Teaching))
            .ToArray();
        SelectedItem = selectedDiagnostic is null
            ? null
            : Items.FirstOrDefault(item => item.Diagnostic == selectedDiagnostic);
    }

    private void OnMentorPreflightCompleted(MentorBuildPreflightCompletedEvent update)
    {
        _mentor = update.Envelope;
        if (update.Snapshot is not null
            && string.Equals(
                update.Snapshot.BuildId,
                update.Envelope.Result.BuildId,
                StringComparison.Ordinal))
            _snapshot = update.Snapshot;
        Refresh();
    }

    private void OnAnalysisSnapshotUpdated(AnalysisSnapshotUpdatedEvent update)
    {
        if (_analysis is null)
            return;
        _ = LoadAnalysisAsync(update);
    }

    private void OnDiagnosticsUpdated(DiagnosticsUpdatedEvent update)
    {
        if (!string.Equals(update.Source, "junexis-lang", StringComparison.OrdinalIgnoreCase))
            return;
        _diagnosticsBuildId = update.BuildId;
        Refresh();
    }

    private async Task LoadAnalysisAsync(AnalysisSnapshotUpdatedEvent update)
    {
        var version = Interlocked.Increment(ref _analysisLoadVersion);
        try
        {
            var snapshot = await _analysis!.LoadAsync(update.SnapshotPath).ConfigureAwait(false);
            if (version != Volatile.Read(ref _analysisLoadVersion)
                || !string.Equals(snapshot.BuildId, update.BuildId, StringComparison.Ordinal))
                return;
            _snapshot = snapshot;
            Refresh();
        }
        catch
        {
            if (version != Volatile.Read(ref _analysisLoadVersion))
                return;
            _snapshot = null;
            Refresh();
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>One compiler diagnostic with optional non-authoritative Mentor context.</summary>
public sealed class ProblemItemViewModel
{
    /// <summary>Creates a Problems row.</summary>
    public ProblemItemViewModel(
        Diagnostic diagnostic,
        MentorDiagnosticContext? mentorContext,
        MentorPresenceMode presence = MentorPresenceMode.Teaching)
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        MentorContext = mentorContext;
        Presence = presence;
    }

    /// <summary>Original compiler or tooling diagnostic.</summary>
    public Diagnostic Diagnostic { get; }

    /// <summary>Optional evidence-bounded Mentor context.</summary>
    public MentorDiagnosticContext? MentorContext { get; }

    /// <summary>Mentor presentation mode active when the row was built.</summary>
    public MentorPresenceMode Presence { get; }

    /// <summary>Whether a separate Mentor section should be shown.</summary>
    public bool HasMentorContext =>
        MentorContext is not null && Presence != MentorPresenceMode.Quiet;

    /// <summary>Compact explicit Mentor label and evidence reference.</summary>
    public string MentorLabel => MentorContext is null
        ? string.Empty
        : $"Mentor · Current · {MentorContext.ExplanationReference}";

    /// <summary>Compact proof that the association came from compiler-owned metadata.</summary>
    public string MentorEvidence => MentorContext is null
        ? string.Empty
        : $"Evidence: {MentorContext.MappingKind} · {MentorContext.SymbolId} · build "
          + MentorContext.BuildId[..Math.Min(12, MentorContext.BuildId.Length)];

    /// <summary>Deterministic explanation from relevant Mentor findings.</summary>
    public string MentorExplanation => MentorContext is null
        ? string.Empty
        : string.Join(" ", MentorContext.Findings
            .Select(finding => finding.Explanation)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal));

    /// <summary>Smallest safe next step, kept separate from the compiler message.</summary>
    public string MentorNextStep => string.IsNullOrWhiteSpace(
        MentorContext?.SmallestSafeNextStep)
        ? string.Empty
        : $"Next step: {MentorContext.SmallestSafeNextStep}";
}
