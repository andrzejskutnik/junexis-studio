namespace Junexis.Studio.Abstractions;

/// <summary>Operation selected for a language launch profile.</summary>
public enum LaunchOperation
{
    /// <summary>Builds the selected project or source.</summary>
    Build,

    /// <summary>Runs the selected project or source.</summary>
    Run,

    /// <summary>Starts the selected project under a debugger.</summary>
    Debug,
}

/// <summary>Describes commands and toolchain requirements for one language family.</summary>
/// <param name="Id">Stable profile identifier.</param>
/// <param name="DisplayName">Human-readable profile name.</param>
/// <param name="FileExtensions">Supported lowercase file extensions.</param>
/// <param name="BuildCommandId">Registered build command, if supported.</param>
/// <param name="RunCommandId">Registered run command, if supported.</param>
/// <param name="DebugCommandId">Registered debug command, if supported.</param>
/// <param name="BuildCapabilities">Capability identifiers required to build.</param>
/// <param name="RunCapabilities">Capability identifiers required to run.</param>
/// <param name="DebugCapabilities">Capability identifiers required to debug.</param>
public sealed record LaunchProfile(
    string Id,
    string DisplayName,
    IReadOnlyList<string> FileExtensions,
    string? BuildCommandId,
    string? RunCommandId,
    string? DebugCommandId,
    IReadOnlyList<string> BuildCapabilities,
    IReadOnlyList<string> RunCapabilities,
    IReadOnlyList<string> DebugCapabilities);

/// <summary>Result of validating one profile operation against toolchain capabilities.</summary>
/// <param name="IsValid">Whether the operation can be started.</param>
/// <param name="CommandId">Command selected for the operation.</param>
/// <param name="MissingCapabilities">Unavailable capability identifiers.</param>
/// <param name="Message">Actionable validation summary.</param>
public sealed record LaunchProfileValidation(
    bool IsValid,
    string? CommandId,
    IReadOnlyList<string> MissingCapabilities,
    string Message);

/// <summary>Resolves language launch profiles and validates their toolchain requirements.</summary>
public interface ILaunchProfileRegistry
{
    /// <summary>Returns all registered launch profiles in deterministic order.</summary>
    /// <returns>The registered profiles.</returns>
    IReadOnlyList<LaunchProfile> GetProfiles();

    /// <summary>Finds the profile associated with a source or project path.</summary>
    /// <param name="path">Source or project path.</param>
    /// <returns>The matching profile, or <see langword="null"/>.</returns>
    LaunchProfile? FindForPath(string? path);

    /// <summary>Validates a profile operation against the latest Doctor snapshot.</summary>
    /// <param name="profile">Profile to validate.</param>
    /// <param name="operation">Requested operation.</param>
    /// <param name="snapshot">Latest capability snapshot; null means it is not available yet.</param>
    /// <returns>The selected command and validation result.</returns>
    LaunchProfileValidation Validate(
        LaunchProfile profile,
        LaunchOperation operation,
        ToolchainDoctorSnapshot? snapshot);
}
