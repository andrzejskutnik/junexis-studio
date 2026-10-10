namespace Junexis.Studio.Abstractions;

/// <summary>
/// Configuration for the auto-build feature.
/// </summary>
/// <param name="Enabled">Whether auto-build is active.</param>
/// <param name="DebounceMs">Debounce delay in milliseconds before triggering a build after a save event.</param>
public sealed record AutoBuildConfig(bool Enabled = false, int DebounceMs = 500);
