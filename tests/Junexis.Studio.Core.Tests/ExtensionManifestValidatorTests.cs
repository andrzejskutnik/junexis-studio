using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

[Trait("Issue", "90")]
public sealed class ExtensionManifestValidatorTests
{
    private static ExtensionManifest ValidManifest(
        string id = "test-ext",
        string version = "0.1.0",
        string entryType = "Test.Extension",
        IReadOnlyList<string>? deps = null,
        IReadOnlyList<string>? perms = null,
        IReadOnlyList<string>? caps = null)
        => new(id, version, entryType, Dependencies: deps, Permissions: perms, ExposedCapabilities: caps);

    [Fact]
    public void ValidManifest_PassesValidation()
    {
        var result = ExtensionManifestValidator.Validate(ValidManifest());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void EmptyEntryType_SchemaError()
    {
        var m = ValidManifest(entryType: "");
        var result = ExtensionManifestValidator.Validate(m);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("EntryType") && e.Contains("required"));
    }

    [Fact]
    public void EntryType_NotFullyQualified_SchemaError()
    {
        var m = ValidManifest(entryType: "NoNamespace");
        var result = ExtensionManifestValidator.Validate(m);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("fully-qualified"));
    }

    [Fact]
    public void Id_TooLong_SchemaError()
    {
        var m = ValidManifest(id: new string('x', 200));
        var result = ExtensionManifestValidator.Validate(m);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("maximum length"));
    }

    [Fact]
    public void Dependency_Available_Passes()
    {
        var m = ValidManifest(deps: new[] { "other-ext" });
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "other-ext" };

        var result = ExtensionManifestValidator.Validate(m, available);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Dependency_NotAvailable_Error()
    {
        var m = ValidManifest(deps: new[] { "missing-ext" });
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "other-ext" };

        var result = ExtensionManifestValidator.Validate(m, available);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing-ext") && e.Contains("not available"));
    }

    [Fact]
    public void Dependency_EmptyEntry_Error()
    {
        var m = ValidManifest(deps: new[] { "" });
        var result = ExtensionManifestValidator.Validate(m);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty dependency"));
    }

    [Fact]
    public void Dependency_WithVersionRange_Parses()
    {
        var m = ValidManifest(deps: new[] { "other-ext:>=0.1.0" });
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "other-ext" };

        var result = ExtensionManifestValidator.Validate(m, available);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Permission_Known_Passes()
    {
        var m = ValidManifest(perms: new[] { "fs.read", "process.exec" });
        var result = ExtensionManifestValidator.Validate(m);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Permission_Unknown_Error()
    {
        var m = ValidManifest(perms: new[] { "super.admin" });
        var result = ExtensionManifestValidator.Validate(m);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown permission") && e.Contains("super.admin"));
    }

    [Fact]
    public void Capability_Duplicate_Error()
    {
        var m = ValidManifest(caps: new[] { "compiler", "compiler" });
        var result = ExtensionManifestValidator.Validate(m);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("duplicate capability"));
    }

    [Fact]
    public void NoDependencies_NoPermissions_Passes()
    {
        var m = ValidManifest();
        var result = ExtensionManifestValidator.Validate(m);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Dependencies_WithoutAvailableSet_SkipsDependencyCheck()
    {
        // When availableExtensionIds is null, dependency availability is not checked.
        var m = ValidManifest(deps: new[] { "anything" });
        var result = ExtensionManifestValidator.Validate(m);

        Assert.True(result.IsValid);
    }
}
