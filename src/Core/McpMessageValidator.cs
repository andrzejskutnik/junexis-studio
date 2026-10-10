using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Validates inbound MCP messages (envelopes) applying security rules:
/// size limits, required fields, sanity checks.
/// Invalid payloads are reported to <see cref="IOutput"/> and never crash the host.
/// </summary>
public sealed class McpMessageValidator
{
    /// <summary>Default maximum allowed payload size in characters.</summary>
    public const int DefaultMaxPayloadSize = 1_048_576; // 1 MB

    /// <summary>Default maximum allowed Type field length.</summary>
    public const int DefaultMaxTypeLength = 256;

    private readonly int _maxPayloadSize;
    private readonly int _maxTypeLength;
    private readonly IOutput? _output;

    /// <summary>Output channel name for MCP validation messages.</summary>
    public const string OutputChannel = "MCP";

    /// <summary>
    /// Creates a new <see cref="McpMessageValidator"/>.
    /// </summary>
    /// <param name="output">Optional output for anomaly logging.</param>
    /// <param name="maxPayloadSize">Maximum payload size in characters.</param>
    /// <param name="maxTypeLength">Maximum Type field length.</param>
    public McpMessageValidator(
        IOutput? output = null,
        int maxPayloadSize = DefaultMaxPayloadSize,
        int maxTypeLength = DefaultMaxTypeLength)
    {
        _output = output;
        _maxPayloadSize = maxPayloadSize;
        _maxTypeLength = maxTypeLength;
    }

    /// <summary>
    /// Validates an MCP envelope. Returns a result indicating validity and any errors found.
    /// </summary>
    /// <param name="envelope">The envelope to validate. May be null.</param>
    /// <returns>A <see cref="McpValidationResult"/> with IsValid and Errors.</returns>
    public McpValidationResult Validate(McpEnvelope? envelope)
    {
        var errors = new List<string>();

        if (envelope is null)
        {
            errors.Add("Envelope is null.");
            LogAnomaly("Received null MCP envelope.");
            return new McpValidationResult(false, errors);
        }

        // Type: required, non-empty, bounded length.
        if (string.IsNullOrWhiteSpace(envelope.Type))
        {
            errors.Add("Type is required and must not be empty.");
        }
        else if (envelope.Type.Length > _maxTypeLength)
        {
            errors.Add($"Type exceeds maximum length ({_maxTypeLength}).");
        }

        // CorrelationId: optional but if present must be reasonable length.
        if (envelope.CorrelationId is not null && envelope.CorrelationId.Length > _maxTypeLength)
        {
            errors.Add($"CorrelationId exceeds maximum length ({_maxTypeLength}).");
        }

        // Payload: optional but must not exceed size limit.
        if (envelope.Payload is not null && envelope.Payload.Length > _maxPayloadSize)
        {
            errors.Add($"Payload exceeds maximum size ({_maxPayloadSize} chars).");
        }

        // Payload: if present, basic JSON sanity (starts with { or [).
        if (envelope.Payload is not null
            && envelope.Payload.Length > 0
            && envelope.Payload[0] != '{' && envelope.Payload[0] != '[')
        {
            errors.Add("Payload does not appear to be valid JSON (must start with '{' or '[').");
        }

        if (errors.Count > 0)
        {
            LogAnomaly($"Validation failed for type='{Truncate(envelope.Type, 60)}': {string.Join("; ", errors)}");
        }

        return new McpValidationResult(errors.Count == 0, errors);
    }

    private void LogAnomaly(string message)
    {
        _output?.Write(OutputChannel, $"[Validation] {message}", OutputSeverity.Warning);
    }

    private static string Truncate(string? value, int max)
    {
        if (value is null) return "(null)";
        return value.Length <= max ? value : value[..max] + "…";
    }
}

/// <summary>
/// Result of MCP envelope validation.
/// </summary>
/// <param name="IsValid">Whether the envelope passed all validation checks.</param>
/// <param name="Errors">List of validation error descriptions (empty if valid).</param>
public record McpValidationResult(bool IsValid, IReadOnlyList<string> Errors);
