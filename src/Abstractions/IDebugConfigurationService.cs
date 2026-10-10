namespace Junexis.Studio.Abstractions;

/// <summary>Kind of DAP request represented by a persisted debug configuration.</summary>
public enum DebugConfigurationRequest
{
    /// <summary>Starts a new debuggee.</summary>
    Launch,
    /// <summary>Attaches to an existing process.</summary>
    Attach,
}

/// <summary>Persisted launch or attach configuration for one debugger profile.</summary>
/// <param name="Id">Stable configuration identifier.</param>
/// <param name="Name">Human-readable configuration name.</param>
/// <param name="ProfileId">Debugger profile such as cpp, go, or typescript.</param>
/// <param name="Request">Launch or attach operation.</param>
/// <param name="Program">Optional program or script path for launch.</param>
/// <param name="ProcessId">Optional process identifier for attach.</param>
/// <param name="WorkingDirectory">Optional working directory; workspace root is the fallback.</param>
/// <param name="StopOnEntry">Whether launch should stop at the entry point.</param>
/// <param name="Arguments">Program arguments used for launch.</param>
public sealed record DebugLaunchConfiguration(
    string Id,
    string Name,
    string ProfileId,
    DebugConfigurationRequest Request,
    string? Program = null,
    int? ProcessId = null,
    string? WorkingDirectory = null,
    bool StopOnEntry = false,
    IReadOnlyList<string>? Arguments = null);

/// <summary>Stores concrete debug launch and attach configurations per workspace.</summary>
public interface IDebugConfigurationService
{
    /// <summary>Returns configurations in deterministic display order.</summary>
    IReadOnlyList<DebugLaunchConfiguration> GetConfigurations();

    /// <summary>Gets the selected configuration identifier.</summary>
    string? SelectedId { get; }

    /// <summary>Adds or replaces a configuration.</summary>
    /// <param name="configuration">Configuration to persist.</param>
    void Upsert(DebugLaunchConfiguration configuration);

    /// <summary>Removes a configuration.</summary>
    /// <param name="id">Stable configuration identifier.</param>
    void Remove(string id);

    /// <summary>Selects a configuration and persists the selection.</summary>
    /// <param name="id">Configuration identifier, or null.</param>
    void Select(string? id);

    /// <summary>Raised when configurations or selection change.</summary>
    event Action? ConfigurationsChanged;
}

/// <summary>Local process that may be selected as an attach target.</summary>
/// <param name="Id">Operating-system process identifier.</param>
/// <param name="Name">Process name.</param>
/// <param name="ExecutablePath">Executable path when accessible.</param>
public sealed record DebugProcessInfo(int Id, string Name, string? ExecutablePath);

/// <summary>Enumerates local processes available to attach configurations.</summary>
public interface IDebugProcessService
{
    /// <summary>Returns accessible local processes ordered by name and identifier.</summary>
    IReadOnlyList<DebugProcessInfo> GetProcesses();
}

/// <summary>Strength of evidence used to associate a process with a debugger profile.</summary>
public enum DebugTargetConfidence
{
    /// <summary>No reliable profile evidence is available.</summary>
    Unknown,
    /// <summary>The profile was inferred from a well-known runtime process.</summary>
    Heuristic,
    /// <summary>The process path was correlated with a versioned Junexis build inventory.</summary>
    BuildArtifact,
}

/// <summary>Narrative-aware explanation of why a process fits a debugger profile.</summary>
/// <param name="Process">Assessed local process.</param>
/// <param name="RecommendedProfileId">Recommended profile, or null when evidence is insufficient.</param>
/// <param name="Confidence">Strength of the recommendation.</param>
/// <param name="RequestedProfileMatches">Whether the currently requested profile matches the recommendation.</param>
/// <param name="BuildId">Correlated Junexis build identifier, when available.</param>
/// <param name="SourceCorrelationReady">Whether versioned source mapping can connect runtime locations to Junexis.</param>
/// <param name="Summary">Short user-facing assessment.</param>
/// <param name="Evidence">Deterministic evidence behind the assessment.</param>
public sealed record DebugTargetAssessment(
    DebugProcessInfo Process,
    string? RecommendedProfileId,
    DebugTargetConfidence Confidence,
    bool? RequestedProfileMatches,
    string? BuildId,
    bool SourceCorrelationReady,
    string Summary,
    IReadOnlyList<string> Evidence);

/// <summary>Correlates attach targets with debugger profiles and Junexis narrative evidence.</summary>
public interface IDebugTargetAssessmentService
{
    /// <summary>Explains the best known debugger profile and source-correlation readiness for a process.</summary>
    /// <param name="process">Process selected as an attach target.</param>
    /// <param name="requestedProfileId">Profile currently selected by the user.</param>
    /// <returns>Evidence-based target assessment.</returns>
    DebugTargetAssessment Assess(DebugProcessInfo process, string? requestedProfileId = null);
}
