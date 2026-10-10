using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Thread-safe implementation of <see cref="IMcpConnectorRegistry"/>.
/// Manages multiple <see cref="IMcpConnector"/> instances with active connector selection.
/// Connector IDs are case-insensitive.
/// </summary>
public sealed class McpConnectorRegistry : IMcpConnectorRegistry
{
    private readonly Dictionary<string, IMcpConnector> _connectors = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly IOutput? _output;
    private string? _activeConnectorId;

    /// <summary>Output channel name for MCP connector registry messages.</summary>
    public const string OutputChannel = "MCP";

    /// <summary>
    /// Creates a new <see cref="McpConnectorRegistry"/>.
    /// </summary>
    /// <param name="output">Optional output service for logging.</param>
    public McpConnectorRegistry(IOutput? output = null)
    {
        _output = output;
    }

    /// <inheritdoc />
    public void Register(IMcpConnector connector)
    {
        ArgumentNullException.ThrowIfNull(connector);

        lock (_lock)
        {
            if (_connectors.ContainsKey(connector.ConnectorId))
                throw new InvalidOperationException($"Connector '{connector.ConnectorId}' is already registered.");

            _connectors[connector.ConnectorId] = connector;
        }

        _output?.Write(OutputChannel, $"Connector registered: {connector.ConnectorId}.");
    }

    /// <inheritdoc />
    public bool Unregister(string connectorId)
    {
        ArgumentNullException.ThrowIfNull(connectorId);

        bool removed;
        lock (_lock)
        {
            removed = _connectors.Remove(connectorId);

            // Clear active if the removed connector was active
            if (removed && string.Equals(_activeConnectorId, connectorId, StringComparison.OrdinalIgnoreCase))
                _activeConnectorId = null;
        }

        if (removed)
            _output?.Write(OutputChannel, $"Connector unregistered: {connectorId}.");

        return removed;
    }

    /// <inheritdoc />
    public IMcpConnector? Get(string connectorId)
    {
        ArgumentNullException.ThrowIfNull(connectorId);

        lock (_lock)
        {
            return _connectors.TryGetValue(connectorId, out var c) ? c : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllIds()
    {
        lock (_lock)
        {
            return _connectors.Keys.ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IMcpConnector> GetAll()
    {
        lock (_lock)
        {
            return _connectors.Values.ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IMcpConnector> GetConnected()
    {
        lock (_lock)
        {
            return _connectors.Values.Where(c => c.IsConnected).ToList();
        }
    }

    /// <inheritdoc />
    public string? ActiveConnectorId
    {
        get
        {
            lock (_lock) return _activeConnectorId;
        }
        set
        {
            lock (_lock)
            {
                if (value is not null && !_connectors.ContainsKey(value))
                    throw new InvalidOperationException($"Cannot set active connector: '{value}' is not registered.");

                _activeConnectorId = value;
            }

            _output?.Write(OutputChannel, $"Active connector changed to: {value ?? "(none)"}.");
        }
    }

    /// <inheritdoc />
    public IMcpConnector? GetActive()
    {
        lock (_lock)
        {
            return _activeConnectorId is not null && _connectors.TryGetValue(_activeConnectorId, out var c) ? c : null;
        }
    }
}
