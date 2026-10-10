namespace Junexis.Studio.Abstractions;

/// <summary>
/// Published on <see cref="IEventBus"/> when an MCP connector provides updated context
/// (e.g. documentation index, query results, runtime state, analytics data).
/// Payload is treated as untrusted; consumers must validate before using.
/// </summary>
/// <param name="ConnectorId">The MCP connector that published the context.</param>
/// <param name="ContextType">
/// A label describing the kind of context (e.g. "documentation-index", "runtime-state").
/// </param>
/// <param name="Payload">JSON payload with the context data. Treated as untrusted.</param>
/// <param name="TimestampUtc">UTC timestamp when the context was captured by the connector.</param>
public record McpContextUpdatedEvent(
    string ConnectorId,
    string ContextType,
    string Payload,
    DateTime TimestampUtc);
