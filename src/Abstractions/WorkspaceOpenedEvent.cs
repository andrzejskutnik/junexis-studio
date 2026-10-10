namespace Junexis.Studio.Abstractions;

/// <summary>
/// Domain event: workspace folder has been opened.
/// </summary>
/// <param name="RootPath">Absolute path to the workspace root directory.</param>
public sealed record WorkspaceOpenedEvent(string RootPath);
