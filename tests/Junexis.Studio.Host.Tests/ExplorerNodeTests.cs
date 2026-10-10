using Junexis.Studio.Host;
using System.Collections.Generic;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class ExplorerNodeTests
{
    // helpers
    private static ExplorerNode File(string name)
        => new(name, $"/tmp/{name}", isDirectory: false, new List<ExplorerNode>());

    private static ExplorerNode Dir(string name)
        => new(name, $"/tmp/{name}", isDirectory: true, new List<ExplorerNode>());

    // -----------------------------------------------------------------------
    // Issue #109 – File icons
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Issue", "109")]
    public void IconText_Directory_ReturnsFolder()
    {
        Assert.Equal("📁", Dir("src").IconText);
    }

    [Theory]
    [Trait("Issue", "109")]
    [InlineData("Program.cs", "♯")]
    [InlineData("App.axaml", "🖼")]
    [InlineData("App.xaml", "🖼")]
    [InlineData("appsettings.json", "🛠")]
    [InlineData("README.md", "📝")]
    [InlineData("notes.txt", "📄")]
    [InlineData("JunexisStudio.sln", "◈")]
    [InlineData("proj.csproj", "◈")]
    [InlineData("config.toml", "◈")]
    [InlineData("deploy.yaml", "◈")]
    [InlineData("deploy.yml", "◈")]
    [InlineData("build.sh", "⌨")]
    [InlineData("build.ps1", "⌨")]
    [InlineData("main.go", "◈")]
    [InlineData("script.py", "🐍")]
    [InlineData("unknown.bin", "📄")]
    [InlineData("noextension", "📄")]
    public void IconText_FileExtensions_ReturnExpectedIcon(string fileName, string expected)
    {
        Assert.Equal(expected, File(fileName).IconText);
    }

    [Fact]
    [Trait("Issue", "109")]
    public void IconText_ExtensionCaseInsensitive()
    {
        Assert.Equal("♯", File("Program.CS").IconText);
        Assert.Equal("📝", File("README.MD").IconText);
        Assert.Equal("🐍", File("script.PY").IconText);
    }

    [Fact]
    [Trait("Issue", "109")]
    public void IconText_DirectoryNeverUsesExtensionLogic()
    {
        // Folder named "src.cs" is still a folder icon, not a C# icon.
        Assert.Equal("📁", Dir("src.cs").IconText);
    }
}
