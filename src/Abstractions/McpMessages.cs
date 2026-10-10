namespace Junexis.Studio.Abstractions;

/// <summary>
/// Represents a request sent to an MCP endpoint.
/// </summary>
/// <param name="Method">The method/command name to invoke on the MCP endpoint.</param>
/// <param name="Payload">Optional JSON payload for the request.</param>
public record McpRequest(string Method, string? Payload = null);

/// <summary>
/// Represents a response received from an MCP endpoint.
/// </summary>
/// <param name="Success">Whether the request completed successfully.</param>
/// <param name="Payload">Optional JSON payload of the response.</param>
/// <param name="Error">Error message if the request failed; null on success.</param>
public record McpResponse(bool Success, string? Payload = null, string? Error = null)
{
    /// <summary>
    /// Creates a successful response with the given payload.
    /// </summary>
    /// <param name="payload">The response payload.</param>
    /// <returns>A successful <see cref="McpResponse"/>.</returns>
    public static McpResponse Ok(string? payload = null) => new(true, payload);

    /// <summary>
    /// Creates a failure response with the given error message.
    /// </summary>
    /// <param name="error">The error description.</param>
    /// <returns>A failed <see cref="McpResponse"/>.</returns>
    public static McpResponse Fail(string error) => new(false, Error: error);
}
