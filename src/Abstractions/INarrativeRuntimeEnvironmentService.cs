namespace Junexis.Studio.Abstractions;

/// <summary>Runtime environment that binds a launched artifact to one managed Narrative session.</summary>
/// <param name="TraceUrl">Base URL of the healthy TraceCollector.</param>
/// <param name="BuildId">Verified compiler build represented by the collector.</param>
public sealed record NarrativeRuntimeEnvironment(string TraceUrl, string BuildId)
{
    /// <summary>Name of the TraceCollector URL environment variable.</summary>
    public const string TraceUrlVariable = "JUNEXIS_TRACE_URL";

    /// <summary>Name of the compiler build identity environment variable.</summary>
    public const string BuildIdVariable = "JUNEXIS_BUILD_ID";

    /// <summary>Returns the complete, atomic child-process environment pair.</summary>
    public IReadOnlyDictionary<string, string?> ToProcessVariables() =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [TraceUrlVariable] = TraceUrl,
            [BuildIdVariable] = BuildId,
        };

    /// <summary>Returns the complete, atomic DAP launch environment pair.</summary>
    public IReadOnlyDictionary<string, string> ToDebugAdapterVariables() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TraceUrlVariable] = TraceUrl,
            [BuildIdVariable] = BuildId,
        };
}

/// <summary>Resolves the environment of the healthy Studio-managed Narrative session.</summary>
public interface INarrativeRuntimeEnvironmentService
{
    /// <summary>Whether a managed session is currently starting or switching builds.</summary>
    bool IsPreparationPending { get; }

    /// <summary>
    /// Returns a runtime environment only when the managed collector is ready and has
    /// both a valid loopback URL and an exact compiler build identity.
    /// </summary>
    NarrativeRuntimeEnvironment? GetCurrent();

    /// <summary>
    /// Waits up to <paramref name="timeout"/> while the managed session is starting
    /// or switching builds, then returns the verified environment. Timeout and a
    /// terminal Narrative state return <see langword="null"/>; caller cancellation
    /// remains observable as <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="timeout">Maximum preparation time.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    Task<NarrativeRuntimeEnvironment?> WaitForCurrentAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
