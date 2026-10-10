using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Helper for MCP connectors to publish validated context updates to <see cref="IEventBus"/>.
/// Validates envelope before publishing and logs anomalies to <see cref="IOutput"/>.
/// </summary>
public sealed class McpContextPublisher
{
    private readonly IEventBus _eventBus;
    private readonly McpMessageValidator _validator;
    private readonly IOutput? _output;

    /// <summary>Output channel name.</summary>
    public const string OutputChannel = "MCP";

    /// <summary>
    /// Creates a new <see cref="McpContextPublisher"/>.
    /// </summary>
    /// <param name="eventBus">The event bus to publish context events on.</param>
    /// <param name="validator">The message validator for payload sanity checks.</param>
    /// <param name="output">Optional output for logging.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="eventBus"/> or <paramref name="validator"/> is null.
    /// </exception>
    public McpContextPublisher(IEventBus eventBus, McpMessageValidator validator, IOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(validator);
        _eventBus = eventBus;
        _validator = validator;
        _output = output;
    }

    /// <summary>
    /// Validates the payload and publishes an <see cref="McpContextUpdatedEvent"/>.
    /// Returns <see langword="false"/> and logs anomaly if validation fails.
    /// </summary>
    /// <param name="connectorId">The source connector ID.</param>
    /// <param name="contextType">The context type label.</param>
    /// <param name="payload">The JSON payload to publish.</param>
    /// <returns><see langword="true"/> if the event was published; <see langword="false"/> otherwise.</returns>
    public bool TryPublish(string connectorId, string contextType, string payload)
    {
        if (string.IsNullOrWhiteSpace(connectorId))
        {
            _output?.Write(OutputChannel, "[Context] Rejected: connectorId is empty.", OutputSeverity.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(contextType))
        {
            _output?.Write(OutputChannel, "[Context] Rejected: contextType is empty.", OutputSeverity.Warning);
            return false;
        }

        // Validate payload as an MCP envelope for sanity
        var envelope = new McpEnvelope($"context/{contextType}", Payload: payload);
        var validation = _validator.Validate(envelope);

        if (!validation.IsValid)
        {
            _output?.Write(OutputChannel,
                $"[Context] Rejected from '{connectorId}': {string.Join("; ", validation.Errors)}",
                OutputSeverity.Warning);
            return false;
        }

        var evt = new McpContextUpdatedEvent(connectorId, contextType, payload, DateTime.UtcNow);
        _eventBus.Publish(evt);

        _output?.Write(OutputChannel,
            $"[Context] Published '{contextType}' from '{connectorId}' ({payload.Length} chars).");
        return true;
    }
}
