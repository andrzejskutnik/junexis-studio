using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class LanguageServiceEditTests
{
    [Fact]
    [Trait("Category", "LanguageServices")]
    public void ApplyTextEdits_PreservesCrLfPositions()
    {
        const string source = "first\r\nvalue = value\r\nlast";
        var edits = new[]
        {
            new LanguageTextEdit(
                "sample.jnx",
                new LanguagePosition(1, 0),
                new LanguagePosition(1, 5),
                "sensor"),
            new LanguageTextEdit(
                "sample.jnx",
                new LanguagePosition(1, 8),
                new LanguagePosition(1, 13),
                "sensor"),
        };

        var result = LanguageService.ApplyTextEdits(source, edits);

        Assert.Equal("first\r\nsensor = sensor\r\nlast", result);
    }

    [Fact]
    [Trait("Category", "LanguageServices")]
    public void ApplyTextEdits_AppliesLaterRangesBeforeEarlierRanges()
    {
        const string source = "alpha beta alpha";
        var edits = new[]
        {
            new LanguageTextEdit(
                "sample.jnx",
                new LanguagePosition(0, 0),
                new LanguagePosition(0, 5),
                "longerName"),
            new LanguageTextEdit(
                "sample.jnx",
                new LanguagePosition(0, 11),
                new LanguagePosition(0, 16),
                "longerName"),
        };

        var result = LanguageService.ApplyTextEdits(source, edits);

        Assert.Equal("longerName beta longerName", result);
    }
}
