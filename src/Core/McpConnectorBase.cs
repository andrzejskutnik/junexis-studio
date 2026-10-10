using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Abstract base class for MCP connectors providing template-method wiring
/// of <see cref="IEventBus"/> events and <see cref="IOutput"/> logging.
/// Subclasses implement <see cref="DoConnectAsync"/>, <see cref="DoDisconnectAsync"/>
/// and <see cref="DoSendAsync"/> with endpoint-specific logic.
/// </summary>
public abstract class McpConnectorBase : IMcpConnector
{
    private readonly IEventBus? _eventBus;
    private readonly IOutput? _output;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of <see cref="McpConnectorBase"/>.
    /// </summary>
    /// <param name="connectorId">Unique identifier for this connector.</param>
    /// <param name="eventBus">Optional event bus for publishing MCP events.</param>
    /// <param name="output">Optional output service for status logging.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectorId"/> is null or whitespace.</exception>
    protected McpConnectorBase(string connectorId, IEventBus? eventBus = null, IOutput? output = null)
    {
        if (string.IsNullOrWhiteSpace(connectorId))
            throw new ArgumentException("ConnectorId must not be null or whitespace.", nameof(connectorId));

        ConnectorId = connectorId;
        _eventBus = eventBus;
        _output = output;
    }

    /// <inheritdoc />
    public string ConnectorId { get; }

    /// <inheritdoc />
    public bool IsConnected { get; private set; }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            return;

        await DoConnectAsync(cancellationToken).ConfigureAwait(false);

        lock (_lock) { IsConnected = true; }

        _output?.Write("MCP", $"[{ConnectorId}] Connected.");
        _eventBus?.Publish(new McpConnectedEvent(ConnectorId));
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            return;

        string? reason = null;
        try
        {
            await DoDisconnectAsync(cancellationToken).ConfigureAwait(false);
            reason = "user-initiated";
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            throw;
        }
        finally
        {
            lock (_lock) { IsConnected = false; }

            _output?.Write("MCP", $"[{ConnectorId}] Disconnected ({reason ?? "unknown"}).");
            _eventBus?.Publish(new McpDisconnectedEvent(ConnectorId, reason));
        }
    }

    /// <inheritdoc />
    public async Task<McpResponse> SendAsync(McpRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            return McpResponse.Fail("Connector is not connected.");

        try
        {
            var response = await DoSendAsync(request, cancellationToken).ConfigureAwait(false);
            _eventBus?.Publish(new McpMessageReceivedEvent(ConnectorId, request.Method, response));
            return response;
        }
        catch (Exception ex)
        {
            var errorMsg = $"SendAsync failed for method '{request.Method}': {ex.Message}";
            _output?.Write("MCP", $"[{ConnectorId}] {errorMsg}");
            _eventBus?.Publish(new McpErrorEvent(ConnectorId, errorMsg));
            return McpResponse.Fail(errorMsg);
        }
    }

    /// <summary>
    /// Performs endpoint-specific connection logic.
    /// Called by <see cref="ConnectAsync"/> only when not already connected.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected abstract Task DoConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Performs endpoint-specific disconnection logic.
    /// Called by <see cref="DisconnectAsync"/> only when currently connected.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected abstract Task DoDisconnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Performs endpoint-specific send logic.
    /// Called by <see cref="SendAsync"/> only when connected.
    /// </summary>
    /// <param name="request">The MCP request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from the MCP endpoint.</returns>
    protected abstract Task<McpResponse> DoSendAsync(McpRequest request, CancellationToken cancellationToken);
}
