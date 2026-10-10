using Junexis.Studio.Shell;

namespace Junexis.Studio.Host.Tests;

public sealed class ShellChromePolicyTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(-1, "")]
    [InlineData(1, "1")]
    [InlineData(42, "42")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void FormatBadgeCount_IsCompactAndBounded(int count, string expected)
    {
        Assert.Equal(expected, ShellChromePolicy.FormatBadgeCount(count));
    }

    [Fact]
    public void UseCompactEmptyWorkspace_RecognizesMissingWorkspace()
    {
        Assert.True(ShellChromePolicy.UseCompactEmptyWorkspace(null));
        Assert.True(ShellChromePolicy.UseCompactEmptyWorkspace(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        Assert.False(ShellChromePolicy.UseCompactEmptyWorkspace(Path.GetTempPath()));
    }
}
