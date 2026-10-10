using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using System.Text.Json;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="WorkspaceConfigValidator"/> — JSON validation, rules, auto-fix hints.
/// </summary>
[Trait("Issue", "91")]
public class WorkspaceConfigValidatorTests
{
    // ── Fake ────────────────────────────────────────────────────

    private sealed class RecordingOutput : IOutput
    {
        public List<string> Lines { get; } = new();
        public string RegisterChannel(string ch) => ch;
        public IReadOnlyList<string> GetChannelsSnapshot() => Array.Empty<string>();
        public void Write(string ch, string msg) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev) => Lines.Add($"[{ch}][{sev}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev, string? src, string? ex = null) => Lines.Add($"[{ch}][{sev}] {msg}");
        public IReadOnlyList<OutputEntry> GetSnapshot() => Array.Empty<OutputEntry>();
        public event Action<OutputEntry>? EntryWritten;
    }

    // ── Empty/null JSON ─────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Json_ReturnsError(string? json)
    {
        var v = new WorkspaceConfigValidator();
        var result = v.Validate(json!);
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal("$", result.Issues[0].Path);
    }

    // ── Invalid JSON syntax ─────────────────────────────────────

    [Fact]
    public void InvalidJson_ReturnsError()
    {
        var v = new WorkspaceConfigValidator();
        var result = v.Validate("{not valid json}");
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("Invalid JSON"));
    }

    [Fact]
    public void InvalidJson_HasFixHint()
    {
        var v = new WorkspaceConfigValidator();
        var result = v.Validate("{broken");
        Assert.NotNull(result.Issues[0].FixHint);
    }

    // ── Root not object ─────────────────────────────────────────

    [Fact]
    public void RootArray_ReturnsError()
    {
        var v = new WorkspaceConfigValidator();
        var result = v.Validate("[1,2,3]");
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("object"));
    }

    // ── Valid empty object (no rules) ───────────────────────────

    [Fact]
    public void EmptyObject_NoRules_Valid()
    {
        var v = new WorkspaceConfigValidator();
        var result = v.Validate("{}");
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    // ── Default rules: name ─────────────────────────────────────

    [Fact]
    public void Default_MissingName_Warning()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var result = v.Validate("{}");
        Assert.Contains(result.Issues, i => i.Path == "name" && i.Severity == ConfigIssueSeverity.Warning);
        Assert.True(result.IsValid); // warning, not error
    }

    [Fact]
    public void Default_EmptyName_Error()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var result = v.Validate("{\"name\": \"\"}");
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "name" && i.Severity == ConfigIssueSeverity.Error);
    }

    [Fact]
    public void Default_ValidName_NoIssue()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var result = v.Validate("{\"name\": \"my-project\"}");
        Assert.DoesNotContain(result.Issues, i => i.Path == "name" && i.Severity == ConfigIssueSeverity.Error);
    }

    // ── Default rules: compiler ─────────────────────────────────

    [Fact]
    public void Default_CompilerNotObject_Error()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var result = v.Validate("{\"name\":\"x\", \"compiler\": \"gpp\"}");
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "compiler" && i.Severity == ConfigIssueSeverity.Error);
    }

    [Fact]
    public void Default_CompilerMissingId_Warning()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var result = v.Validate("{\"name\":\"x\", \"compiler\": {}}");
        Assert.Contains(result.Issues, i => i.Path == "compiler.id" && i.Severity == ConfigIssueSeverity.Warning);
    }

    [Fact]
    public void Default_CompilerWithId_NoIssue()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var result = v.Validate("{\"name\":\"x\", \"compiler\": {\"id\": \"gpp\"}}");
        Assert.DoesNotContain(result.Issues, i => i.Path.StartsWith("compiler"));
    }

    // ── Default rules: extensions ───────────────────────────────

    [Fact]
    public void Default_ExtensionsNotArray_Error()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var result = v.Validate("{\"name\":\"x\", \"extensions\": \"wrong\"}");
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "extensions");
    }

    [Fact]
    public void Default_ExtensionsArray_Valid()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var result = v.Validate("{\"name\":\"x\", \"extensions\": []}");
        Assert.DoesNotContain(result.Issues, i => i.Path == "extensions");
    }

    // ── Full valid config ───────────────────────────────────────

    [Fact]
    public void Default_FullValidConfig_NoErrors()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var json = """
        {
            "name": "test-project",
            "compiler": { "id": "gpp" },
            "extensions": []
        }
        """;
        var result = v.Validate(json);
        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Issues, i => i.Severity == ConfigIssueSeverity.Error);
    }

    // ── Custom rules ────────────────────────────────────────────

    [Fact]
    public void CustomRule_IsApplied()
    {
        var v = new WorkspaceConfigValidator();
        v.AddRule(new ConfigRule("custom", root =>
        {
            return !root.TryGetProperty("required_field", out _)
                ? new[] { new ConfigIssue("required_field", "Missing required_field.", ConfigIssueSeverity.Error) }
                : Array.Empty<ConfigIssue>();
        }));

        var result = v.Validate("{}");
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "required_field");
    }

    [Fact]
    public void RuleCount_ReflectsAddedRules()
    {
        var v = new WorkspaceConfigValidator();
        Assert.Equal(0, v.RuleCount);
        v.AddRule(new ConfigRule("r1", _ => Array.Empty<ConfigIssue>()));
        Assert.Equal(1, v.RuleCount);
    }

    [Fact]
    public void AddRule_NullThrows()
    {
        var v = new WorkspaceConfigValidator();
        Assert.Throws<ArgumentNullException>(() => v.AddRule(null!));
    }

    // ── Rule exception handling ─────────────────────────────────

    [Fact]
    public void RuleThrows_CapturedAsWarning()
    {
        var v = new WorkspaceConfigValidator();
        v.AddRule(new ConfigRule("faulty", _ => throw new InvalidOperationException("boom")));
        var result = v.Validate("{}");
        Assert.True(result.IsValid); // exception → warning, not error
        Assert.Contains(result.Issues, i => i.Message.Contains("boom") && i.Severity == ConfigIssueSeverity.Warning);
    }

    // ── Output logging ──────────────────────────────────────────

    [Fact]
    public void InvalidConfig_LogsToOutput()
    {
        var output = new RecordingOutput();
        var v = new WorkspaceConfigValidator(output);
        v.Validate("{broken");
        Assert.Contains(output.Lines, l => l.Contains("Integrity") && l.Contains("invalid JSON"));
    }

    [Fact]
    public void WarningsOnly_LogsHintCount()
    {
        var output = new RecordingOutput();
        var v = WorkspaceConfigValidator.CreateDefault(output);
        v.Validate("{}"); // missing name = warning
        Assert.Contains(output.Lines, l => l.Contains("hint"));
    }

    // ── FixHints ────────────────────────────────────────────────

    [Fact]
    public void Default_MissingName_HasFixHint()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var result = v.Validate("{}");
        var nameIssue = result.Issues.First(i => i.Path == "name");
        Assert.NotNull(nameIssue.FixHint);
        Assert.Contains("name", nameIssue.FixHint);
    }

    [Fact]
    public void Default_CompilerNotObject_HasFixHint()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        var result = v.Validate("{\"name\":\"x\", \"compiler\": 123}");
        var issue = result.Issues.First(i => i.Path == "compiler");
        Assert.NotNull(issue.FixHint);
    }

    // ── ConfigValidationResult.Valid() factory ──────────────────

    [Fact]
    public void ValidResult_Factory()
    {
        var r = ConfigValidationResult.Valid();
        Assert.True(r.IsValid);
        Assert.Empty(r.Issues);
    }

    // ── OutputChannel constant ──────────────────────────────────

    [Fact]
    public void OutputChannel_IsIntegrity()
    {
        Assert.Equal("Integrity", WorkspaceConfigValidator.OutputChannel);
    }

    // ── CreateDefault has 3 rules ───────────────────────────────

    [Fact]
    public void CreateDefault_Has3Rules()
    {
        var v = WorkspaceConfigValidator.CreateDefault();
        Assert.Equal(3, v.RuleCount);
    }
}
