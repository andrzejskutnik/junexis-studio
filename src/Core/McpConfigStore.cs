using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// In-memory implementation of <see cref="IMcpConfigStore"/>.
/// Supports two tiers: global defaults and workspace overrides.
/// <c>Get</c> returns workspace override if present, else global default.
/// Thread-safe via locking.
/// </summary>
public sealed class McpConfigStore : IMcpConfigStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, McpConnectorConfig> _global = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, McpConnectorConfig> _workspace = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOutput? _output;

    /// <summary>Output channel name for MCP configuration messages.</summary>
    public const string OutputChannel = "MCP";

    /// <summary>
    /// Creates a new <see cref="McpConfigStore"/>.
    /// </summary>
    /// <param name="output">Optional output for logging configuration changes.</param>
    public McpConfigStore(IOutput? output = null)
    {
        _output = output;
    }

    /// <inheritdoc />
    public McpConnectorConfig? Get(string connectorId)
    {
        ArgumentNullException.ThrowIfNull(connectorId);

        lock (_lock)
        {
            if (_workspace.TryGetValue(connectorId, out var ws))
                return ws;

            if (_global.TryGetValue(connectorId, out var gl))
                return gl;

            return null;
        }
    }

    /// <inheritdoc />
    public void SetGlobal(McpConnectorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_lock)
        {
            _global[config.ConnectorId] = config;
        }

        _output?.Write(OutputChannel, $"[Config] Global config set for '{config.ConnectorId}'.");
    }

    /// <inheritdoc />
    public void SetWorkspace(McpConnectorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_lock)
        {
            _workspace[config.ConnectorId] = config;
        }

        _output?.Write(OutputChannel, $"[Config] Workspace override set for '{config.ConnectorId}'.");
    }

    /// <inheritdoc />
    public void RemoveWorkspaceOverride(string connectorId)
    {
        ArgumentNullException.ThrowIfNull(connectorId);

        bool removed;
        lock (_lock)
        {
            removed = _workspace.Remove(connectorId);
        }

        if (removed)
            _output?.Write(OutputChannel, $"[Config] Workspace override removed for '{connectorId}'.");
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllConnectorIds()
    {
        lock (_lock)
        {
            var ids = new HashSet<string>(_global.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in _workspace.Keys)
                ids.Add(key);
            return ids.ToArray();
        }
    }
}
