using System.ComponentModel;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Pojedynczy wpis w łańcuchu przyczynowym zwracanym przez TraceCollector.
/// </summary>
/// <param name="EventId">Unikalny identyfikator zdarzenia.</param>
/// <param name="TaskId">Id zadania powiązanego ze zdarzeniem.</param>
/// <param name="Kind">Rodzaj zdarzenia.</param>
/// <param name="LamportClock">Znormalizowany zegar Lamporta.</param>
/// <param name="Timestamp">Znacznik czasu w formacie tekstowym.</param>
/// <param name="NarrativeId">Id narracji, jeśli zostało zapisane w atrybutach.</param>
public sealed record WhyTraceChainEntry(
    ulong EventId,
    uint TaskId,
    string Kind,
    long LamportClock,
    string Timestamp,
    string? NarrativeId);

/// <summary>A trace event resolved to a stable Junexis communication.</summary>
/// <param name="EventId">Trace event identifier.</param>
/// <param name="CommunicationId">Stable communication identifier.</param>
/// <param name="Kind">Junexis communication kind.</param>
/// <param name="PipelineSymbolId">Stable containing pipeline identifier.</param>
/// <param name="SourceSymbolId">Stable source task identifier.</param>
/// <param name="TargetSymbolId">Stable target task identifier.</param>
/// <param name="Source">Optional Junexis source location.</param>
public sealed record WhyCommunicationResolution(
    ulong EventId,
    string CommunicationId,
    string Kind,
    string PipelineSymbolId,
    string SourceSymbolId,
    string TargetSymbolId,
    AnalysisSourceLocation? Source);

/// <summary>
/// Wynik zapytania <c>why</c> do TraceCollector.
/// </summary>
/// <param name="TargetEventId">Id zdarzenia, dla którego pobrano łańcuch.</param>
/// <param name="CausalChain">Łańcuch zdarzeń prowadzących do celu.</param>
/// <param name="Depth">Głębokość zwróconego łańcucha.</param>
public sealed record WhyTraceResult(
    ulong TargetEventId,
    IReadOnlyList<WhyTraceChainEntry> CausalChain,
    int Depth)
{
    /// <summary>Stable Junexis communications resolved for the target event.</summary>
    public IReadOnlyList<WhyCommunicationResolution> Communications { get; init; } = [];
}

/// <summary>Stopped debugger frame submitted for Narrative correlation and optional WHY loading.</summary>
/// <param name="Frame">Mapped debugger frame carrying stable Junexis evidence.</param>
/// <param name="LoadWhy">Whether a successful correlation should immediately load the causal chain.</param>
public sealed record WhyForStoppedFrameRequested(DebugStackFrame Frame, bool LoadWhy = true);

/// <summary>Evidence sent to TraceCollector to correlate a stopped frame with a captured event.</summary>
/// <param name="SymbolId">Stable Junexis symbol identifier.</param>
/// <param name="SourcePath">Mapped Junexis source path.</param>
/// <param name="Line">One-based source line.</param>
/// <param name="BuildId">Optional active compiler build identity.</param>
/// <param name="NarrativeId">Optional Narrative filter.</param>
/// <param name="EventKind">Optional runtime event-kind filter.</param>
public sealed record WhyDebugCorrelationRequest(
    string? SymbolId,
    string? SourcePath,
    int? Line,
    string? BuildId = null,
    string? NarrativeId = null,
    string? EventKind = null)
{
    /// <summary>Optional verified System Bundle identity used to reject events from another deployment.</summary>
    public string? RuntimeBundleId { get; init; }
}


/// <summary>Newest runtime event selected by explicit Narrative filters.</summary>
/// <param name="EventId">Collector event identifier.</param>
/// <param name="Kind">Runtime event kind.</param>
/// <param name="NarrativeId">Optional Narrative identity.</param>
/// <param name="BuildId">Optional compiler build identity.</param>
public sealed record NarrativeEventSelection(
    ulong EventId,
    string Kind,
    string? NarrativeId,
    string? BuildId)
{
    /// <summary>Optional verified System Bundle identity.</summary>
    public string? RuntimeBundleId { get; init; }
}

/// <summary>Deterministic difference between a pinned and the currently selected Diagnostic Story.</summary>
/// <param name="PinnedEventId">Pinned baseline event.</param>
/// <param name="CurrentEventId">Current comparison event.</param>
/// <param name="SameBuild">Whether both stories carry the same complete runtime Build ID / Bundle ID identity.</param>
/// <param name="Summary">Compact evidence-based comparison summary.</param>
/// <param name="AddedEvidence">Evidence present only in the current story.</param>
/// <param name="RemovedEvidence">Evidence present only in the pinned story.</param>
/// <param name="TimelineDelta">Current minus pinned timeline size.</param>
/// <param name="CausalChainDelta">Current minus pinned causal-chain size.</param>
/// <param name="ConcurrentEventsDelta">Current minus pinned concurrent-event size.</param>
/// <param name="CommunicationsDelta">Current minus pinned communication count.</param>
public sealed record DiagnosticStoryComparison(
    ulong PinnedEventId,
    ulong CurrentEventId,
    bool SameBuild,
    string Summary,
    IReadOnlyList<string> AddedEvidence,
    IReadOnlyList<string> RemovedEvidence,
    int TimelineDelta,
    int CausalChainDelta,
    int ConcurrentEventsDelta,
    int CommunicationsDelta);

/// <summary>Deterministic difference between imported evidence and a captured live Story.</summary>
/// <param name="ImportedEventId">Imported Story event identifier.</param>
/// <param name="LiveEventId">Live Story event identifier captured before import.</param>
/// <param name="SameBuild">Whether both Stories carry the same non-empty build identity.</param>
/// <param name="Summary">Compact provenance-aware comparison summary.</param>
/// <param name="AddedInLive">Evidence present only in the live Story.</param>
/// <param name="MissingFromLive">Evidence present only in the imported Story.</param>
/// <param name="TimelineDelta">Live minus imported timeline size.</param>
/// <param name="CausalChainDelta">Live minus imported causal-chain size.</param>
/// <param name="ConcurrentEventsDelta">Live minus imported concurrent-event size.</param>
/// <param name="CommunicationsDelta">Live minus imported communication count.</param>
public sealed record DiagnosticSessionCrossComparison(
    ulong ImportedEventId,
    ulong LiveEventId,
    bool SameBuild,
    string Summary,
    IReadOnlyList<string> AddedInLive,
    IReadOnlyList<string> MissingFromLive,
    int TimelineDelta,
    int CausalChainDelta,
    int ConcurrentEventsDelta,
    int CommunicationsDelta);

/// <summary>Versioned Narrative correlation result returned for a stopped debugger frame.</summary>
/// <param name="SchemaVersion">Correlation schema version.</param>
/// <param name="Matched">Whether deterministic evidence selected an event.</param>
/// <param name="EventId">Selected event identifier.</param>
/// <param name="TaskId">Resolved runtime task identifier.</param>
/// <param name="SymbolId">Resolved stable Junexis symbol.</param>
/// <param name="Confidence">Evidence class returned by Narrative.</param>
/// <param name="NarrativeId">Optional narrative context.</param>
/// <param name="Evidence">Deterministic matching explanation.</param>
public sealed record WhyDebugCorrelationResult(
    string SchemaVersion,
    bool Matched,
    ulong? EventId,
    uint? TaskId,
    string? SymbolId,
    string Confidence,
    string? NarrativeId,
    IReadOnlyList<string> Evidence);

/// <summary>A trace event presented as evidence in a Diagnostic Story.</summary>
/// <param name="EventId">Trace event identifier.</param>
/// <param name="TaskId">Runtime task identifier.</param>
/// <param name="Kind">Trace event kind.</param>
/// <param name="LamportClock">Normalized Lamport clock.</param>
/// <param name="Timestamp">Captured timestamp.</param>
/// <param name="File">Optional source file.</param>
/// <param name="Line">Optional source line.</param>
/// <param name="Function">Optional runtime function.</param>
/// <param name="NarrativeId">Optional narrative identifier.</param>
public sealed record DiagnosticStoryEvent(
    ulong EventId,
    uint TaskId,
    string Kind,
    long LamportClock,
    string Timestamp,
    string? File,
    int? Line,
    string? Function,
    string? NarrativeId);

/// <summary>Evidence-backed diagnostic view assembled by Narrative and enriched by Mentor.</summary>
/// <param name="SchemaVersion">Story schema version.</param>
/// <param name="TargetEventId">Event being explained.</param>
/// <param name="TargetSymbolId">Stable Junexis task symbol.</param>
/// <param name="TargetDisplayName">Human-facing task name.</param>
/// <param name="TaskResolved">Whether the task was resolved through the debug manifest.</param>
/// <param name="NarrativeId">Optional narrative context.</param>
/// <param name="Timeline">Bounded task timeline.</param>
/// <param name="CausalChain">Happens-before chain.</param>
/// <param name="ConcurrentEvents">Vector-clock-proven concurrent events.</param>
/// <param name="Communications">Stable Junexis communications in the causal evidence.</param>
/// <param name="Evidence">Deterministic assembly evidence.</param>
public sealed record DiagnosticStoryResult(
    string SchemaVersion,
    ulong TargetEventId,
    string TargetSymbolId,
    string TargetDisplayName,
    bool TaskResolved,
    string? NarrativeId,
    IReadOnlyList<DiagnosticStoryEvent> Timeline,
    IReadOnlyList<DiagnosticStoryEvent> CausalChain,
    IReadOnlyList<DiagnosticStoryEvent> ConcurrentEvents,
    IReadOnlyList<WhyCommunicationResolution> Communications,
    IReadOnlyList<string> Evidence)
{
    /// <summary>Compiler build identity captured by Narrative on the target runtime event.</summary>
    public string? BuildId { get; init; }

    /// <summary>Verified System Bundle identity captured by Narrative on the target runtime event.</summary>
    public string? RuntimeBundleId { get; init; }

    /// <summary>Mentor findings grounded in stable symbols present in this story.</summary>
    public IReadOnlyList<MentorFinding> MentorFindings { get; init; } = [];
}

/// <summary>
/// Kontrakt dla klienta pobierającego łańcuch przyczynowy z TraceCollector.
/// </summary>
public interface IWhyTraceQueryService
{
    /// <summary>
    /// Pobiera wynik <c>why</c> dla wskazanego zdarzenia.
    /// </summary>
    /// <param name="baseUrl">Bazowy adres TraceCollector, np. <c>http://localhost:8080</c>.</param>
    /// <param name="eventId">Id zdarzenia docelowego.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    /// <returns>Wynik zawierający łańcuch przyczynowy.</returns>
    Task<WhyTraceResult> GetWhyAsync(string baseUrl, ulong eventId, CancellationToken cancellationToken = default);

    /// <summary>Correlates a mapped debugger frame with a captured Narrative event.</summary>
    /// <param name="baseUrl">TraceCollector base URL.</param>
    /// <param name="request">Stable symbol and source evidence.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Versioned correlation result; unmatched by default for legacy clients.</returns>
    Task<WhyDebugCorrelationResult> CorrelateAsync(
        string baseUrl,
        WhyDebugCorrelationRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(
            new WhyDebugCorrelationResult("1.0", false, null, null, null, "none", null,
                ["Trace correlation is unavailable."]));

    /// <summary>Returns the newest event matching the active runtime filters.</summary>
    /// <param name="baseUrl">TraceCollector base URL.</param>
    /// <param name="buildId">Optional compiler build identity.</param>
    /// <param name="narrativeId">Optional Narrative identity.</param>
    /// <param name="runtimeBundleId">Optional verified System Bundle identity.</param>
    /// <param name="eventKind">Optional event kind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<NarrativeEventSelection?> GetLatestEventAsync(
        string baseUrl,
        string? buildId,
        string? runtimeBundleId,
        string? narrativeId,
        string? eventKind,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<NarrativeEventSelection?>(null);

    /// <summary>Returns the nearest filtered event before or after an anchor.</summary>
    /// <param name="baseUrl">TraceCollector base URL.</param>
    /// <param name="eventId">Anchor event identifier.</param>
    /// <param name="previous">Selects the previous event when true, otherwise the next event.</param>
    /// <param name="buildId">Optional compiler build identity.</param>
    /// <param name="narrativeId">Optional Narrative identity.</param>
    /// <param name="runtimeBundleId">Optional verified System Bundle identity.</param>
    /// <param name="eventKind">Optional event kind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<NarrativeEventSelection?> GetAdjacentEventAsync(
        string baseUrl,
        ulong eventId,
        bool previous,
        string? buildId,
        string? runtimeBundleId,
        string? narrativeId,
        string? eventKind,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<NarrativeEventSelection?>(null);

    /// <summary>Loads a versioned Narrative Diagnostic Story for an event.</summary>
    /// <param name="baseUrl">TraceCollector base URL.</param>
    /// <param name="eventId">Target trace event identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evidence-backed story.</returns>
    async Task<DiagnosticStoryResult> GetDiagnosticStoryAsync(
        string baseUrl,
        ulong eventId,
        CancellationToken cancellationToken = default)
    {
        var why = await GetWhyAsync(baseUrl, eventId, cancellationToken).ConfigureAwait(false);
        return new DiagnosticStoryResult(
            "1.0", why.TargetEventId, string.Empty, $"Event {why.TargetEventId}", false, null, [],
            why.CausalChain.Select(item => new DiagnosticStoryEvent(
                item.EventId, item.TaskId, item.Kind, item.LamportClock, item.Timestamp,
                null, null, null, item.NarrativeId)).ToArray(),
            [], why.Communications, ["Legacy WHY response; timeline and concurrency evidence are unavailable."]);
    }
}

/// <summary>
/// View model panelu <c>Why?</c> dla TraceCollector.
/// </summary>
public sealed class WhyPanelViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IWhyTraceQueryService _service;
    private readonly IOutput _output;
    private readonly IEventBus? _eventBus;
    private readonly IDebugSessionService? _debugSession;
    private readonly INarrativeSessionService? _narrativeSession;
    private readonly IWorkspace? _workspace;
    private readonly NarrativeDiagnosticTrustStore _diagnosticTrustStore;
    private readonly SynchronizationContext? _synchronizationContext;
    private string _baseUrl = "http://localhost:8080";
    private string _eventIdText = string.Empty;
    private string _statusText = "Enter an event ID and click Load.";
    private IReadOnlyList<WhyTraceChainEntry> _causalChain = Array.Empty<WhyTraceChainEntry>();
    private IReadOnlyList<WhyCommunicationResolution> _communications = [];
    private bool _isBusy;
    private string _debugCorrelationText = "Stop at a mapped frame to correlate it with Narrative.";
    private WhyDebugCorrelationResult? _debugCorrelation;
    private DiagnosticStoryResult? _diagnosticStory;
    private DiagnosticStoryResult? _pinnedDiagnosticStory;
    private DiagnosticStoryComparison? _storyComparison;
    private string _pinnedStoryText = "No Diagnostic Story is pinned.";
    private IReadOnlyList<NarrativeStoryBookmark> _bookmarks = [];
    private NarrativeStoryBookmark? _selectedBookmark;
    private string _bookmarkNameText = string.Empty;
    private ImportedNarrativeDiagnosticSession? _importedSession;
    private string _diagnosticSessionModeText = "Workspace session · editable";
    private string _importedSessionSummaryText =
        "Import a diagnostic bundle to inspect its verified provenance.";
    private string _liveComparisonCandidateText =
        "Load a live Diagnostic Story before importing a bundle to compare both.";
    private IReadOnlyList<NarrativeDiagnosticEvidenceFile> _importedEvidenceFiles = [];
    private DiagnosticStoryResult? _liveStoryBeforeImport;
    private DiagnosticSessionCrossComparison? _crossSessionComparison;
    private string _diagnosticAuthenticityText =
        "Authenticity · no imported bundle";
    private NarrativeDiagnosticReviewDocument? _diagnosticReview;
    private IReadOnlyList<NarrativeDiagnosticReviewEntry> _reviewEntries = [];
    private NarrativeDiagnosticReviewEntry? _selectedReviewEntry;
    private string _reviewAuthorText = string.Empty;
    private string _reviewText = string.Empty;
    private NarrativeDiagnosticReviewKind _reviewKind = NarrativeDiagnosticReviewKind.Observation;
    private NarrativeDiagnosticReviewStatus _reviewStatus = NarrativeDiagnosticReviewStatus.Open;
    private NarrativeDiagnosticEvidenceFile? _selectedReviewEvidenceFile;
    private string _reviewStateText = "Open a verified diagnostic bundle to begin a review.";
    private string _reviewSignatureText = "Review authenticity · unavailable";
    private NarrativeDiagnosticReviewComparison? _reviewComparison;
    private bool _reviewDirty;
    private string? _reviewPath;
    private byte[]? _diagnosticCaseBundle;
    private IReadOnlyList<NarrativeDiagnosticMentorProposal> _mentorReviewProposals = [];
    private NarrativeDiagnosticMentorProposal? _selectedMentorReviewProposal;
    private string _mentorReviewProposalText =
        "Mentor proposals are generated only from build-correlated findings captured in this Story.";
    private IReadOnlyList<DiagnosticStoryEvent> _timeline = [];
    private IReadOnlyList<DiagnosticStoryEvent> _concurrentEvents = [];
    private IReadOnlyList<string> _storyEvidence = [];
    private IReadOnlyList<MentorFinding> _mentorFindings = [];
    private string _mentorContextText = "Mentor · waiting for build-correlated runtime evidence.";
    private string _narrativeSessionText = "Narrative session is externally managed.";
    private string _narrativeActivityText = "Runtime activity · externally managed";
    private bool _autoFollowRuntime = true;
    private bool _runtimeFollowPaused;
    private string _narrativeFilterText = string.Empty;
    private string _eventKindFilterText = string.Empty;
    private string? _observedManagedBuildId;
    private ulong? _observedManagedEventId;
    private int _autoFollowVersion;
    private readonly CancellationTokenSource _autoFollowCancellation = new();
    private CancellationTokenSource? _autoFollowRequestCancellation;
    private CancellationTokenSource? _correlationRequestCancellation;
    private bool _disposed;
    private readonly IMentorAnalysisService? _mentor;
    private string? _analysisSnapshotPath;
    private string? _analysisBuildId;
    private int _correlationVersion;

    /// <summary>
    /// Tworzy view model z wstrzykiwanym klientem zapytań.
    /// </summary>
    /// <param name="service">Serwis pobierający dane z TraceCollector.</param>
    /// <param name="output">Kanał logów Studio.</param>
    /// <param name="eventBus">Opcjonalna magistrala nawigacji do źródła.</param>
    /// <param name="debugSession">Optional DAP session observed for automatic stopped-frame correlation.</param>
    /// <param name="mentor">Optional deterministic Mentor analysis service.</param>
    /// <param name="narrativeSession">Optional Studio-managed TraceCollector session.</param>
    /// <param name="workspace">Optional workspace used for persistent diagnostic bookmarks.</param>
    /// <param name="diagnosticTrustStorePath">Optional local signer trust-store path, primarily for isolated hosts and tests.</param>
    public WhyPanelViewModel(
        IWhyTraceQueryService service,
        IOutput output,
        IEventBus? eventBus = null,
        IDebugSessionService? debugSession = null,
        IMentorAnalysisService? mentor = null,
        INarrativeSessionService? narrativeSession = null,
        IWorkspace? workspace = null,
        string? diagnosticTrustStorePath = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _eventBus = eventBus;
        _debugSession = debugSession;
        _mentor = mentor;
        _narrativeSession = narrativeSession;
        _workspace = workspace;
        _diagnosticTrustStore = new NarrativeDiagnosticTrustStore(diagnosticTrustStorePath);
        _synchronizationContext = SynchronizationContext.Current;
        if (_debugSession is not null)
            _debugSession.SnapshotChanged += OnDebugSnapshotChanged;
        _eventBus?.Subscribe<WhyForStoppedFrameRequested>(OnWhyForStoppedFrameRequested);
        _eventBus?.Subscribe<AnalysisSnapshotUpdatedEvent>(OnAnalysisSnapshotUpdated);
        if (_workspace is not null)
        {
            _workspace.FolderOpened += OnWorkspaceOpened;
            ReloadBookmarks();
        }
        if (_narrativeSession is not null)
        {
            _narrativeSession.SnapshotChanged += OnNarrativeSessionChanged;
            ApplyNarrativeSession(_narrativeSession.Snapshot, allowAutoFollow: false);
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Bazowy adres TraceCollector.
    /// </summary>
    public string BaseUrl
    {
        get => _baseUrl;
        set => SetField(ref _baseUrl, value);
    }

    /// <summary>
    /// Tekstowe pole z id zdarzenia.
    /// </summary>
    public string EventIdText
    {
        get => _eventIdText;
        set => SetField(ref _eventIdText, value);
    }

    /// <summary>
    /// Aktualny status panelu.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>
    /// Zwrócony łańcuch przyczynowy.
    /// </summary>
    public IReadOnlyList<WhyTraceChainEntry> CausalChain
    {
        get => _causalChain;
        private set => SetField(ref _causalChain, value);
    }

    /// <summary>Stable Junexis communications resolved for the selected trace event.</summary>
    public IReadOnlyList<WhyCommunicationResolution> Communications
    {
        get => _communications;
        private set => SetField(ref _communications, value);
    }

    /// <summary>Latest complete Diagnostic Story.</summary>
    public DiagnosticStoryResult? DiagnosticStory
    {
        get => _diagnosticStory;
        private set
        {
            if (SetField(ref _diagnosticStory, value))
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(CanGenerateMentorReviewProposals)));
        }
    }

    /// <summary>Stable baseline Story retained while the live debugger and auto-follow continue.</summary>
    public DiagnosticStoryResult? PinnedDiagnosticStory
    {
        get => _pinnedDiagnosticStory;
        private set => SetField(ref _pinnedDiagnosticStory, value);
    }

    /// <summary>Latest deterministic comparison between the pinned and current stories.</summary>
    public DiagnosticStoryComparison? StoryComparison
    {
        get => _storyComparison;
        private set => SetField(ref _storyComparison, value);
    }

    /// <summary>Human-readable pinned Story state.</summary>
    public string PinnedStoryText
    {
        get => _pinnedStoryText;
        private set => SetField(ref _pinnedStoryText, value);
    }

    /// <summary>Workspace-persistent named Diagnostic Story bookmarks.</summary>
    public IReadOnlyList<NarrativeStoryBookmark> Bookmarks
    {
        get => _bookmarks;
        private set => SetField(ref _bookmarks, value);
    }

    /// <summary>Bookmark selected for opening or removal.</summary>
    public NarrativeStoryBookmark? SelectedBookmark
    {
        get => _selectedBookmark;
        set => SetField(ref _selectedBookmark, value);
    }

    /// <summary>Name entered for a new or replacement bookmark.</summary>
    public string BookmarkNameText
    {
        get => _bookmarkNameText;
        set => SetField(ref _bookmarkNameText, value);
    }

    /// <summary>Verified external bundle currently presented in read-only mode.</summary>
    public ImportedNarrativeDiagnosticSession? ImportedSession
    {
        get => _importedSession;
        private set
        {
            if (!SetField(ref _importedSession, value))
                return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEditBookmarks)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCloseImportedSession)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCompareImportedWithLive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanTrustImportedSigner)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRemoveImportedSignerTrust)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEditReview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExportDiagnosticCase)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CanGenerateMentorReviewProposals)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CanActOnMentorReviewProposal)));
        }
    }

    /// <summary>Human-readable persistence and mutability mode of the diagnostic session.</summary>
    public string DiagnosticSessionModeText
    {
        get => _diagnosticSessionModeText;
        private set => SetField(ref _diagnosticSessionModeText, value);
    }

    /// <summary>Verified bundle provenance and captured content summary.</summary>
    public string ImportedSessionSummaryText
    {
        get => _importedSessionSummaryText;
        private set => SetField(ref _importedSessionSummaryText, value);
    }

    /// <summary>Live Story retained as an explicit comparison candidate during import.</summary>
    public string LiveComparisonCandidateText
    {
        get => _liveComparisonCandidateText;
        private set => SetField(ref _liveComparisonCandidateText, value);
    }

    /// <summary>Manifest entries whose bytes passed SHA-256 verification.</summary>
    public IReadOnlyList<NarrativeDiagnosticEvidenceFile> ImportedEvidenceFiles
    {
        get => _importedEvidenceFiles;
        private set => SetField(ref _importedEvidenceFiles, value);
    }

    /// <summary>Latest imported-versus-live evidence comparison.</summary>
    public DiagnosticSessionCrossComparison? CrossSessionComparison
    {
        get => _crossSessionComparison;
        private set => SetField(ref _crossSessionComparison, value);
    }

    /// <summary>Independent signature verification and local signer-trust explanation.</summary>
    public string DiagnosticAuthenticityText
    {
        get => _diagnosticAuthenticityText;
        private set => SetField(ref _diagnosticAuthenticityText, value);
    }

    /// <summary>Current editable semantic review bound to the imported Bundle ID.</summary>
    public NarrativeDiagnosticReviewDocument? DiagnosticReview
    {
        get => _diagnosticReview;
        private set => SetField(ref _diagnosticReview, value);
    }

    /// <summary>Current working review statements.</summary>
    public IReadOnlyList<NarrativeDiagnosticReviewEntry> ReviewEntries
    {
        get => _reviewEntries;
        private set => SetField(ref _reviewEntries, value);
    }

    /// <summary>Review statement selected for removal.</summary>
    public NarrativeDiagnosticReviewEntry? SelectedReviewEntry
    {
        get => _selectedReviewEntry;
        set => SetField(ref _selectedReviewEntry, value);
    }

    /// <summary>Declared author of a new review statement or saved revision.</summary>
    public string ReviewAuthorText
    {
        get => _reviewAuthorText;
        set => SetField(ref _reviewAuthorText, value);
    }

    /// <summary>Text of the next review statement.</summary>
    public string ReviewText
    {
        get => _reviewText;
        set => SetField(ref _reviewText, value);
    }

    /// <summary>Semantic role of the next review statement.</summary>
    public NarrativeDiagnosticReviewKind ReviewKind
    {
        get => _reviewKind;
        set => SetField(ref _reviewKind, value);
    }

    /// <summary>Available semantic review roles.</summary>
    public IReadOnlyList<NarrativeDiagnosticReviewKind> ReviewKinds { get; } =
        Enum.GetValues<NarrativeDiagnosticReviewKind>();

    /// <summary>Current diagnostic case status.</summary>
    public NarrativeDiagnosticReviewStatus ReviewStatus
    {
        get => _reviewStatus;
        set
        {
            if (SetField(ref _reviewStatus, value) && DiagnosticReview is not null)
                MarkReviewChanged();
        }
    }

    /// <summary>Available diagnostic case states.</summary>
    public IReadOnlyList<NarrativeDiagnosticReviewStatus> ReviewStatuses { get; } =
        Enum.GetValues<NarrativeDiagnosticReviewStatus>();

    /// <summary>Verified evidence file referenced by the next review statement.</summary>
    public NarrativeDiagnosticEvidenceFile? SelectedReviewEvidenceFile
    {
        get => _selectedReviewEvidenceFile;
        set => SetField(ref _selectedReviewEvidenceFile, value);
    }

    /// <summary>Human-readable review persistence and revision state.</summary>
    public string ReviewStateText
    {
        get => _reviewStateText;
        private set => SetField(ref _reviewStateText, value);
    }

    /// <summary>Independent signature state of the editable review layer.</summary>
    public string ReviewSignatureText
    {
        get => _reviewSignatureText;
        private set => SetField(ref _reviewSignatureText, value);
    }

    /// <summary>Difference between the two latest saved review revisions.</summary>
    public NarrativeDiagnosticReviewComparison? ReviewComparison
    {
        get => _reviewComparison;
        private set => SetField(ref _reviewComparison, value);
    }

    /// <summary>Whether a review can be edited for the imported immutable bundle.</summary>
    public bool CanEditReview => ImportedSession is not null;

    /// <summary>Whether the current review can be exported as a complete case.</summary>
    public bool CanExportDiagnosticCase =>
        ImportedSession is not null
        && DiagnosticReview?.Revisions.Count > 0
        && !_reviewDirty;

    /// <summary>Non-binding Mentor statements awaiting an explicit human decision.</summary>
    public IReadOnlyList<NarrativeDiagnosticMentorProposal> MentorReviewProposals
    {
        get => _mentorReviewProposals;
        private set => SetField(ref _mentorReviewProposals, value);
    }

    /// <summary>Mentor proposal selected for explicit acceptance or dismissal.</summary>
    public NarrativeDiagnosticMentorProposal? SelectedMentorReviewProposal
    {
        get => _selectedMentorReviewProposal;
        set
        {
            if (SetField(ref _selectedMentorReviewProposal, value))
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(CanActOnMentorReviewProposal)));
        }
    }

    /// <summary>Explains proposal grounding and the current queue state.</summary>
    public string MentorReviewProposalText
    {
        get => _mentorReviewProposalText;
        private set => SetField(ref _mentorReviewProposalText, value);
    }

    /// <summary>Whether the imported Story contains findings grounded in the exact bundle build.</summary>
    public bool CanGenerateMentorReviewProposals =>
        ImportedSession is not null
        && DiagnosticStory is { MentorFindings.Count: > 0, BuildId: { Length: > 0 } buildId }
        && string.Equals(buildId, ImportedSession.BuildId, StringComparison.Ordinal);

    /// <summary>Whether one visible Mentor proposal can receive an explicit human decision.</summary>
    public bool CanActOnMentorReviewProposal =>
        CanEditReview && SelectedMentorReviewProposal is not null;

    /// <summary>Whether bookmark mutations target the currently open workspace.</summary>
    public bool CanEditBookmarks => ImportedSession is null;

    /// <summary>Whether a verified imported session can be closed.</summary>
    public bool CanCloseImportedSession => ImportedSession is not null;

    /// <summary>Whether verified imported evidence and a pre-import live Story are both available.</summary>
    public bool CanCompareImportedWithLive =>
        ImportedSession is not null && _liveStoryBeforeImport is not null;

    /// <summary>Whether the valid imported signer can be added to the local trust store.</summary>
    public bool CanTrustImportedSigner =>
        ImportedSession?.Signature.Status
        == NarrativeDiagnosticAuthenticityStatus.SignatureVerified;

    /// <summary>Whether local trust can be removed for the imported signer.</summary>
    public bool CanRemoveImportedSignerTrust =>
        ImportedSession?.Signature.Status
        == NarrativeDiagnosticAuthenticityStatus.TrustedSigner;

    /// <summary>Bounded target-task timeline around the selected event.</summary>
    public IReadOnlyList<DiagnosticStoryEvent> Timeline
    {
        get => _timeline;
        private set => SetField(ref _timeline, value);
    }

    /// <summary>Events proven concurrent with the selected event.</summary>
    public IReadOnlyList<DiagnosticStoryEvent> ConcurrentEvents
    {
        get => _concurrentEvents;
        private set => SetField(ref _concurrentEvents, value);
    }

    /// <summary>Deterministic evidence statements supplied by Narrative.</summary>
    public IReadOnlyList<string> StoryEvidence
    {
        get => _storyEvidence;
        private set => SetField(ref _storyEvidence, value);
    }

    /// <summary>Mentor findings grounded in stable symbols present in the story.</summary>
    public IReadOnlyList<MentorFinding> MentorFindings
    {
        get => _mentorFindings;
        private set => SetField(ref _mentorFindings, value);
    }

    /// <summary>Explains whether and why Mentor analysis was joined with runtime evidence.</summary>
    public string MentorContextText
    {
        get => _mentorContextText;
        private set => SetField(ref _mentorContextText, value);
    }

    /// <summary>Current status of the Studio-managed TraceCollector session.</summary>
    public string NarrativeSessionText
    {
        get => _narrativeSessionText;
        private set => SetField(ref _narrativeSessionText, value);
    }

    /// <summary>Live runtime activity observed by the managed Narrative monitor.</summary>
    public string NarrativeActivityText
    {
        get => _narrativeActivityText;
        private set => SetField(ref _narrativeActivityText, value);
    }

    /// <summary>Whether a newly observed managed event automatically loads its Diagnostic Story.</summary>
    public bool AutoFollowRuntime
    {
        get => _autoFollowRuntime;
        set
        {
            if (SetField(ref _autoFollowRuntime, value) && !value)
                CancelAutoFollowRequest();
        }
    }

    /// <summary>Whether automatic runtime observation is temporarily paused without stopping capture.</summary>
    public bool RuntimeFollowPaused
    {
        get => _runtimeFollowPaused;
        private set => SetField(ref _runtimeFollowPaused, value);
    }

    /// <summary>Optional exact Narrative identifier used by auto-follow and stopped-frame correlation.</summary>
    public string NarrativeFilterText
    {
        get => _narrativeFilterText;
        set
        {
            if (SetField(ref _narrativeFilterText, value))
                OnRuntimeFilterChanged();
        }
    }

    /// <summary>Optional runtime event kind used by auto-follow and stopped-frame correlation.</summary>
    public string EventKindFilterText
    {
        get => _eventKindFilterText;
        set
        {
            if (SetField(ref _eventKindFilterText, value))
                OnRuntimeFilterChanged();
        }
    }

    /// <summary>
    /// Informuje, czy trwa pobieranie danych.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>
    /// Pobiera łańcuch <c>why</c> dla zdarzenia wskazanego przez użytkownika.
    /// </summary>
    /// <param name="cancellationToken">Token anulowania.</param>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            ClearStory();
            StatusText = "TraceCollector URL is required.";
            return;
        }

        if (!ulong.TryParse(EventIdText, out var eventId))
        {
            ClearStory();
            StatusText = "Event ID must be an unsigned integer.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = $"Loading Diagnostic Story for event {eventId}...";

            var story = await _service.GetDiagnosticStoryAsync(BaseUrl, eventId, cancellationToken);
            story = await EnrichWithMentorAsync(story, cancellationToken);
            if (ImportedSession is not null)
                LeaveImportedSession(clearStory: false);
            DiagnosticStory = story;
            StoryComparison = null;
            Timeline = story.Timeline;
            ConcurrentEvents = story.ConcurrentEvents;
            StoryEvidence = story.Evidence;
            MentorFindings = story.MentorFindings;
            CausalChain = story.CausalChain.Select(item => new WhyTraceChainEntry(
                item.EventId, item.TaskId, item.Kind, item.LamportClock, item.Timestamp, item.NarrativeId)).ToArray();
            Communications = story.Communications;
            _eventBus?.Publish(new DiagnosticStoryLoadedEvent(
                story.BuildId,
                story.TargetEventId,
                story.NarrativeId,
                story.MentorFindings.Count));
            StatusText = story.CausalChain.Count == 0
                ? $"No causal chain returned for event {eventId}."
                : Communications.Count == 0
                    ? $"Loaded {story.CausalChain.Count} event(s) for target {story.TargetEventId}."
                    : $"Loaded {story.CausalChain.Count} event(s) and {Communications.Count} communication resolution(s) for target {story.TargetEventId}.";

            _output.Write("Why", StatusText);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ClearStory();
            StatusText = "Why query cancelled.";
            _output.Write("Why", StatusText);
        }
        catch (Exception ex)
        {
            ClearStory();
            StatusText = $"Why query failed: {ex.Message}";
            _output.Write("Why", StatusText, OutputSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Restarts the managed Narrative session for the latest verified build.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RestartNarrativeAsync(CancellationToken cancellationToken = default)
    {
        if (_narrativeSession is null)
        {
            NarrativeSessionText = "Narrative session service is unavailable.";
            return;
        }
        await _narrativeSession.RestartAsync(cancellationToken);
    }

    /// <summary>Stops the Studio-managed Narrative session.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StopNarrativeAsync(CancellationToken cancellationToken = default)
    {
        if (_narrativeSession is null)
            return;
        await _narrativeSession.StopAsync(cancellationToken);
    }

    /// <summary>Refreshes managed Narrative health and selects its latest build event.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RefreshNarrativeAsync(CancellationToken cancellationToken = default)
    {
        if (_narrativeSession is null)
        {
            NarrativeSessionText = "Narrative session service is unavailable.";
            return;
        }
        await _narrativeSession.RefreshAsync(cancellationToken);
    }

    /// <summary>Pauses automatic Story selection while the Collector continues capturing events.</summary>
    public void PauseRuntimeFollow()
    {
        RuntimeFollowPaused = true;
        CancelAutoFollowRequest();
        NarrativeActivityText = "Runtime activity · follow paused · capture continues";
    }

    /// <summary>Resumes automatic Story selection and catches up to the newest matching event.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ResumeRuntimeFollowAsync(CancellationToken cancellationToken = default)
    {
        RuntimeFollowPaused = false;
        if (_narrativeSession?.Snapshot is
            {
                State: NarrativeSessionState.Ready,
                LatestEventId: { } latestEventId
            })
        {
            return StartFollowingManagedEvent(latestEventId, cancellationToken);
        }
        else
        {
            NarrativeActivityText = "Runtime activity · follow resumed · waiting for events";
        }
        return Task.CompletedTask;
    }

    /// <summary>Navigates to the nearest event matching the active build and runtime filters.</summary>
    /// <param name="previous">Selects the previous event when true, otherwise the next event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task NavigateHistoryAsync(
        bool previous,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            StatusText = "Wait for the current Diagnostic Story request before navigating history.";
            return;
        }
        if (!ulong.TryParse(EventIdText, out var anchorEventId))
        {
            StatusText = "Select a valid event before navigating Narrative history.";
            return;
        }

        try
        {
            var selection = await _service.GetAdjacentEventAsync(
                BaseUrl,
                anchorEventId,
                previous,
                _narrativeSession?.Snapshot.BuildId,
                DiagnosticStory?.RuntimeBundleId,
                NormalizeFilter(NarrativeFilterText),
                NormalizeFilter(EventKindFilterText),
                cancellationToken).ConfigureAwait(false);
            if (selection is null)
            {
                Update(() => StatusText = previous
                    ? "No previous event matches the active runtime filters."
                    : "No next event matches the active runtime filters.");
                return;
            }

            Update(() =>
            {
                PauseRuntimeFollow();
                EventIdText = selection.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
            await LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Update(() => StatusText = "Narrative history navigation cancelled.");
        }
        catch (Exception ex)
        {
            Update(() => StatusText = $"Narrative history navigation failed: {ex.Message}");
            _output.Write("Why", StatusText, OutputSeverity.Error);
        }
    }

    /// <summary>Pins the current Diagnostic Story as a stable comparison baseline.</summary>
    public void PinCurrentStory()
    {
        if (DiagnosticStory is null)
        {
            StatusText = "Load a Diagnostic Story before pinning it.";
            return;
        }
        PinnedDiagnosticStory = DiagnosticStory;
        StoryComparison = null;
        PinnedStoryText =
            $"Pinned event #{DiagnosticStory.TargetEventId} · build {ShortId(DiagnosticStory.BuildId ?? "unknown")} · " +
            $"bundle {ShortId(DiagnosticStory.RuntimeBundleId ?? "unknown")}";
    }

    /// <summary>Clears the pinned baseline without changing the current debugger context.</summary>
    public void ClearPinnedStory()
    {
        PinnedDiagnosticStory = null;
        StoryComparison = null;
        PinnedStoryText = "No Diagnostic Story is pinned.";
    }

    /// <summary>Compares the current Story with the pinned baseline.</summary>
    public void CompareWithPinnedStory()
    {
        if (PinnedDiagnosticStory is not { } pinned || DiagnosticStory is not { } current)
        {
            StatusText = "Pin one Story and load another before comparing them.";
            return;
        }

        var sameBuild = !string.IsNullOrWhiteSpace(pinned.BuildId)
            && string.Equals(pinned.BuildId, current.BuildId, StringComparison.Ordinal);
        var sameBundle = !string.IsNullOrWhiteSpace(pinned.RuntimeBundleId)
            && string.Equals(pinned.RuntimeBundleId, current.RuntimeBundleId, StringComparison.Ordinal);
        var addedEvidence = current.Evidence
            .Except(pinned.Evidence, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var removedEvidence = pinned.Evidence
            .Except(current.Evidence, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        StoryComparison = new DiagnosticStoryComparison(
            pinned.TargetEventId,
            current.TargetEventId,
            sameBuild && sameBundle,
            $"Event #{pinned.TargetEventId} → #{current.TargetEventId} · " +
            $"{(sameBuild && sameBundle ? "same runtime Build/Bundle identity" : "different or incomplete runtime identity")} · " +
            $"+{addedEvidence.Length}/-{removedEvidence.Length} evidence item(s)",
            addedEvidence,
            removedEvidence,
            current.Timeline.Count - pinned.Timeline.Count,
            current.CausalChain.Count - pinned.CausalChain.Count,
            current.ConcurrentEvents.Count - pinned.ConcurrentEvents.Count,
            current.Communications.Count - pinned.Communications.Count);
        StatusText = $"Compared event #{pinned.TargetEventId} with event #{current.TargetEventId}.";
    }

    /// <summary>Saves the current Story as a named workspace bookmark.</summary>
    public void SaveBookmark()
    {
        if (!CanEditBookmarks)
        {
            StatusText = "Imported diagnostic bundles are read-only.";
            return;
        }
        if (_workspace is null || string.IsNullOrWhiteSpace(_workspace.RootPath))
        {
            StatusText = "Open a workspace before creating persistent diagnostic bookmarks.";
            return;
        }
        if (DiagnosticStory is not { } story)
        {
            StatusText = "Load a Diagnostic Story before creating a bookmark.";
            return;
        }
        var name = BookmarkNameText.Trim();
        if (name.Length == 0)
        {
            StatusText = "Bookmark name is required.";
            return;
        }
        var existing = Bookmarks.FirstOrDefault(bookmark =>
            bookmark.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var bookmark = new NarrativeStoryBookmark(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            name,
            DateTimeOffset.UtcNow,
            story);
        Bookmarks =
        [
            bookmark,
            .. Bookmarks.Where(item => !item.Id.Equals(bookmark.Id, StringComparison.Ordinal))
        ];
        SelectedBookmark = bookmark;
        BookmarkNameText = string.Empty;
        if (SaveBookmarks())
            StatusText = $"Saved diagnostic bookmark '{name}' for event #{story.TargetEventId}.";
    }

    /// <summary>Opens the selected bookmark without requiring a running TraceCollector.</summary>
    public void OpenSelectedBookmark()
    {
        if (SelectedBookmark is not { } bookmark)
        {
            StatusText = "Select a diagnostic bookmark to open.";
            return;
        }
        PauseRuntimeFollow();
        PresentStoredStory(bookmark.Story);
        EventIdText = bookmark.Story.TargetEventId.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        StatusText =
            $"Opened diagnostic bookmark '{bookmark.Name}' for event #{bookmark.Story.TargetEventId}.";
    }

    /// <summary>Removes the selected bookmark from the current workspace.</summary>
    public void RemoveSelectedBookmark()
    {
        if (!CanEditBookmarks)
        {
            StatusText = "Imported diagnostic bundles are read-only.";
            return;
        }
        if (SelectedBookmark is not { } bookmark)
            return;
        Bookmarks = Bookmarks
            .Where(item => !item.Id.Equals(bookmark.Id, StringComparison.Ordinal))
            .ToArray();
        SelectedBookmark = null;
        if (SaveBookmarks())
            StatusText = $"Removed diagnostic bookmark '{bookmark.Name}'.";
    }

    /// <summary>Exports the complete diagnostic session as a checksummed ZIP bundle.</summary>
    /// <param name="path">Destination bundle path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExportDiagnosticSessionAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await ExportDiagnosticSessionCoreAsync(
            path,
            signingCredentials: null,
            cancellationToken).ConfigureAwait(false);
        _output.Write("Why", $"Diagnostic session bundle exported to {path}.");
    }

    /// <summary>Exports the diagnostic session with an ECDSA P-256 signature over its manifest.</summary>
    /// <param name="path">Destination bundle path.</param>
    /// <param name="privateKeyPath">PEM file containing an externally managed ECDSA private key.</param>
    /// <param name="signerName">Human-facing signer identity recorded in the signature claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExportSignedDiagnosticSessionAsync(
        string path,
        string privateKeyPath,
        string signerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(signerName);
            var keyFile = new FileInfo(Path.GetFullPath(privateKeyPath));
            if (!keyFile.Exists)
                throw new FileNotFoundException("Diagnostic signing key was not found.", keyFile.FullName);
            if (keyFile.Length > 256 * 1024)
                throw new InvalidDataException("Diagnostic signing key exceeds the 256 KiB safety limit.");
            var credentials = new NarrativeDiagnosticSigningCredentials(
                signerName.Trim(),
                await File.ReadAllTextAsync(keyFile.FullName, cancellationToken).ConfigureAwait(false));
            await ExportDiagnosticSessionCoreAsync(
                path,
                credentials,
                cancellationToken).ConfigureAwait(false);
            StatusText = $"Signed diagnostic bundle exported as '{credentials.SignerName}'.";
            _output.Write(
                "Why",
                $"Signed diagnostic session bundle exported to {path} as '{credentials.SignerName}'.");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Signed diagnostic bundle export cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Signed diagnostic bundle export failed: {ex.Message}";
            _output.Write("Why", StatusText, OutputSeverity.Error);
        }
    }

    private async Task ExportDiagnosticSessionCoreAsync(
        string path,
        NarrativeDiagnosticSigningCredentials? signingCredentials,
        CancellationToken cancellationToken)
    {
        if (DiagnosticStory is null && PinnedDiagnosticStory is null && Bookmarks.Count == 0)
            throw new InvalidOperationException(
                "Load or bookmark Diagnostic Story evidence before exporting a session.");
        await NarrativeDiagnosticSessionStore.ExportBundleAsync(
            path,
            _workspace,
            DiagnosticStory,
            PinnedDiagnosticStory,
            StoryComparison,
            DebugCorrelation,
            Bookmarks,
            NarrativeFilterText,
            EventKindFilterText,
            cancellationToken,
            signingCredentials).ConfigureAwait(false);
    }

    /// <summary>Imports and verifies a diagnostic ZIP bundle before opening it read-only.</summary>
    /// <param name="path">Source bundle path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ImportDiagnosticSessionAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            throw new InvalidOperationException("Wait for the current Diagnostic Story request.");
        IsBusy = true;
        StatusText = "Verifying diagnostic session bundle...";
        try
        {
            var imported = await NarrativeDiagnosticSessionStore.ImportBundleAsync(
                path,
                cancellationToken,
                _diagnosticTrustStore).ConfigureAwait(false);
            var review = NarrativeDiagnosticReviewStore.Load(
                NarrativeDiagnosticReviewStore.SidecarPath(imported.SourcePath),
                imported.BundleId,
                _diagnosticTrustStore);
            Update(() =>
            {
                ApplyImportedSession(imported);
                ApplyReview(
                    review.Document,
                    review.Signature,
                    NarrativeDiagnosticReviewStore.SidecarPath(imported.SourcePath));
                _diagnosticCaseBundle = null;
            });
            _output.Write(
                "Why",
                $"Verified {imported.VerifiedFileCount} diagnostic evidence file(s) from {path}.");
        }
        catch (OperationCanceledException)
        {
            Update(() => StatusText = "Diagnostic session import cancelled.");
        }
        catch (Exception ex)
        {
            Update(() => StatusText = $"Diagnostic session import rejected: {ex.Message}");
            _output.Write(
                "Why",
                $"Diagnostic session import rejected: {ex.Message}",
                OutputSeverity.Error);
        }
        finally
        {
            Update(() => IsBusy = false);
        }
    }

    /// <summary>Adds a semantic statement linked to the currently presented evidence.</summary>
    public void AddReviewEntry()
    {
        if (ImportedSession is null || DiagnosticReview is null)
        {
            StatusText = "Open a verified diagnostic bundle before reviewing it.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ReviewAuthorText)
            || string.IsNullOrWhiteSpace(ReviewText))
        {
            StatusText = "Review author and statement text are required.";
            return;
        }
        var story = DiagnosticStory;
        var entry = new NarrativeDiagnosticReviewEntry(
            Guid.NewGuid().ToString("N"),
            ReviewKind,
            ReviewAuthorText.Trim(),
            DateTimeOffset.UtcNow,
            ReviewText.Trim(),
            story?.TargetEventId,
            story?.TargetSymbolId,
            SelectedReviewEvidenceFile?.Path);
        ReviewEntries = [.. ReviewEntries, entry];
        SelectedReviewEntry = entry;
        ReviewText = string.Empty;
        MarkReviewChanged();
        StatusText = $"Added {entry.Kind.ToString().ToLowerInvariant()} to the working review.";
    }

    /// <summary>Removes the selected working review statement.</summary>
    public void RemoveSelectedReviewEntry()
    {
        if (!CanEditReview || SelectedReviewEntry is not { } selected)
            return;
        ReviewEntries = ReviewEntries.Where(item => item.Id != selected.Id).ToArray();
        SelectedReviewEntry = null;
        MarkReviewChanged();
        StatusText = "Removed the selected statement from the working review.";
    }

    /// <summary>Saves a complete immutable snapshot as the next review revision.</summary>
    public void SaveReviewRevision()
    {
        if (ImportedSession is null || DiagnosticReview is null || _reviewPath is null)
        {
            StatusText = "Open a verified diagnostic bundle before saving a review.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ReviewAuthorText))
        {
            StatusText = "Review author is required to save a revision.";
            return;
        }
        try
        {
            var revision = new NarrativeDiagnosticReviewRevision(
                DiagnosticReview.Revisions.Count + 1,
                DateTimeOffset.UtcNow,
                ReviewAuthorText.Trim(),
                ReviewStatus,
                ReviewEntries.ToArray());
            var document = DiagnosticReview with
            {
                Status = ReviewStatus,
                Entries = ReviewEntries.ToArray(),
                Revisions = [.. DiagnosticReview.Revisions, revision],
                Signature = null
            };
            NarrativeDiagnosticReviewStore.Save(_reviewPath, document);
            ApplyReview(
                document,
                NarrativeDiagnosticReviewStore.Verify(document, _diagnosticTrustStore),
                _reviewPath);
            StatusText = $"Saved diagnostic review revision r{revision.Number}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Cannot save diagnostic review: {ex.Message}";
            _output.Write("Why", StatusText, OutputSeverity.Error);
        }
    }

    /// <summary>Compares the two latest saved review snapshots.</summary>
    public void CompareLatestReviewRevisions()
    {
        if (DiagnosticReview is null)
            return;
        ReviewComparison = NarrativeDiagnosticReviewStore.CompareLatest(DiagnosticReview);
        StatusText = ReviewComparison?.Summary
            ?? "Save at least two review revisions before comparing them.";
    }

    /// <summary>Builds non-binding Mentor proposals from findings grounded in the imported Story.</summary>
    public void GenerateMentorReviewProposals()
    {
        if (!CanGenerateMentorReviewProposals
            || ImportedSession is null
            || DiagnosticStory is not { BuildId: { } buildId } story)
        {
            MentorReviewProposals = [];
            SelectedMentorReviewProposal = null;
            MentorReviewProposalText =
                "Mentor proposals unavailable: the Story has no findings grounded in the exact bundle build.";
            StatusText = MentorReviewProposalText;
            return;
        }
        var stableIds = new HashSet<string>(StringComparer.Ordinal) { story.TargetSymbolId };
        foreach (var communication in story.Communications)
        {
            stableIds.Add(communication.CommunicationId);
            stableIds.Add(communication.PipelineSymbolId);
            stableIds.Add(communication.SourceSymbolId);
            stableIds.Add(communication.TargetSymbolId);
        }
        var evidencePath = ResolvePresentedStoryEvidencePath(ImportedSession, story);
        var acceptedReferences = new HashSet<string>(
            ReviewEntries
                .Where(entry => entry.Source == NarrativeDiagnosticReviewSource.Mentor)
                .Select(entry => entry.SourceReference)
                .OfType<string>(),
            StringComparer.Ordinal);
        var proposals = new List<NarrativeDiagnosticMentorProposal>();
        foreach (var finding in story.MentorFindings
                     .Where(finding =>
                         !string.IsNullOrWhiteSpace(finding.Code)
                         && !string.IsNullOrWhiteSpace(finding.Message)
                         && finding.RelatedSymbolId is { } symbolId
                         && stableIds.Contains(symbolId))
                     .OrderBy(finding => finding.Code, StringComparer.Ordinal))
        {
            AddProposal(
                NarrativeDiagnosticReviewKind.Observation,
                $"Mentor {finding.Code} [{finding.Severity}]: {finding.Message}");
            if (!string.IsNullOrWhiteSpace(finding.Explanation))
            {
                AddProposal(
                    NarrativeDiagnosticReviewKind.Hypothesis,
                    $"Mentor rationale to examine: {finding.Explanation.Trim()}");
            }
            if (!string.IsNullOrWhiteSpace(finding.RecommendedAction))
            {
                AddProposal(
                    NarrativeDiagnosticReviewKind.Action,
                    finding.RecommendedAction.Trim());
            }

            void AddProposal(NarrativeDiagnosticReviewKind kind, string text)
            {
                var reference = $"mentor:{finding.Code}:{kind}:build:{buildId}";
                if (acceptedReferences.Contains(reference))
                    return;
                proposals.Add(new NarrativeDiagnosticMentorProposal(
                    reference,
                    kind,
                    finding.Code,
                    finding.Severity,
                    text,
                    finding.RelatedSymbolId!,
                    story.TargetEventId,
                    evidencePath,
                    buildId,
                    reference));
            }
        }
        MentorReviewProposals = proposals;
        SelectedMentorReviewProposal = proposals.FirstOrDefault();
        MentorReviewProposalText = proposals.Count == 0
            ? "Mentor · no new grounded proposals; accepted statements are not proposed again."
            : $"Mentor · {proposals.Count} non-binding proposal(s) · exact build " +
              $"{ShortId(buildId)} · acceptance is always explicit";
        StatusText = MentorReviewProposalText;
    }

    /// <summary>Copies the selected Mentor proposal into the working review after explicit acceptance.</summary>
    public void AcceptSelectedMentorReviewProposal()
    {
        if (!CanActOnMentorReviewProposal
            || SelectedMentorReviewProposal is not { } proposal)
        {
            StatusText = "Select a Mentor proposal to accept.";
            return;
        }
        if (proposal.Kind == NarrativeDiagnosticReviewKind.Decision)
        {
            StatusText = "Mentor cannot author a review Decision.";
            return;
        }
        var entry = new NarrativeDiagnosticReviewEntry(
            proposal.SourceReference,
            proposal.Kind,
            "Junexis Mentor",
            DateTimeOffset.UtcNow,
            proposal.Text,
            proposal.EventId,
            proposal.RelatedSymbolId,
            proposal.EvidencePath);
        ReviewEntries = [.. ReviewEntries, entry];
        SelectedReviewEntry = entry;
        MentorReviewProposals = MentorReviewProposals
            .Where(item => item.Id != proposal.Id)
            .ToArray();
        SelectedMentorReviewProposal = MentorReviewProposals.FirstOrDefault();
        MarkReviewChanged();
        MentorReviewProposalText =
            $"Accepted Mentor {proposal.FindingCode} as {proposal.Kind}; " +
            "save a human-authored revision to retain it.";
        StatusText = MentorReviewProposalText;
    }

    /// <summary>Dismisses the selected transient proposal without changing the review.</summary>
    public void DismissSelectedMentorReviewProposal()
    {
        if (SelectedMentorReviewProposal is not { } proposal)
            return;
        MentorReviewProposals = MentorReviewProposals
            .Where(item => item.Id != proposal.Id)
            .ToArray();
        SelectedMentorReviewProposal = MentorReviewProposals.FirstOrDefault();
        MentorReviewProposalText =
            $"Dismissed Mentor {proposal.FindingCode} for this view; the review was not changed.";
        StatusText = MentorReviewProposalText;
    }

    /// <summary>Signs the latest saved review using an externally managed ECDSA P-256 key.</summary>
    /// <param name="privateKeyPath">PEM private-key path.</param>
    /// <param name="signerName">Declared signer name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SignReviewAsync(
        string privateKeyPath,
        string signerName,
        CancellationToken cancellationToken = default)
    {
        if (DiagnosticReview is null || _reviewPath is null
            || DiagnosticReview.Revisions.Count == 0 || _reviewDirty)
        {
            StatusText = "Save the current review revision before signing it.";
            return;
        }
        try
        {
            var keyFile = new FileInfo(Path.GetFullPath(privateKeyPath));
            if (!keyFile.Exists || keyFile.Length > 256 * 1024)
                throw new InvalidDataException("Review signing key is missing or exceeds 256 KiB.");
            var credentials = new NarrativeDiagnosticSigningCredentials(
                signerName,
                await File.ReadAllTextAsync(keyFile.FullName, cancellationToken).ConfigureAwait(false));
            var signed = NarrativeDiagnosticReviewStore.Sign(DiagnosticReview, credentials);
            NarrativeDiagnosticReviewStore.Save(_reviewPath, signed);
            ApplyReview(
                signed,
                NarrativeDiagnosticReviewStore.Verify(signed, _diagnosticTrustStore),
                _reviewPath);
            StatusText = $"Signed review revision r{signed.Revisions[^1].Number}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Cannot sign diagnostic review: {ex.Message}";
            _output.Write("Why", StatusText, OutputSeverity.Error);
        }
    }

    /// <summary>Exports immutable evidence and its saved review as one portable diagnostic case.</summary>
    /// <param name="path">Destination case ZIP.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExportDiagnosticCaseAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!CanExportDiagnosticCase || DiagnosticReview is null || ImportedSession is null)
        {
            StatusText = "Save the review before exporting a diagnostic case.";
            return;
        }
        try
        {
            if (_diagnosticCaseBundle is not null)
            {
                await NarrativeDiagnosticReviewStore.ExportCaseAsync(
                    path, _diagnosticCaseBundle, DiagnosticReview, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await NarrativeDiagnosticReviewStore.ExportCaseAsync(
                    path, ImportedSession.SourcePath, DiagnosticReview, cancellationToken)
                    .ConfigureAwait(false);
            }
            Update(() => StatusText = $"Exported diagnostic case '{Path.GetFileName(path)}'.");
        }
        catch (Exception ex)
        {
            Update(() => StatusText = $"Cannot export diagnostic case: {ex.Message}");
            _output.Write("Why", StatusText, OutputSeverity.Error);
        }
    }

    /// <summary>Imports and verifies a complete diagnostic case atomically.</summary>
    /// <param name="path">Source case ZIP.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ImportDiagnosticCaseAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            throw new InvalidOperationException("Wait for the current Diagnostic Story request.");
        IsBusy = true;
        StatusText = "Verifying diagnostic case...";
        try
        {
            var payload = await NarrativeDiagnosticReviewStore.ImportCaseAsync(path, cancellationToken)
                .ConfigureAwait(false);
            var imported = await NarrativeDiagnosticSessionStore.ImportBundleBytesAsync(
                payload.Bundle, path, cancellationToken, _diagnosticTrustStore).ConfigureAwait(false);
            if (!string.Equals(imported.BundleId, payload.Review.BundleId, StringComparison.Ordinal))
                throw new InvalidDataException("Diagnostic case review targets a different Bundle ID.");
            var verification = NarrativeDiagnosticReviewStore.Verify(
                payload.Review, _diagnosticTrustStore);
            var reviewPath = Path.GetFullPath(path) + ".review.json";
            NarrativeDiagnosticReviewStore.Save(reviewPath, payload.Review);
            Update(() =>
            {
                ApplyImportedSession(imported);
                ApplyReview(payload.Review, verification, reviewPath);
                _diagnosticCaseBundle = payload.Bundle;
                StatusText = $"Verified and opened diagnostic case '{Path.GetFileName(path)}'.";
            });
        }
        catch (OperationCanceledException)
        {
            Update(() => StatusText = "Diagnostic case import cancelled.");
        }
        catch (Exception ex)
        {
            Update(() => StatusText = $"Diagnostic case import rejected: {ex.Message}");
            _output.Write("Why", StatusText, OutputSeverity.Error);
        }
        finally
        {
            Update(() => IsBusy = false);
        }
    }

    /// <summary>Closes the imported read-only bundle and restores workspace bookmarks.</summary>
    public void CloseImportedDiagnosticSession()
    {
        if (ImportedSession is null)
            return;
        LeaveImportedSession(clearStory: true);
        StatusText = "Closed imported diagnostic session; workspace session restored.";
    }

    /// <summary>Compares the selected imported Story with the live Story captured before import.</summary>
    public void CompareImportedWithLive()
    {
        if (ImportedSession is null || DiagnosticStory is not { } imported)
        {
            StatusText = "Open a verified diagnostic bundle before comparing it.";
            return;
        }
        if (_liveStoryBeforeImport is not { } live)
        {
            StatusText = "Load a live Diagnostic Story before importing the bundle.";
            return;
        }
        var sameBuild = !string.IsNullOrWhiteSpace(imported.BuildId)
            && string.Equals(imported.BuildId, live.BuildId, StringComparison.Ordinal);
        var addedInLive = live.Evidence
            .Except(imported.Evidence, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missingFromLive = imported.Evidence
            .Except(live.Evidence, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        CrossSessionComparison = new DiagnosticSessionCrossComparison(
            imported.TargetEventId,
            live.TargetEventId,
            sameBuild,
            $"Imported event #{imported.TargetEventId} ↔ live event #{live.TargetEventId} · " +
            $"{(sameBuild ? "same build" : "different or unknown build")} · " +
            $"live +{addedInLive.Length}/-{missingFromLive.Length} evidence item(s)",
            addedInLive,
            missingFromLive,
            live.Timeline.Count - imported.Timeline.Count,
            live.CausalChain.Count - imported.CausalChain.Count,
            live.ConcurrentEvents.Count - imported.ConcurrentEvents.Count,
            live.Communications.Count - imported.Communications.Count);
        StatusText =
            $"Compared imported event #{imported.TargetEventId} with live event #{live.TargetEventId}.";
    }

    /// <summary>Adds the valid imported signer public key to the local trust store.</summary>
    public void TrustImportedSigner()
    {
        if (ImportedSession is not { Signature:
                {
                    Status: NarrativeDiagnosticAuthenticityStatus.SignatureVerified,
                    SignerFingerprint: { } fingerprint,
                    PublicKeySpki: { } publicKey,
                    SignerName: { } signerName
                }
            } imported)
        {
            StatusText = "Only a valid untrusted signature can be trusted.";
            return;
        }
        try
        {
            _diagnosticTrustStore.Trust(fingerprint, signerName, publicKey);
            var verification = imported.Signature with
            {
                Status = NarrativeDiagnosticAuthenticityStatus.TrustedSigner,
                Message = $"Signature verified; signer '{signerName}' is locally trusted."
            };
            ImportedSession = imported with { Signature = verification };
            DiagnosticAuthenticityText = BuildAuthenticityText(verification);
            ImportedSessionSummaryText = BuildImportedSessionSummary(ImportedSession);
            StatusText = $"Trusted diagnostic signer '{signerName}' locally.";
        }
        catch (Exception ex)
        {
            StatusText = $"Cannot update diagnostic signer trust: {ex.Message}";
            _output.Write("Why", StatusText, OutputSeverity.Error);
        }
    }

    /// <summary>Removes local trust for the valid imported signer public key.</summary>
    public void RemoveImportedSignerTrust()
    {
        if (ImportedSession is not { Signature:
                {
                    Status: NarrativeDiagnosticAuthenticityStatus.TrustedSigner,
                    SignerFingerprint: { } fingerprint,
                    SignerName: { } signerName
                }
            } imported)
        {
            StatusText = "The imported signer is not locally trusted.";
            return;
        }
        try
        {
            _diagnosticTrustStore.Remove(fingerprint);
            var verification = imported.Signature with
            {
                Status = NarrativeDiagnosticAuthenticityStatus.SignatureVerified,
                Message = $"Signature verified; signer '{signerName}' is not locally trusted."
            };
            ImportedSession = imported with { Signature = verification };
            DiagnosticAuthenticityText = BuildAuthenticityText(verification);
            ImportedSessionSummaryText = BuildImportedSessionSummary(ImportedSession);
            StatusText = $"Removed local trust for diagnostic signer '{signerName}'.";
        }
        catch (Exception ex)
        {
            StatusText = $"Cannot update diagnostic signer trust: {ex.Message}";
            _output.Write("Why", StatusText, OutputSeverity.Error);
        }
    }

    /// <summary>Current stopped-frame correlation explanation.</summary>
    public string DebugCorrelationText
    {
        get => _debugCorrelationText;
        private set => SetField(ref _debugCorrelationText, value);
    }

    /// <summary>Latest versioned correlation result for a stopped debugger frame.</summary>
    public WhyDebugCorrelationResult? DebugCorrelation
    {
        get => _debugCorrelation;
        private set => SetField(ref _debugCorrelation, value);
    }

    /// <summary>Correlates a stopped frame and optionally loads WHY for the selected trace event.</summary>
    /// <param name="frame">Mapped debugger frame.</param>
    /// <param name="loadWhy">Whether to load the causal chain after a successful match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CorrelateStoppedFrameAsync(
        DebugStackFrame frame,
        bool loadWhy = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        CancelCorrelationRequest();
        var version = Interlocked.Increment(ref _correlationVersion);
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _correlationRequestCancellation = requestCancellation;
        Update(() => DebugCorrelationText =
            $"Correlating stopped frame {frame.JunexisSymbolId ?? frame.Name} at {frame.SourcePath}:{frame.Line}...");
        try
        {
            var result = await _service.CorrelateAsync(
                BaseUrl,
                new WhyDebugCorrelationRequest(
                    frame.JunexisSymbolId,
                    frame.SourcePath,
                    frame.Line,
                    _narrativeSession?.Snapshot.BuildId,
                    NormalizeFilter(NarrativeFilterText),
                    NormalizeFilter(EventKindFilterText))
                {
                    RuntimeBundleId = DiagnosticStory?.RuntimeBundleId
                },
                requestCancellation.Token);
            if (version != Volatile.Read(ref _correlationVersion))
                return;
            Update(() =>
            {
                DebugCorrelation = result;
                DebugCorrelationText = result.Matched && result.EventId is not null
                    ? $"Event #{result.EventId} matched by {result.Confidence}: {string.Join(" ", result.Evidence)}"
                    : $"No Narrative event matched: {string.Join(" ", result.Evidence)}";
            });
            if (loadWhy && result.Matched && result.EventId is not null)
            {
                EventIdText = result.EventId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                CancelAutoFollowRequest();
                await LoadAsync(requestCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            Update(() => DebugCorrelationText = "Stopped-frame correlation cancelled.");
        }
        catch (Exception ex)
        {
            Update(() =>
            {
                DebugCorrelation = null;
                DebugCorrelationText = $"Stopped-frame correlation failed: {ex.Message}";
            });
            _output.Write("Why", DebugCorrelationText, OutputSeverity.Warning);
        }
        finally
        {
            if (ReferenceEquals(_correlationRequestCancellation, requestCancellation))
                _correlationRequestCancellation = null;
            requestCancellation.Dispose();
        }
    }

    /// <summary>Loads WHY for the currently correlated stopped-frame event.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task LoadCorrelatedWhyAsync(CancellationToken cancellationToken = default)
    {
        if (DebugCorrelation is not { Matched: true, EventId: not null } result)
        {
            _output.Write("Why", "No correlated stopped event is available.", OutputSeverity.Warning);
            return Task.CompletedTask;
        }
        EventIdText = result.EventId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return LoadAsync(cancellationToken);
    }

    /// <summary>Exports the current Diagnostic Story as versioned JSON.</summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExportJsonAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var story = DiagnosticStory ?? throw new InvalidOperationException("Load a Diagnostic Story before exporting it.");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, story, new JsonSerializerOptions { WriteIndented = true }, cancellationToken)
            .ConfigureAwait(false);
        _output.Write("Why", $"Diagnostic Story exported to {path}.");
    }

    /// <summary>Exports the current Diagnostic Story as evidence-linked Markdown.</summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExportMarkdownAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var story = DiagnosticStory ?? throw new InvalidOperationException("Load a Diagnostic Story before exporting it.");
        var markdown = BuildMarkdown(story);
        await File.WriteAllTextAsync(path, markdown, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        _output.Write("Why", $"Diagnostic Story exported to {path}.");
    }

    /// <summary>Unsubscribes automatic debugger and event-bus correlation handlers.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Interlocked.Increment(ref _correlationVersion);
        Interlocked.Increment(ref _autoFollowVersion);
        CancelAutoFollowRequest();
        CancelCorrelationRequest();
        _autoFollowCancellation.Cancel();
        _autoFollowCancellation.Dispose();
        if (_debugSession is not null)
            _debugSession.SnapshotChanged -= OnDebugSnapshotChanged;
        _eventBus?.Unsubscribe<WhyForStoppedFrameRequested>(OnWhyForStoppedFrameRequested);
        _eventBus?.Unsubscribe<AnalysisSnapshotUpdatedEvent>(OnAnalysisSnapshotUpdated);
        if (_narrativeSession is not null)
            _narrativeSession.SnapshotChanged -= OnNarrativeSessionChanged;
        if (_workspace is not null)
            _workspace.FolderOpened -= OnWorkspaceOpened;
    }

    /// <summary>Navigates from a resolved trace communication to Junexis source.</summary>
    /// <param name="resolution">Selected communication resolution.</param>
    public void NavigateTo(WhyCommunicationResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (_eventBus is null || resolution.Source is null || string.IsNullOrWhiteSpace(resolution.Source.File))
        {
            _output.Write("Why", "Selected trace communication has no Junexis source location.", OutputSeverity.Warning);
            return;
        }

        _eventBus.Publish(new NavigateToLocationRequested(
            resolution.Source.File,
            Math.Max(1, resolution.Source.Line),
            Math.Max(1, resolution.Source.Column)));
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

    private void OnDebugSnapshotChanged(DebugSessionSnapshot snapshot)
    {
        if (snapshot.State == DebugSessionState.Stopped && snapshot.StackFrames.FirstOrDefault() is { } frame)
            _ = CorrelateStoppedFrameAsync(frame, loadWhy: true);
        else if (snapshot.State is DebugSessionState.Running or DebugSessionState.Terminated or DebugSessionState.Failed)
        {
            CancelCorrelationRequest();
            Update(() =>
            {
                DebugCorrelation = null;
                DebugCorrelationText = snapshot.State == DebugSessionState.Running
                    ? "Debugger running; stopped-frame Narrative correlation is idle."
                    : "Debug session ended; stopped-frame Narrative correlation is idle.";
            });
        }
    }

    private void OnWhyForStoppedFrameRequested(WhyForStoppedFrameRequested request) =>
        _ = CorrelateStoppedFrameAsync(request.Frame, request.LoadWhy);

    private void OnAnalysisSnapshotUpdated(AnalysisSnapshotUpdatedEvent update)
    {
        _analysisSnapshotPath = update.SnapshotPath;
        _analysisBuildId = update.BuildId;
    }

    private void OnWorkspaceOpened(string _) => Update(() =>
    {
        if (ImportedSession is not null)
            LeaveImportedSession(clearStory: true);
        else
            ReloadBookmarks();
    });

    private void OnNarrativeSessionChanged(NarrativeSessionSnapshot snapshot) =>
        Update(() => ApplyNarrativeSession(snapshot, allowAutoFollow: true));

    private void ApplyNarrativeSession(
        NarrativeSessionSnapshot snapshot,
        bool allowAutoFollow)
    {
        NarrativeSessionText = snapshot.Status;
        NarrativeActivityText = RuntimeFollowPaused
            ? snapshot.LatestEventId is { } pausedEventId
                ? $"Runtime activity · follow paused · capture continues · latest #{pausedEventId}"
                : "Runtime activity · follow paused · capture continues"
            : FormatRuntimeActivity(snapshot);
        if (!string.IsNullOrWhiteSpace(snapshot.BaseUrl))
            BaseUrl = snapshot.BaseUrl;
        if (!string.Equals(_observedManagedBuildId, snapshot.BuildId, StringComparison.Ordinal))
        {
            _observedManagedBuildId = snapshot.BuildId;
            _observedManagedEventId = null;
            EventIdText = string.Empty;
            Interlocked.Increment(ref _autoFollowVersion);
            CancelAutoFollowRequest();
            if (allowAutoFollow)
                ClearStory();
        }
        if (snapshot.LatestEventId is { } latestEventId)
        {
            EventIdText = latestEventId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var isNewEvent = _observedManagedEventId != latestEventId;
            _observedManagedEventId = latestEventId;
            if (!_disposed && allowAutoFollow && AutoFollowRuntime && !RuntimeFollowPaused && isNewEvent)
                _ = StartFollowingManagedEvent(latestEventId);
        }
    }

    private Task StartFollowingManagedEvent(
        ulong latestEventId,
        CancellationToken cancellationToken = default)
    {
        CancelAutoFollowRequest();
        var version = Interlocked.Increment(ref _autoFollowVersion);
        _autoFollowRequestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _autoFollowCancellation.Token,
            cancellationToken);
        NarrativeActivityText =
            $"Runtime activity · new event #{latestEventId} · applying filters…";
        return FollowManagedEventAsync(
            latestEventId,
            version,
            _autoFollowRequestCancellation.Token);
    }

    private void CancelAutoFollowRequest()
    {
        Interlocked.Increment(ref _autoFollowVersion);
        _autoFollowRequestCancellation?.Cancel();
        _autoFollowRequestCancellation?.Dispose();
        _autoFollowRequestCancellation = null;
    }

    private void CancelCorrelationRequest()
    {
        Interlocked.Increment(ref _correlationVersion);
        _correlationRequestCancellation?.Cancel();
        _correlationRequestCancellation?.Dispose();
        _correlationRequestCancellation = null;
    }

    private async Task FollowManagedEventAsync(
        ulong eventId,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            if (HasRuntimeFilter)
            {
                var selection = await _service.GetLatestEventAsync(
                    BaseUrl,
                    _observedManagedBuildId,
                    null,
                    NormalizeFilter(NarrativeFilterText),
                    NormalizeFilter(EventKindFilterText),
                    cancellationToken).ConfigureAwait(false);
                if (version != Volatile.Read(ref _autoFollowVersion))
                    return;
                if (selection is null)
                {
                    Update(() => NarrativeActivityText =
                        "Runtime activity · live · no event matches the active filters");
                    return;
                }
                eventId = selection.EventId;
                Update(() => EventIdText =
                    eventId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            for (var attempt = 0; IsBusy && attempt < 40; attempt++)
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            if (version != Volatile.Read(ref _autoFollowVersion) || IsBusy)
                return;

            await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (version == Volatile.Read(ref _autoFollowVersion)
                && DiagnosticStory?.TargetEventId == eventId)
            {
                Update(() => NarrativeActivityText =
                    $"Runtime activity · live · event #{eventId} loaded automatically");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool HasRuntimeFilter =>
        !string.IsNullOrWhiteSpace(NarrativeFilterText)
        || !string.IsNullOrWhiteSpace(EventKindFilterText);

    private void OnRuntimeFilterChanged()
    {
        CancelAutoFollowRequest();
        NarrativeActivityText = RuntimeFollowPaused
            ? "Runtime activity · follow paused · filters updated"
            : "Runtime activity · filters updated · resume or wait for the next event";
    }

    private static string? NormalizeFilter(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatRuntimeActivity(NarrativeSessionSnapshot snapshot) =>
        snapshot.Activity switch
        {
            NarrativeRuntimeActivity.Preparing => "Runtime activity · preparing collector",
            NarrativeRuntimeActivity.WaitingForEvents =>
                $"Runtime activity · connected · waiting for build {ShortId(snapshot.BuildId ?? "unknown")}",
            NarrativeRuntimeActivity.Live when snapshot.LatestEventId is { } eventId =>
                snapshot.EventDelta > 0
                    ? $"Runtime activity · live · +{snapshot.EventDelta} event(s) · latest #{eventId}"
                    : $"Runtime activity · live · latest #{eventId}",
            NarrativeRuntimeActivity.Disconnected => "Runtime activity · disconnected",
            _ => "Runtime activity · inactive"
        };

    private async Task<DiagnosticStoryResult> EnrichWithMentorAsync(
        DiagnosticStoryResult story,
        CancellationToken cancellationToken)
    {
        if (_mentor is null)
        {
            Update(() => MentorContextText = "Mentor · unavailable in this Studio session.");
            return story;
        }
        if (string.IsNullOrWhiteSpace(_analysisSnapshotPath) || string.IsNullOrWhiteSpace(_analysisBuildId))
        {
            Update(() => MentorContextText = "Mentor · not joined: active analysis snapshot is unavailable.");
            return story;
        }
        if (string.IsNullOrWhiteSpace(story.BuildId))
        {
            Update(() => MentorContextText = "Mentor · not joined: runtime build identity was not captured.");
            return story;
        }
        if (!string.Equals(story.BuildId, _analysisBuildId, StringComparison.Ordinal))
        {
            Update(() => MentorContextText =
                $"Mentor · not joined: runtime build {ShortId(story.BuildId)} differs from active build {ShortId(_analysisBuildId)}.");
            return story;
        }
        try
        {
            var analysis = await _mentor.AnalyzeAsync(_analysisSnapshotPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(analysis.BuildId, _analysisBuildId, StringComparison.Ordinal))
                throw new InvalidDataException("Mentor result buildId does not match the active analysis snapshot.");
            _eventBus?.Publish(new MentorAnalysisCompletedEvent(
                analysis.BuildId,
                analysis.Findings.Count));
            var stableIds = new HashSet<string>(StringComparer.Ordinal) { story.TargetSymbolId };
            foreach (var communication in story.Communications)
            {
                stableIds.Add(communication.CommunicationId);
                stableIds.Add(communication.PipelineSymbolId);
                stableIds.Add(communication.SourceSymbolId);
                stableIds.Add(communication.TargetSymbolId);
            }
            var groundedFindings = analysis.Findings
                .Where(finding => finding.RelatedSymbolId is not null && stableIds.Contains(finding.RelatedSymbolId))
                .ToArray();
            Update(() => MentorContextText =
                $"Mentor · Current · build {ShortId(analysis.BuildId)} · {groundedFindings.Length} grounded finding(s).");
            return story with
            {
                MentorFindings = groundedFindings
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Update(() => MentorContextText = $"Mentor · enrichment unavailable: {ex.Message}");
            _output.Write("Why", $"Mentor enrichment unavailable: {ex.Message}", OutputSeverity.Warning);
            return story;
        }
    }

    private void ClearStory()
    {
        DiagnosticStory = null;
        StoryComparison = null;
        CausalChain = [];
        Communications = [];
        Timeline = [];
        ConcurrentEvents = [];
        StoryEvidence = [];
        MentorFindings = [];
        MentorContextText = "Mentor · waiting for build-correlated runtime evidence.";
    }

    private void PresentStoredStory(DiagnosticStoryResult story)
    {
        DiagnosticStory = story;
        StoryComparison = null;
        Timeline = story.Timeline;
        ConcurrentEvents = story.ConcurrentEvents;
        StoryEvidence = story.Evidence;
        MentorFindings = story.MentorFindings;
        CausalChain = story.CausalChain.Select(item => new WhyTraceChainEntry(
            item.EventId,
            item.TaskId,
            item.Kind,
            item.LamportClock,
            item.Timestamp,
            item.NarrativeId)).ToArray();
        Communications = story.Communications;
        MentorContextText = story.MentorFindings.Count == 0
            ? "Mentor · stored Story contains no grounded findings."
            : $"Mentor · stored Story · {story.MentorFindings.Count} grounded finding(s).";
    }

    private void ReloadBookmarks()
    {
        if (_workspace is null)
            return;
        try
        {
            Bookmarks = NarrativeDiagnosticSessionStore.Load(_workspace);
            SelectedBookmark = Bookmarks.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Bookmarks = [];
            SelectedBookmark = null;
            _output.Write(
                "Why",
                $"Cannot load Narrative diagnostic bookmarks: {ex.Message}",
                OutputSeverity.Warning);
        }
    }

    private bool SaveBookmarks()
    {
        if (_workspace is null)
            return false;
        try
        {
            NarrativeDiagnosticSessionStore.Save(_workspace, Bookmarks);
            return true;
        }
        catch (Exception ex)
        {
            _output.Write(
                "Why",
                $"Cannot save Narrative diagnostic bookmarks: {ex.Message}",
                OutputSeverity.Error);
            StatusText = $"Cannot save diagnostic bookmarks: {ex.Message}";
            return false;
        }
    }

    private void ApplyImportedSession(ImportedNarrativeDiagnosticSession imported)
    {
        PauseRuntimeFollow();
        if (ImportedSession is null)
        {
            _liveStoryBeforeImport = DiagnosticStory;
            LiveComparisonCandidateText = _liveStoryBeforeImport is null
                ? "No live Story was loaded before import."
                : $"Live candidate · event #{_liveStoryBeforeImport.TargetEventId} · " +
                  $"build {_liveStoryBeforeImport.BuildId ?? "not captured"}";
        }
        ImportedSession = imported;
        DiagnosticSessionModeText =
            $"Imported · evidence read-only · {Path.GetFileName(imported.SourcePath)} · " +
            $"{imported.VerifiedFileCount} verified file(s)";
        ImportedSessionSummaryText = BuildImportedSessionSummary(imported);
        DiagnosticAuthenticityText = BuildAuthenticityText(imported.Signature);
        ImportedEvidenceFiles = imported.EvidenceFiles;
        MentorReviewProposals = [];
        SelectedMentorReviewProposal = null;
        MentorReviewProposalText =
            "Mentor proposals are generated only from build-correlated findings captured in this Story.";
        SelectedReviewEvidenceFile = ImportedEvidenceFiles.FirstOrDefault();
        CrossSessionComparison = null;
        NarrativeFilterText = imported.NarrativeFilter ?? string.Empty;
        EventKindFilterText = imported.EventKindFilter ?? string.Empty;
        Bookmarks = imported.Bookmarks;
        SelectedBookmark = Bookmarks.FirstOrDefault();
        PinnedDiagnosticStory = imported.Pinned;
        PinnedStoryText = imported.Pinned is null
            ? "Imported session has no pinned Diagnostic Story."
            : $"Imported pin · event #{imported.Pinned.TargetEventId} · build " +
              $"{imported.Pinned.BuildId ?? "not captured"}";
        var presented = imported.Current
            ?? imported.Bookmarks.FirstOrDefault()?.Story
            ?? imported.Pinned!;
        PresentStoredStory(presented);
        StoryComparison = imported.Comparison;
        DebugCorrelation = imported.DebugCorrelation;
        DebugCorrelationText = imported.DebugCorrelation is null
            ? "Imported session contains no stopped-frame correlation."
            : $"Imported correlation · {imported.DebugCorrelation.Confidence} · " +
              $"event #{imported.DebugCorrelation.EventId?.ToString() ?? "not matched"}";
        EventIdText = presented.TargetEventId.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        StatusText =
            $"Verified and opened diagnostic session from " +
            $"'{Path.GetFileName(imported.SourcePath)}' read-only.";
    }

    private void ApplyReview(
        NarrativeDiagnosticReviewDocument document,
        NarrativeDiagnosticSignatureVerification signature,
        string path)
    {
        DiagnosticReview = document;
        ReviewEntries = document.Entries;
        SelectedReviewEntry = ReviewEntries.FirstOrDefault();
        _reviewStatus = document.Status;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReviewStatus)));
        _reviewPath = path;
        _reviewDirty = false;
        ReviewComparison = NarrativeDiagnosticReviewStore.CompareLatest(document);
        ReviewStateText =
            $"Case · {document.Status} · Bundle ID {document.BundleId} · " +
            $"{document.Entries.Count} statement(s) · {document.Revisions.Count} saved revision(s)";
        ReviewSignatureText = BuildReviewAuthenticityText(signature);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExportDiagnosticCase)));
    }

    private void MarkReviewChanged()
    {
        if (DiagnosticReview is null)
            return;
        DiagnosticReview = DiagnosticReview with
        {
            Status = ReviewStatus,
            Entries = ReviewEntries.ToArray(),
            Signature = null
        };
        _reviewDirty = true;
        ReviewComparison = null;
        ReviewStateText =
            $"Case · {ReviewStatus} · unsaved working changes · " +
            $"{ReviewEntries.Count} statement(s) · saved history remains unchanged";
        ReviewSignatureText = "Review authenticity · unsigned working changes";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExportDiagnosticCase)));
    }

    private void LeaveImportedSession(bool clearStory)
    {
        ImportedSession = null;
        DiagnosticSessionModeText = "Workspace session · editable";
        ImportedSessionSummaryText =
            "Import a diagnostic bundle to inspect its verified provenance.";
        DiagnosticAuthenticityText = "Authenticity · no imported bundle";
        ImportedEvidenceFiles = [];
        _liveStoryBeforeImport = null;
        LiveComparisonCandidateText =
            "Load a live Diagnostic Story before importing a bundle to compare both.";
        CrossSessionComparison = null;
        DiagnosticReview = null;
        ReviewEntries = [];
        SelectedReviewEntry = null;
        ReviewStatus = NarrativeDiagnosticReviewStatus.Open;
        SelectedReviewEvidenceFile = null;
        ReviewStateText = "Open a verified diagnostic bundle to begin a review.";
        ReviewSignatureText = "Review authenticity · unavailable";
        ReviewComparison = null;
        _reviewPath = null;
        _reviewDirty = false;
        _diagnosticCaseBundle = null;
        MentorReviewProposals = [];
        SelectedMentorReviewProposal = null;
        MentorReviewProposalText =
            "Mentor proposals are generated only from build-correlated findings captured in this Story.";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExportDiagnosticCase)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCompareImportedWithLive)));
        ReloadBookmarks();
        PinnedDiagnosticStory = null;
        PinnedStoryText = "No Diagnostic Story is pinned.";
        StoryComparison = null;
        DebugCorrelation = null;
        DebugCorrelationText = "Stop at a mapped frame to correlate it with Narrative.";
        if (clearStory)
            ClearStory();
    }

    private static string BuildImportedSessionSummary(
        ImportedNarrativeDiagnosticSession imported) =>
        $"Integrity · verified SHA-256 for {imported.VerifiedFileCount} file(s)\n" +
        $"Bundle ID · {imported.BundleId}\n" +
        $"Authenticity · {imported.Signature.Status}\n" +
        $"Bundle · {Path.GetFileName(imported.SourcePath)}\n" +
        $"Exported · {imported.ExportedAt:u}\n" +
        $"Source workspace · {imported.WorkspaceName ?? "not captured"}\n" +
        $"Build · {imported.BuildId ?? "not captured"}\n" +
        $"Current event · {imported.Current?.TargetEventId.ToString() ?? "not captured"}\n" +
        $"Pinned event · {imported.Pinned?.TargetEventId.ToString() ?? "not captured"}\n" +
        $"Bookmarks · {imported.Bookmarks.Count}";

    private static string ResolvePresentedStoryEvidencePath(
        ImportedNarrativeDiagnosticSession imported,
        DiagnosticStoryResult story)
    {
        if (imported.Current?.TargetEventId == story.TargetEventId)
            return "stories/current.json";
        if (imported.Pinned?.TargetEventId == story.TargetEventId)
            return "stories/pinned.json";
        var bookmark = imported.Bookmarks.FirstOrDefault(
            item => item.Story.TargetEventId == story.TargetEventId);
        return bookmark is null ? "session.json" : $"bookmarks/{bookmark.Id}.json";
    }

    private static string BuildAuthenticityText(
        NarrativeDiagnosticSignatureVerification signature) =>
        $"Authenticity · {signature.Status}\n" +
        $"Signer · {signature.SignerName ?? "not declared"}\n" +
        $"Fingerprint · {signature.SignerFingerprint ?? "not available"}\n" +
        signature.Message;

    private static string BuildReviewAuthenticityText(
        NarrativeDiagnosticSignatureVerification signature) =>
        $"Review authenticity · {signature.Status}\n" +
        $"Signer · {signature.SignerName ?? "not declared"}\n" +
        $"Fingerprint · {signature.SignerFingerprint ?? "not available"}\n" +
        signature.Message;

    private static string BuildMarkdown(DiagnosticStoryResult story)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Diagnostic Story — event #{story.TargetEventId}");
        builder.AppendLine();
        builder.AppendLine($"- Schema: `{story.SchemaVersion}`");
        builder.AppendLine($"- Build: `{story.BuildId ?? "not captured"}`");
        builder.AppendLine($"- Runtime bundle: `{story.RuntimeBundleId ?? "not captured"}`");
        builder.AppendLine($"- Task: `{story.TargetSymbolId}` ({story.TargetDisplayName})");
        builder.AppendLine($"- Narrative: `{story.NarrativeId ?? "not captured"}`");
        AppendEvents(builder, "Timeline", story.Timeline);
        AppendEvents(builder, "Happens-before chain", story.CausalChain);
        AppendEvents(builder, "Concurrent events", story.ConcurrentEvents);
        builder.AppendLine("## Evidence");
        builder.AppendLine();
        foreach (var evidence in story.Evidence)
            builder.AppendLine($"- {evidence}");
        builder.AppendLine();
        builder.AppendLine("## Mentor findings");
        builder.AppendLine();
        foreach (var finding in story.MentorFindings)
            builder.AppendLine($"- `{finding.Code}` [{finding.Severity}] {finding.Message} (`{finding.RelatedSymbolId}`)");
        return builder.ToString();
    }

    private static string ShortId(string value) => value[..Math.Min(12, value.Length)];

    private static void AppendEvents(StringBuilder builder, string title, IReadOnlyList<DiagnosticStoryEvent> events)
    {
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        builder.AppendLine("| Event | Task | Kind | Lamport | Source |");
        builder.AppendLine("|---:|---:|---|---:|---|");
        foreach (var item in events)
            builder.AppendLine($"| {item.EventId} | {item.TaskId} | {item.Kind} | {item.LamportClock} | {item.File}:{item.Line} |");
    }

    private void Update(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
            action();
        else
            _synchronizationContext.Post(_ => action(), null);
    }
}

/// <summary>
/// Rejestruje panel narzędziowy <c>Why?</c> dla TraceCollector.
/// </summary>
internal sealed class WhyPanelExtension : IJunexisExtension
{
    private WhyPanelViewModel? _viewModel;

    /// <inheritdoc />
    public string Id => "junexis.narrative.why";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Capabilities.TryGet<IWhyTraceQueryService>(out var service) || service is null)
        {
            context.Output.Write("Why", "IWhyTraceQueryService not available — Why panel inactive.");
            return;
        }

        context.Capabilities.TryGet<IDebugSessionService>(out var debugSession);
        context.Capabilities.TryGet<IMentorAnalysisService>(out var mentor);
        context.Capabilities.TryGet<INarrativeSessionService>(out var narrativeSession);
        _viewModel = new WhyPanelViewModel(
            service,
            context.Output,
            context.EventBus,
            debugSession,
            mentor,
            narrativeSession,
            context.Workspace);

        context.Panels.RegisterPanel(
            "junexis.narrative.why",
            PanelType.Tool,
            () => _viewModel,
            title: "Why?",
            dockGroup: DockGroup.Bottom);

        context.Output.Write("Extensions", "WhyPanelExtension loaded");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}

internal sealed class HttpWhyTraceQueryService : IWhyTraceQueryService
{
    public Task<NarrativeEventSelection?> GetAdjacentEventAsync(
        string baseUrl,
        ulong eventId,
        bool previous,
        string? buildId,
        string? runtimeBundleId,
        string? narrativeId,
        string? eventKind,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"eventId={eventId.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"direction={(previous ? "previous" : "next")}"
        };
        AddQuery(query, "buildId", buildId);
        AddQuery(query, "bundleId", runtimeBundleId);
        AddQuery(query, "narrativeId", narrativeId);
        AddQuery(query, "kind", eventKind);
        return GetSelectedEventAsync(
            baseUrl,
            "/events/adjacent?" + string.Join("&", query),
            cancellationToken);
    }

    public async Task<NarrativeEventSelection?> GetLatestEventAsync(
        string baseUrl,
        string? buildId,
        string? runtimeBundleId,
        string? narrativeId,
        string? eventKind,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        AddQuery(query, "buildId", buildId);
        AddQuery(query, "bundleId", runtimeBundleId);
        AddQuery(query, "narrativeId", narrativeId);
        AddQuery(query, "kind", eventKind);
        return await GetSelectedEventAsync(
            baseUrl,
            "/events/latest" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query)),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<NarrativeEventSelection?> GetSelectedEventAsync(
        string baseUrl,
        string requestUri,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(10)
        };
        using var response = await http.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WhyEventDto>(
            cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("TraceCollector returned an empty latest-event response.");
        string? capturedNarrativeId = null;
        string? capturedBuildId = null;
        string? capturedBundleId = null;
        result.Attributes?.TryGetValue("narrative_id", out capturedNarrativeId);
        result.Attributes?.TryGetValue("build_id", out capturedBuildId);
        result.Attributes?.TryGetValue("bundle_id", out capturedBundleId);
        return new NarrativeEventSelection(
            result.EventId,
            result.Kind,
            capturedNarrativeId,
            capturedBuildId)
        {
            RuntimeBundleId = capturedBundleId
        };
    }

    public async Task<DiagnosticStoryResult> GetDiagnosticStoryAsync(
        string baseUrl,
        ulong eventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(10)
        };
        var dto = await http.GetFromJsonAsync<DiagnosticStoryDto>(
            $"/events/{eventId}/story", cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("TraceCollector returned an empty Diagnostic Story.");
        if (!dto.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported Diagnostic Story schema '{dto.SchemaVersion}'.");
        ValidateStoryIdentity(dto);
        return new DiagnosticStoryResult(
            dto.SchemaVersion,
            dto.TargetEventId,
            dto.TargetTask.SymbolId,
            dto.TargetTask.DisplayName,
            dto.TargetTask.Resolved,
            dto.NarrativeId,
            dto.Timeline.Select(ToStoryEvent).ToArray(),
            dto.CausalChain.Select(ToStoryEvent).ToArray(),
            dto.ConcurrentEvents.Select(ToStoryEvent).ToArray(),
            dto.Communications.Select(ToCommunication).ToArray(),
            dto.Evidence)
        {
            BuildId = dto.BuildId,
            RuntimeBundleId = dto.BundleId
        };
    }

    private static void ValidateStoryIdentity(DiagnosticStoryDto dto)
    {
        foreach (var item in dto.Timeline.Concat(dto.CausalChain).Concat(dto.ConcurrentEvents))
        {
            string? eventBuildId = null;
            string? eventBundleId = null;
            item.Attributes?.TryGetValue("build_id", out eventBuildId);
            item.Attributes?.TryGetValue("bundle_id", out eventBundleId);
            if (!string.Equals(NormalizeIdentity(eventBuildId), NormalizeIdentity(dto.BuildId), StringComparison.Ordinal)
                || !string.Equals(NormalizeIdentity(eventBundleId), NormalizeIdentity(dto.BundleId), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Diagnostic Story event #{item.EventId} does not match the Story Build ID / Bundle ID identity.");
            }
        }
    }

    private static string? NormalizeIdentity(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    public async Task<WhyDebugCorrelationResult> CorrelateAsync(
        string baseUrl,
        WhyDebugCorrelationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(request);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(10)
        };
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.SymbolId))
            query.Add($"symbolId={Uri.EscapeDataString(request.SymbolId)}");
        if (!string.IsNullOrWhiteSpace(request.SourcePath))
            query.Add($"file={Uri.EscapeDataString(request.SourcePath)}");
        if (request.Line is > 0)
            query.Add($"line={request.Line.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        AddQuery(query, "buildId", request.BuildId);
        AddQuery(query, "bundleId", request.RuntimeBundleId);
        AddQuery(query, "narrativeId", request.NarrativeId);
        AddQuery(query, "kind", request.EventKind);
        var result = await http.GetFromJsonAsync<CorrelationDto>(
            "/events/correlate?" + string.Join("&", query), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("TraceCollector returned an empty correlation response.");
        if (!result.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported trace correlation schema '{result.SchemaVersion}'.");
        return new WhyDebugCorrelationResult(
            result.SchemaVersion, result.Matched, result.EventId, result.TaskId, result.SymbolId,
            result.Confidence, result.NarrativeId, result.Evidence);
    }

    public async Task<WhyTraceResult> GetWhyAsync(string baseUrl, ulong eventId, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(10)
        };

        var whyTask = http.GetFromJsonAsync<WhyResultDto>($"/events/{eventId}/why", cancellationToken);
        var communicationsTask = GetCommunicationsAsync(http, eventId, cancellationToken);
        var dto = await whyTask.ConfigureAwait(false)
                  ?? throw new InvalidOperationException("TraceCollector returned an empty response.");
        var communications = await communicationsTask.ConfigureAwait(false);

        return new WhyTraceResult(
            dto.TargetEventId,
            dto.CausalChain.Select(e => new WhyTraceChainEntry(
                e.EventId,
                e.TaskId,
                e.Kind,
                e.LamportClock,
                e.Timestamp,
                e.Attributes is not null && e.Attributes.TryGetValue("narrative_id", out var narrativeId) ? narrativeId : null)).ToList(),
            dto.Depth)
        {
            Communications = communications
        };
    }

    private static async Task<IReadOnlyList<WhyCommunicationResolution>> GetCommunicationsAsync(
        HttpClient http,
        ulong eventId,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"/events/{eventId}/communications", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return [];
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<CommunicationEnvelopeDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (dto is null || !dto.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
            return [];
        return dto.Communications.Select(communication => new WhyCommunicationResolution(
            communication.EventId,
            communication.CommunicationId,
            communication.Kind,
            communication.PipelineSymbolId,
            communication.SourceSymbolId,
            communication.TargetSymbolId,
            communication.Source is null
                ? null
                : new AnalysisSourceLocation(communication.Source.File, communication.Source.Line, communication.Source.Column))).ToArray();
    }

    private static DiagnosticStoryEvent ToStoryEvent(WhyEventDto item) => new(
        item.EventId,
        item.TaskId,
        item.Kind,
        item.LamportClock,
        item.Timestamp,
        item.File,
        item.Line,
        item.Function,
        item.Attributes is not null && item.Attributes.TryGetValue("narrative_id", out var narrativeId)
            ? narrativeId
            : null);

    private static WhyCommunicationResolution ToCommunication(CommunicationDto communication) => new(
        communication.EventId,
        communication.CommunicationId,
        communication.Kind,
        communication.PipelineSymbolId,
        communication.SourceSymbolId,
        communication.TargetSymbolId,
        communication.Source is null
            ? null
            : new AnalysisSourceLocation(communication.Source.File, communication.Source.Line, communication.Source.Column));

    private static void AddQuery(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            query.Add($"{name}={Uri.EscapeDataString(value)}");
    }

    private sealed record WhyResultDto
    {
        public ulong TargetEventId { get; init; }
        public List<WhyEventDto> CausalChain { get; init; } = new();
        public int Depth { get; init; }
    }

    private sealed record WhyEventDto
    {
        public ulong EventId { get; init; }
        public uint TaskId { get; init; }
        public string Kind { get; init; } = string.Empty;
        public long LamportClock { get; init; }
        public string Timestamp { get; init; } = string.Empty;
        public string? File { get; init; }
        public int? Line { get; init; }
        public string? Function { get; init; }
        public Dictionary<string, string>? Attributes { get; init; }
    }

    private sealed record DiagnosticStoryDto
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public ulong TargetEventId { get; init; }
        public DiagnosticTaskDto TargetTask { get; init; } = new();
        public string? NarrativeId { get; init; }
        public string? BuildId { get; init; }
        public string? BundleId { get; init; }
        public List<WhyEventDto> Timeline { get; init; } = [];
        public List<WhyEventDto> CausalChain { get; init; } = [];
        public List<WhyEventDto> ConcurrentEvents { get; init; } = [];
        public List<CommunicationDto> Communications { get; init; } = [];
        public List<string> Evidence { get; init; } = [];
    }

    private sealed record DiagnosticTaskDto
    {
        public string SymbolId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool Resolved { get; init; }
    }

    private sealed record CorrelationDto
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public bool Matched { get; init; }
        public ulong? EventId { get; init; }
        public uint? TaskId { get; init; }
        public string? SymbolId { get; init; }
        public string Confidence { get; init; } = "none";
        public string? NarrativeId { get; init; }
        public List<string> Evidence { get; init; } = [];
    }

    private sealed record CommunicationEnvelopeDto
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public ulong EventId { get; init; }
        public List<CommunicationDto> Communications { get; init; } = [];
    }

    private sealed record CommunicationDto
    {
        public ulong EventId { get; init; }
        public string CommunicationId { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string PipelineSymbolId { get; init; } = string.Empty;
        public string SourceSymbolId { get; init; } = string.Empty;
        public string TargetSymbolId { get; init; } = string.Empty;
        public CommunicationSourceDto? Source { get; init; }
    }

    private sealed record CommunicationSourceDto
    {
        public string File { get; init; } = string.Empty;
        public int Line { get; init; }
        public int Column { get; init; }
    }
}
