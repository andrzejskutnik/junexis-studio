using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Default implementation of <see cref="IIdeContextProvider"/>.
/// Collects IDE state from callback delegates and applies the configured <see cref="ContextPolicy"/>.
/// </summary>
public sealed class IdeContextProvider : IIdeContextProvider
{
    /// <summary>
    /// Delegate that supplies the current workspace root path (or null).
    /// </summary>
    public Func<string?>? WorkspaceRootProvider { get; set; }

    /// <summary>
    /// Delegate that supplies the active file path (or null).
    /// </summary>
    public Func<string?>? ActiveFilePathProvider { get; set; }

    /// <summary>
    /// Delegate that reads file content by path (or null for no-op).
    /// </summary>
    public Func<string, string?>? FileContentReader { get; set; }

    /// <summary>
    /// Delegate that supplies the current text selection (or null).
    /// </summary>
    public Func<string?>? SelectionProvider { get; set; }

    /// <summary>
    /// Delegate that supplies the list of open editor paths.
    /// </summary>
    public Func<IReadOnlyList<string>>? OpenEditorsProvider { get; set; }

    /// <inheritdoc />
    public ContextPolicy Policy { get; set; } = new();

    /// <inheritdoc />
    public IdeContextSnapshot GetSnapshot()
    {
        var wsRoot = Policy.IncludeWorkspaceRoot
            ? WorkspaceRootProvider?.Invoke()
            : null;

        var activeFile = Policy.IncludeActiveFilePath
            ? ActiveFilePathProvider?.Invoke()
            : null;

        string? content = null;
        if (Policy.IncludeActiveFileContent && activeFile is not null && FileContentReader is not null)
        {
            content = FileContentReader(activeFile);
            if (content is not null && Policy.MaxContentLength > 0 && content.Length > Policy.MaxContentLength)
            {
                content = content[..Policy.MaxContentLength];
            }
        }

        var selection = Policy.IncludeSelection
            ? SelectionProvider?.Invoke()
            : null;

        var editors = Policy.IncludeOpenEditors
            ? OpenEditorsProvider?.Invoke() ?? (IReadOnlyList<string>)[]
            : (IReadOnlyList<string>)[];

        return new IdeContextSnapshot(wsRoot, activeFile, content, selection, editors);
    }
}
