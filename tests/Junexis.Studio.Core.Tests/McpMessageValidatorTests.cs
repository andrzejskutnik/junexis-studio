using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="McpMessageValidator"/>, <see cref="McpEnvelope"/>
/// and <see cref="McpValidationResult"/>.
/// </summary>
[Trait("Issue", "59")]
public class McpMessageValidatorTests
{
    // ── Helpers ─────────────────────────────────────────────────

    private sealed class RecordingOutput : IOutput
    {
        public List<string> Lines { get; } = new();
        public string RegisterChannel(string ch) => ch;
        public IReadOnlyList<string> GetChannelsSnapshot() => Array.Empty<string>();
        public void Write(string ch, string msg) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev, string? src, string? ex = null) => Lines.Add($"[{ch}] {msg}");
        public IReadOnlyList<OutputEntry> GetSnapshot() => Array.Empty<OutputEntry>();
        public event Action<OutputEntry>? EntryWritten;
    }

    // ── Tests ───────────────────────────────────────────────────

    [Fact]
    public void ValidEnvelope_WithPayload()
    {
        var v = new McpMessageValidator();
        var result = v.Validate(new McpEnvelope("tools/list", "corr-1", "{\"filter\":\"*\"}"));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidEnvelope_WithoutPayload()
    {
        var v = new McpMessageValidator();
        var result = v.Validate(new McpEnvelope("ping"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NullEnvelope_IsInvalid()
    {
        var output = new RecordingOutput();
        var v = new McpMessageValidator(output);
        var result = v.Validate(null);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("null"));
        Assert.Contains(output.Lines, l => l.Contains("null"));
    }

    [Fact]
    public void EmptyType_IsInvalid()
    {
        var v = new McpMessageValidator();
        var result = v.Validate(new McpEnvelope(""));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Type"));
    }

    [Fact]
    public void WhitespaceType_IsInvalid()
    {
        var v = new McpMessageValidator();
        var result = v.Validate(new McpEnvelope("   "));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void TypeExceedsMaxLength_IsInvalid()
    {
        var v = new McpMessageValidator(maxTypeLength: 10);
        var result = v.Validate(new McpEnvelope(new string('x', 11)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Type"));
    }

    [Fact]
    public void CorrelationIdExceedsMaxLength_IsInvalid()
    {
        var v = new McpMessageValidator(maxTypeLength: 10);
        var result = v.Validate(new McpEnvelope("ping", new string('c', 11)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("CorrelationId"));
    }

    [Fact]
    public void PayloadExceedsMaxSize_IsInvalid()
    {
        var v = new McpMessageValidator(maxPayloadSize: 100);
        var result = v.Validate(new McpEnvelope("ping", null, "{" + new string('x', 200) + "}"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Payload exceeds"));
    }

    [Fact]
    public void PayloadNotJson_IsInvalid()
    {
        var v = new McpMessageValidator();
        var result = v.Validate(new McpEnvelope("ping", null, "not-json"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("JSON"));
    }

    [Fact]
    public void PayloadStartsWithBracket_IsValid()
    {
        var v = new McpMessageValidator();
        var result = v.Validate(new McpEnvelope("list", null, "[1,2,3]"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void MultipleErrors_AllReported()
    {
        var output = new RecordingOutput();
        var v = new McpMessageValidator(output, maxPayloadSize: 5, maxTypeLength: 3);
        var result = v.Validate(new McpEnvelope("toolbox", null, "not-json-and-too-long"));
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2);
        Assert.Contains(output.Lines, l => l.Contains("Validation failed"));
    }

    [Fact]
    public void McpEnvelope_RecordEquality()
    {
        var a = new McpEnvelope("ping", "1", "{}");
        var b = new McpEnvelope("ping", "1", "{}");
        Assert.Equal(a, b);
    }

    [Fact]
    public void McpValidationResult_Properties()
    {
        var r = new McpValidationResult(true, Array.Empty<string>());
        Assert.True(r.IsValid);
        Assert.Empty(r.Errors);
    }
}
