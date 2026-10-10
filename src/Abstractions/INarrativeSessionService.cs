namespace Junexis.Studio.Abstractions;

/// <summary>Lifecycle state of the Studio-managed Narrative backend.</summary>
public enum NarrativeSessionState
{
    /// <summary>No TraceCollector process is managed by Studio.</summary>
    Inactive,

    /// <summary>TraceCollector is starting and its health endpoint is being verified.</summary>
    Starting,

    /// <summary>TraceCollector is healthy and bound to the current compiler build.</summary>
    Ready,

    /// <summary>TraceCollector is being stopped.</summary>
    Stopping,

    /// <summary>The managed session could not start or lost its backend process.</summary>
    Failed
}

/// <summary>Observed runtime activity of the Studio-managed Narrative session.</summary>
public enum NarrativeRuntimeActivity
{
    /// <summary>No managed runtime observation is active.</summary>
    Inactive,
    /// <summary>The collector is starting or changing its bound build.</summary>
    Preparing,
    /// <summary>The collector is healthy but the active build has not emitted events.</summary>
    WaitingForEvents,
    /// <summary>The collector has observed runtime events for the active build.</summary>
    Live,
    /// <summary>The collector process or its monitoring loop is unavailable.</summary>
    Disconnected
}

/// <summary>Observable state of one managed Narrative session.</summary>
/// <param name="State">Current lifecycle state.</param>
/// <param name="Status">Human-readable, evidence-based status.</param>
/// <param name="BaseUrl">Loopback TraceCollector URL, when allocated.</param>
/// <param name="BuildId">Compiler build represented by the session.</param>
/// <param name="DebugManifestPath">Debug manifest loaded by TraceCollector.</param>
/// <param name="LatestEventId">Newest event captured for <paramref name="BuildId"/>.</param>
/// <param name="TotalEvents">Total number of events currently retained by the collector.</param>
/// <param name="Activity">Current runtime activity classification.</param>
/// <param name="EventDelta">Number of events observed since the previous successful refresh.</param>
/// <param name="LastEventObservedAt">Studio timestamp of the most recently observed new event.</param>
public sealed record NarrativeSessionSnapshot(
    NarrativeSessionState State,
    string Status,
    string? BaseUrl,
    string? BuildId,
    string? DebugManifestPath,
    ulong? LatestEventId,
    long TotalEvents,
    NarrativeRuntimeActivity Activity = NarrativeRuntimeActivity.Inactive,
    long EventDelta = 0,
    DateTimeOffset? LastEventObservedAt = null);

/// <summary>Controls the TraceCollector process associated with the latest verified Junexis build.</summary>
public interface INarrativeSessionService : IAsyncDisposable
{
    /// <summary>Gets the latest observable session state.</summary>
    NarrativeSessionSnapshot Snapshot { get; }

    /// <summary>Raised whenever the observable session state changes.</summary>
    event Action<NarrativeSessionSnapshot>? SnapshotChanged;

    /// <summary>Starts or switches the managed TraceCollector to a verified build receipt.</summary>
    /// <param name="receipt">Verified latest-build receipt containing the debug manifest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(
        LatestBuildReceipt receipt,
        CancellationToken cancellationToken = default);

    /// <summary>Restarts the session for the most recently observed verified build.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RestartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the managed TraceCollector process.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Refreshes health, event count, and the latest event for the active build.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
