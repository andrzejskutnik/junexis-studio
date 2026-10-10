namespace Junexis.Studio.Abstractions;

/// <summary>
/// Published on <see cref="IEventBus"/> when an MCP connector successfully connects.
/// </summary>
/// <param name="ConnectorId">The identifier of the connector that connected.</param>
public record McpConnectedEvent(string ConnectorId);

/// <summary>
/// Published on <see cref="IEventBus"/> when an MCP connector disconnects.
/// </summary>
/// <param name="ConnectorId">The identifier of the connector that disconnected.</param>
/// <param name="Reason">Optional reason for disconnection (e.g. user-initiated, error).</param>
public record McpDisconnectedEvent(string ConnectorId, string? Reason = null);

/// <summary>
/// Published on <see cref="IEventBus"/> when a response is received from an MCP endpoint.
/// </summary>
/// <param name="ConnectorId">The identifier of the connector that received the message.</param>
/// <param name="Method">The method that was originally invoked.</param>
/// <param name="Response">The received response.</param>
public record McpMessageReceivedEvent(string ConnectorId, string Method, McpResponse Response);

/// <summary>
/// Published on <see cref="IEventBus"/> when an MCP connector encounters an error.
/// </summary>
/// <param name="ConnectorId">The identifier of the connector that encountered the error.</param>
/// <param name="Error">Description of the error.</param>
public record McpErrorEvent(string ConnectorId, string Error);
