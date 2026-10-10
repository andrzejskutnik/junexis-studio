namespace Junexis.Studio.Abstractions;

/// <summary>
/// Provides read access to MCP connector configurations.
/// Supports a two-tier model: global defaults + workspace overrides.
/// </summary>
public interface IMcpConfigStore
{
    /// <summary>
    /// Gets the effective configuration for the given connector (workspace override > global default).
    /// </summary>
    /// <param name="connectorId">The connector identifier.</param>
    /// <returns>The configuration, or null if none is defined.</returns>
    McpConnectorConfig? Get(string connectorId);

    /// <summary>
    /// Sets a global (default) configuration for a connector.
    /// </summary>
    /// <param name="config">The configuration to store.</param>
    void SetGlobal(McpConnectorConfig config);

    /// <summary>
    /// Sets a workspace-level override for a connector.
    /// </summary>
    /// <param name="config">The configuration to store.</param>
    void SetWorkspace(McpConnectorConfig config);

    /// <summary>
    /// Removes the workspace-level override for a connector, falling back to global.
    /// </summary>
    /// <param name="connectorId">The connector identifier.</param>
    void RemoveWorkspaceOverride(string connectorId);

    /// <summary>
    /// Returns all known connector IDs (from both global and workspace stores).
    /// </summary>
    /// <returns>Distinct connector IDs.</returns>
    IReadOnlyList<string> GetAllConnectorIds();
}
