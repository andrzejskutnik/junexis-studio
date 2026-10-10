using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class PlatformToolResolverTests
{
    [Theory]
    [Trait("Milestone", "M19.5.1")]
    [InlineData("windows", "C:\\Users\\test\\AppData\\Local\\Junexis\\bin\\junexis.exe")]
    [InlineData("linux", "/home/test/.local/bin/junexis")]
    [InlineData("macos", "/home/test/.local/bin/junexis")]
    public void ResolveCommand_FindsStableProfileEntry(string platform, string expected)
    {
        var resolver = Create(platform, expected);

        Assert.Equal(expected, resolver.ResolveCommand("junexis", "JUNEXIS_CLI"));
    }

    [Fact]
    [Trait("Milestone", "M19.5.1")]
    public void ResolveCommand_PrefersExplicitConfiguration()
    {
        var environment = new Dictionary<string, string?>
        {
            ["JUNEXIS_CLI"] = "/opt/junexis/custom"
        };
        var resolver = new PlatformToolResolver(
            "linux",
            "/home/test",
            string.Empty,
            null,
            name => environment.GetValueOrDefault(name),
            _ => false);

        Assert.Equal("/opt/junexis/custom", resolver.ResolveCommand("junexis", "JUNEXIS_CLI"));
    }

    [Fact]
    [Trait("Milestone", "M19.5.1")]
    public void ResolveCommand_UsesXdgBinOnLinux()
    {
        const string expected = "/srv/profile/bin/junexis";
        var resolver = new PlatformToolResolver(
            "linux",
            "/home/test",
            string.Empty,
            "/srv/profile/bin",
            _ => null,
            path => path == expected);

        Assert.Equal(expected, resolver.ResolveCommand("junexis"));
    }

    [Fact]
    [Trait("Milestone", "M19.5.1")]
    public void ResolveCommand_FallsBackToPathWithoutSearchingRepositories()
    {
        var resolver = Create("linux", existingPath: null);

        Assert.Equal("junexis", resolver.ResolveCommand("junexis"));
    }

    private static PlatformToolResolver Create(string platform, string? existingPath) =>
        new(
            platform,
            "/home/test",
            "C:\\Users\\test\\AppData\\Local",
            null,
            _ => null,
            path => path == existingPath);
}
