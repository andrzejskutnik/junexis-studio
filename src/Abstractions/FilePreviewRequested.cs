namespace Junexis.Studio.Abstractions;

/// <summary>Published to <see cref="IEventBus"/> when the user single-clicks a file in Explorer (preview open).</summary>
public sealed record FilePreviewRequested(string Path);
