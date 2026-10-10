namespace Junexis.Studio.Abstractions;

/// <summary>
/// Published on <see cref="IEventBus"/> when a compiler (or other source)
/// updates the diagnostics set for its <see cref="Source"/>.
/// Consumers (e.g. Problems panel) should refresh their view.
/// </summary>
/// <param name="Source">
/// The source/owner that produced the diagnostics (e.g. CompilerId, McpConnectorId).
/// </param>
/// <param name="Diagnostics">The current set of diagnostics from this source.</param>
/// <param name="BuildId">Optional compiler build identity owning this diagnostic set.</param>
public record DiagnosticsUpdatedEvent(
    string Source,
    IReadOnlyList<Diagnostic> Diagnostics,
    string? BuildId = null);
