using System.Text.RegularExpressions;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="PrivacyFilter"/>, <see cref="IPrivacyFilter"/>,
/// <see cref="RedactionRule"/> and <see cref="RedactionResult"/>.
/// </summary>
[Trait("Issue", "83")]
public class PrivacyFilterTests
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

    // ── Basic tests ─────────────────────────────────────────────

    [Fact]
    public void Redact_EmptyInput_ReturnsEmpty()
    {
        var f = new PrivacyFilter();
        var r = f.Redact("");
        Assert.Equal("", r.Output);
        Assert.Equal(0, r.RedactionCount);
    }

    [Fact]
    public void Redact_NullInput_ReturnsEmpty()
    {
        var f = new PrivacyFilter();
        var r = f.Redact(null!);
        Assert.Equal("", r.Output);
        Assert.Equal(0, r.RedactionCount);
    }

    [Fact]
    public void Redact_NoRules_ReturnsOriginal()
    {
        var f = new PrivacyFilter();
        var r = f.Redact("hello world");
        Assert.Equal("hello world", r.Output);
        Assert.Equal(0, r.RedactionCount);
    }

    [Fact]
    public void Redact_CustomRule_Replaces()
    {
        var f = new PrivacyFilter();
        f.AddRule(new RedactionRule("SSN", new Regex(@"\d{3}-\d{2}-\d{4}"), "[SSN]"));

        var r = f.Redact("My SSN is 123-45-6789.");
        Assert.Equal("My SSN is [SSN].", r.Output);
        Assert.Equal(1, r.RedactionCount);
        Assert.Contains("SSN", r.RedactedPatterns);
    }

    [Fact]
    public void Redact_MultipleMatches_CountsAll()
    {
        var f = new PrivacyFilter();
        f.AddRule(new RedactionRule("digits", new Regex(@"\d{4}"), "****"));

        var r = f.Redact("codes: 1234 and 5678");
        Assert.Equal("codes: **** and ****", r.Output);
        Assert.Equal(2, r.RedactionCount);
    }

    // ── Default rules ───────────────────────────────────────────

    [Fact]
    public void Default_RedactsJwt()
    {
        var f = PrivacyFilter.CreateDefault();
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";
        var r = f.Redact($"token: {jwt}");

        Assert.DoesNotContain("eyJ", r.Output);
        Assert.True(r.RedactionCount > 0);
    }

    [Fact]
    public void Default_RedactsBearerToken()
    {
        var f = PrivacyFilter.CreateDefault();
        var r = f.Redact("Authorization: Bearer abcdefghijklmnop1234567890");

        Assert.DoesNotContain("abcdefghijklmnop", r.Output);
        Assert.Contains("Bearer [REDACTED]", r.Output);
    }

    [Fact]
    public void Default_RedactsApiKey()
    {
        var f = PrivacyFilter.CreateDefault();
        var r = f.Redact("api_key=superSecretKey12345678");

        Assert.DoesNotContain("superSecretKey", r.Output);
        Assert.True(r.RedactionCount > 0);
    }

    [Fact]
    public void Default_RedactsAwsKey()
    {
        var f = PrivacyFilter.CreateDefault();
        var r = f.Redact("key: AKIAIOSFODNN7EXAMPLE");

        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", r.Output);
    }

    [Fact]
    public void Default_RedactsConnectionStringPassword()
    {
        var f = PrivacyFilter.CreateDefault();
        var r = f.Redact("Server=db;Password=MyS3cret!;Database=app");

        Assert.DoesNotContain("MyS3cret!", r.Output);
    }

    [Fact]
    public void Default_RedactsEnvVars()
    {
        var f = PrivacyFilter.CreateDefault();
        var r = f.Redact("use %SECRET_TOKEN% or $DB_PASSWORD");

        Assert.DoesNotContain("%SECRET_TOKEN%", r.Output);
        Assert.DoesNotContain("$DB_PASSWORD", r.Output);
    }

    [Fact]
    public void Default_CleanText_NoRedaction()
    {
        var f = PrivacyFilter.CreateDefault();
        var r = f.Redact("Hello, this is a clean message.");

        Assert.Equal("Hello, this is a clean message.", r.Output);
        Assert.Equal(0, r.RedactionCount);
    }

    // ── Rule management ─────────────────────────────────────────

    [Fact]
    public void AddRule_Null_Throws()
    {
        var f = new PrivacyFilter();
        Assert.Throws<ArgumentNullException>(() => f.AddRule(null!));
    }

    [Fact]
    public void RemoveRules_RemovesByLabel()
    {
        var f = new PrivacyFilter();
        f.AddRule(new RedactionRule("test", new Regex("abc")));
        f.AddRule(new RedactionRule("test", new Regex("def")));
        f.AddRule(new RedactionRule("other", new Regex("ghi")));

        var removed = f.RemoveRules("test");
        Assert.Equal(2, removed);
        Assert.Single(f.GetRuleLabels());
    }

    [Fact]
    public void RemoveRules_CaseInsensitive()
    {
        var f = new PrivacyFilter();
        f.AddRule(new RedactionRule("Secret", new Regex("x")));

        Assert.Equal(1, f.RemoveRules("SECRET"));
    }

    [Fact]
    public void GetRuleLabels_ReturnsSnapshot()
    {
        var f = new PrivacyFilter();
        f.AddRule(new RedactionRule("A", new Regex("a")));
        f.AddRule(new RedactionRule("B", new Regex("b")));

        var labels = f.GetRuleLabels();
        Assert.Equal(2, labels.Count);
    }

    // ── Output logging ──────────────────────────────────────────

    [Fact]
    public void Redact_LogsToOutput()
    {
        var output = new RecordingOutput();
        var f = new PrivacyFilter(output);
        f.AddRule(new RedactionRule("test-secret", new Regex("secret123")));

        f.Redact("here is secret123 data");

        Assert.Contains(output.Lines, l => l.Contains("Redacted") && l.Contains("test-secret"));
    }

    [Fact]
    public void Redact_NoMatch_NoOutput()
    {
        var output = new RecordingOutput();
        var f = new PrivacyFilter(output);
        f.AddRule(new RedactionRule("test", new Regex("nomatch")));

        f.Redact("hello world");

        Assert.Empty(output.Lines);
    }

    // ── Model tests ─────────────────────────────────────────────

    [Fact]
    public void RedactionResult_Properties()
    {
        var r = new RedactionResult("text", 2, new[] { "A", "B" });
        Assert.Equal("text", r.Output);
        Assert.Equal(2, r.RedactionCount);
        Assert.Equal(2, r.RedactedPatterns.Count);
    }

    [Fact]
    public void RedactionRule_DefaultReplacement()
    {
        var rule = new RedactionRule("test", new Regex("x"));
        Assert.Equal("[REDACTED]", rule.Replacement);
    }
}
