namespace Junexis.Studio.Abstractions;

/// <summary>
/// Registry and manager for multiple <see cref="IMcpConnector"/> instances.
/// Supports concurrent connectors with capability-based lookup and active connector selection.
/// </summary>
public interface IMcpConnectorRegistry
{
    /// <summary>
    /// Registers a connector in the registry.
    /// </summary>
    /// <param name="connector">The connector to register.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="connector"/> is null.</exception>
    /// <exception cref="InvalidOperationException">When a connector with the same <see cref="IMcpConnector.ConnectorId"/> is already registered.</exception>
    void Register(IMcpConnector connector);

    /// <summary>
    /// Removes a connector from the registry.
    /// </summary>
    /// <param name="connectorId">The connector ID to remove.</param>
    /// <returns><c>true</c> if the connector was found and removed; otherwise <c>false</c>.</returns>
    bool Unregister(string connectorId);

    /// <summary>
    /// Gets a connector by its ID.
    /// </summary>
    /// <param name="connectorId">The connector ID.</param>
    /// <returns>The connector, or <c>null</c> if not found.</returns>
    IMcpConnector? Get(string connectorId);

    /// <summary>
    /// Gets all registered connector IDs.
    /// </summary>
    /// <returns>A snapshot of all registered connector IDs.</returns>
    IReadOnlyList<string> GetAllIds();

    /// <summary>
    /// Gets all registered connectors.
    /// </summary>
    /// <returns>A snapshot of all registered connectors.</returns>
    IReadOnlyList<IMcpConnector> GetAll();

    /// <summary>
    /// Gets all currently connected connectors.
    /// </summary>
    /// <returns>A snapshot of currently connected connectors.</returns>
    IReadOnlyList<IMcpConnector> GetConnected();

    /// <summary>
    /// Gets or sets the currently active (default) connector ID.
    /// Can be <c>null</c> if no active connector is set.
    /// </summary>
    string? ActiveConnectorId { get; set; }

    /// <summary>
    /// Gets the currently active connector, or <c>null</c> if none is set or the active ID is invalid.
    /// </summary>
    /// <returns>The active connector, or <c>null</c>.</returns>
    IMcpConnector? GetActive();
}
