namespace Junexis.Studio.Abstractions;

/// <summary>
/// Structured MCP message with type, correlation and payload.
/// Every inbound/outbound MCP interaction should be wrapped in this envelope.
/// </summary>
/// <param name="Type">Message type identifier (e.g. "tools/list", "completion/create").</param>
/// <param name="CorrelationId">
/// Unique correlation ID linking request to response. Null for unsolicited messages.
/// </param>
/// <param name="Payload">Optional JSON payload. Treated as untrusted data.</param>
public record McpEnvelope(string Type, string? CorrelationId = null, string? Payload = null);
