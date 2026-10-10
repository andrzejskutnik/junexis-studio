namespace Junexis.Studio.Abstractions;

/// <summary>
/// Immutable configuration for an MCP connector.
/// Stored per-workspace (overrides) or globally (defaults).
/// Core does not interpret the content — connectors consume it.
/// </summary>
/// <param name="ConnectorId">The connector this config applies to.</param>
/// <param name="Endpoint">The MCP endpoint address/URI.</param>
/// <param name="Token">
/// Optional authentication token. Must NEVER be logged to Output.
/// </param>
/// <param name="Mode">Operating mode (e.g. "dev", "prod"). Defaults to "dev".</param>
/// <param name="TransportParameters">
/// Optional key-value pairs for transport-specific settings.
/// </param>
public record McpConnectorConfig(
    string ConnectorId,
    string Endpoint,
    string? Token = null,
    string Mode = "dev",
    IReadOnlyDictionary<string, string>? TransportParameters = null);
