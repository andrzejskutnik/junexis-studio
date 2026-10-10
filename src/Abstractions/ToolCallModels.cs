namespace Junexis.Studio.Abstractions;

/// <summary>
/// Represents a request to invoke an MCP tool.
/// </summary>
/// <param name="ToolName">Name of the tool to invoke.</param>
/// <param name="Payload">JSON payload for the tool call (may be null).</param>
/// <param name="TimeoutMs">Timeout in milliseconds (0 = no timeout).</param>
public sealed record ToolCallRequest(string ToolName, string? Payload = null, int TimeoutMs = 30000);

/// <summary>
/// Result of an MCP tool call.
/// </summary>
/// <param name="Success">Whether the tool call succeeded.</param>
/// <param name="Result">The tool's response payload (may be null).</param>
/// <param name="Error">Error message if the call failed.</param>
/// <param name="DurationMs">Elapsed time in milliseconds.</param>
public sealed record ToolCallResult(bool Success, string? Result = null, string? Error = null, long DurationMs = 0)
{
    /// <summary>Creates a successful result.</summary>
    public static ToolCallResult Ok(string? result, long durationMs = 0) => new(true, result, null, durationMs);

    /// <summary>Creates a failed result.</summary>
    public static ToolCallResult Fail(string error, long durationMs = 0) => new(false, null, error, durationMs);
}
