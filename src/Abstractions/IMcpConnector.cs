namespace Junexis.Studio.Abstractions;

/// <summary>
/// Defines the contract for an MCP (Model Context Protocol) connector.
/// Each MCP integration is an extension that provides its own connector instance.
/// Connectors publish events to <see cref="IEventBus"/> and report status to <see cref="IOutput"/>.
/// </summary>
public interface IMcpConnector
{
    /// <summary>
    /// Gets the unique identifier of this connector (e.g. "openai-mcp", "local-mcp").
    /// </summary>
    string ConnectorId { get; }

    /// <summary>
    /// Gets a value indicating whether this connector is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Establishes a connection to the MCP endpoint.
    /// Publishes <see cref="McpConnectedEvent"/> on success.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task representing the asynchronous connect operation.</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the MCP endpoint.
    /// Publishes <see cref="McpDisconnectedEvent"/> on completion.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task representing the asynchronous disconnect operation.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request to the MCP endpoint and returns the response.
    /// Publishes <see cref="McpMessageReceivedEvent"/> when a response arrives.
    /// On failure, publishes <see cref="McpErrorEvent"/>.
    /// </summary>
    /// <param name="request">The MCP request to send.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The MCP response.</returns>
    Task<McpResponse> SendAsync(McpRequest request, CancellationToken cancellationToken = default);
}
