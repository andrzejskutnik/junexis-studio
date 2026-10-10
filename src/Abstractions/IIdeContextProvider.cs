namespace Junexis.Studio.Abstractions;

/// <summary>
/// Provides IDE context data for the assistant (OMWiA).
/// Returns a standardised snapshot of the current IDE state
/// (active document, selection, workspace root, open editors).
/// </summary>
public interface IIdeContextProvider
{
    /// <summary>
    /// Builds a snapshot of the current IDE context.
    /// The snapshot respects the configured <see cref="ContextPolicy"/>.
    /// </summary>
    /// <returns>A snapshot of the IDE context.</returns>
    IdeContextSnapshot GetSnapshot();

    /// <summary>
    /// Gets or sets the policy controlling which fields are included.
    /// </summary>
    ContextPolicy Policy { get; set; }
}

/// <summary>
/// Standardised snapshot of the IDE context for the assistant.
/// </summary>
/// <param name="WorkspaceRoot">Root path of the workspace (null if not opened).</param>
/// <param name="ActiveFilePath">Path of the currently focused file (null if none).</param>
/// <param name="ActiveFileContent">Content of the active file (null if excluded by policy or none open).</param>
/// <param name="Selection">Currently selected text (null if none or excluded by policy).</param>
/// <param name="OpenEditorPaths">Paths of all open editor tabs.</param>
public sealed record IdeContextSnapshot(
    string? WorkspaceRoot,
    string? ActiveFilePath,
    string? ActiveFileContent,
    string? Selection,
    IReadOnlyList<string> OpenEditorPaths);

/// <summary>
/// Policy controlling what IDE context data is automatically included in the assistant prompt.
/// </summary>
/// <param name="IncludeWorkspaceRoot">Whether to include the workspace root path.</param>
/// <param name="IncludeActiveFilePath">Whether to include the active file path.</param>
/// <param name="IncludeActiveFileContent">Whether to include the active file content.</param>
/// <param name="IncludeSelection">Whether to include the current text selection.</param>
/// <param name="IncludeOpenEditors">Whether to include the list of open editor paths.</param>
/// <param name="MaxContentLength">Maximum number of characters for file content (0 = unlimited).</param>
public sealed record ContextPolicy(
    bool IncludeWorkspaceRoot = true,
    bool IncludeActiveFilePath = true,
    bool IncludeActiveFileContent = false,
    bool IncludeSelection = true,
    bool IncludeOpenEditors = true,
    int MaxContentLength = 10_000);
