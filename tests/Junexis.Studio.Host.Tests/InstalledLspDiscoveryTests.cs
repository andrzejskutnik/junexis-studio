using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class InstalledLspDiscoveryTests
{
    [Theory]
    [InlineData("windows", "C:\\Users\\tester", "C:\\Users\\tester\\AppData\\Local", "Junexis/lsp/JunexisLang.Compiler.LspHost.exe")]
    [InlineData("macos", "/Users/tester", null, "Library/Application Support/Junexis/lsp/JunexisLang.Compiler.LspHost")]
    [InlineData("linux", "/home/tester", null, ".local/share/junexis/lsp/JunexisLang.Compiler.LspHost")]
    public void InstalledPath_FollowsPlatformContract(
        string platform,
        string userProfile,
        string? localApplicationData,
        string expectedSuffix)
    {
        var path = LanguageServerProfileResolver.InstalledJunexisLspPath(
            platform,
            userProfile,
            localApplicationData);

        Assert.EndsWith(
            expectedSuffix.Replace('/', Path.DirectorySeparatorChar),
            path,
            StringComparison.OrdinalIgnoreCase);
    }
}
