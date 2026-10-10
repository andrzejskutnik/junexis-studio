using Xunit;
using Junexis.Studio.Shell;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Testy rejestracji i rozpoznawania kolorowania składni JunexisLang.
/// </summary>
public sealed class JunexisHighlightingRegistrarTests
{
    public JunexisHighlightingRegistrarTests()
    {
        // Upewnij się, że definicje są zarejestrowane przed każdym testem.
        JunexisHighlightingRegistrar.RegisterAll();
    }

    // ── RegisterAll ──

    [Fact]
    [Trait("Category", "Highlighting")]
    public void RegisterAll_CanBeCalledMultipleTimes_WithoutError()
    {
        // Powinno być idempotentne
        JunexisHighlightingRegistrar.RegisterAll();
        JunexisHighlightingRegistrar.RegisterAll();
    }

    // ── GetHighlightingForFile: JunexisLang ──

    [Theory]
    [InlineData("main.jnx")]
    [InlineData("src/temperature.jnx")]
    [InlineData(@"C:\project\hello.jnx")]
    [Trait("Category", "Highlighting")]
    public void GetHighlightingForFile_JnxFile_ReturnsJunexisLangDefinition(string path)
    {
        var definition = JunexisHighlightingRegistrar.GetHighlightingForFile(path);

        Assert.NotNull(definition);
        Assert.Equal(JunexisHighlightingRegistrar.JunexisLangName, definition.Name);
    }

    [Theory]
    [InlineData("MAIN.jnx")]
    [InlineData("test.jnx")]
    [Trait("Category", "Highlighting")]
    public void GetHighlightingForFile_JnxFileCaseInsensitive_ReturnsJunexisLangDefinition(string path)
    {
        var definition = JunexisHighlightingRegistrar.GetHighlightingForFile(path);

        Assert.NotNull(definition);
        Assert.Equal(JunexisHighlightingRegistrar.JunexisLangName, definition.Name);
    }

    // ── GetHighlightingForFile: wbudowane języki ──

    [Theory]
    [InlineData("file.cs")]
    [InlineData("file.xml")]
    [InlineData("file.json")]
    [Trait("Category", "Highlighting")]
    public void GetHighlightingForFile_BuiltInLanguage_ReturnsDefinition(string path)
    {
        var definition = JunexisHighlightingRegistrar.GetHighlightingForFile(path);

        Assert.NotNull(definition);
    }

    // ── GetHighlightingForFile: brak dopasowania / null ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "Highlighting")]
    public void GetHighlightingForFile_NullOrEmpty_ReturnsNull(string? path)
    {
        var definition = JunexisHighlightingRegistrar.GetHighlightingForFile(path);

        Assert.Null(definition);
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("data.csv")]
    [InlineData("image.png")]
    [Trait("Category", "Highlighting")]
    public void GetHighlightingForFile_UnknownExtension_ReturnsNull(string path)
    {
        var definition = JunexisHighlightingRegistrar.GetHighlightingForFile(path);

        Assert.Null(definition);
    }

    // ── Definicja JunexisLang: zawartość ──

    [Fact]
    [Trait("Category", "Highlighting")]
    public void JunexisLangDefinition_ContainsMainRuleSet()
    {
        var definition = JunexisHighlightingRegistrar.GetHighlightingForFile("test.jnx");

        Assert.NotNull(definition);
        Assert.NotNull(definition.MainRuleSet);
    }

    [Fact]
    [Trait("Category", "Highlighting")]
    public void JunexisLangDefinition_HasNamedColors()
    {
        var definition = JunexisHighlightingRegistrar.GetHighlightingForFile("test.jnx");

        Assert.NotNull(definition);

        // Sprawdź kilka kluczowych kolorów zdefiniowanych w xshd
        var colors = definition.NamedHighlightingColors;
        Assert.Contains(colors, c => c.Name == "Comment");
        Assert.Contains(colors, c => c.Name == "Keyword");
        Assert.Contains(colors, c => c.Name == "String");
        Assert.Contains(colors, c => c.Name == "Number");
        Assert.Contains(colors, c => c.Name == "Declaration");
        Assert.Contains(colors, c => c.Name == "Communication");
        Assert.Contains(colors, c => c.Name == "PlatformAnnotation");
        Assert.Contains(colors, c => c.Name == "Resilience");
        Assert.Contains(colors, c => c.Name == "PrimitiveType");
    }
}
