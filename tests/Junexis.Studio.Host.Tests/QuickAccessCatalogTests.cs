using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class QuickAccessCatalogTests
{
    [Fact]
    public void CreateCommands_UsesMetadataAndShortcut()
    {
        var commands = new CommandRegistry();
        var keybindings = new KeybindingRegistry();
        commands.Register(
            "workspace.openFolder",
            () => { },
            menuPath: "Workspace/Open Folder...",
            displayName: "Open Folder",
            category: "Workspace");
        keybindings.Bind(new KeyGesture("O", KeyModifiers.Ctrl), "workspace.openFolder");

        var item = Assert.Single(QuickAccessCatalog.CreateCommands(commands, keybindings));

        Assert.Equal("Open Folder", item.Title);
        Assert.Equal("Workspace · workspace.openFolder", item.Description);
        Assert.Equal("Ctrl+O", item.Shortcut);
        Assert.Equal(QuickAccessItemKind.Command, item.Kind);
    }

    [Fact]
    public void Search_PrioritizesTitlePrefixAndSupportsFuzzyMatch()
    {
        var items = new[]
        {
            new QuickAccessItem("Build Project", "Build", null, "project.build", QuickAccessItemKind.Command),
            new QuickAccessItem("Rebuild Workspace", "Build", null, "project.rebuild", QuickAccessItemKind.Command),
            new QuickAccessItem("Open Folder", "Workspace", null, "workspace.openFolder", QuickAccessItemKind.Command),
        };

        var prefixMatches = QuickAccessCatalog.Search(items, "build");
        var fuzzyMatches = QuickAccessCatalog.Search(items, "opfld");

        Assert.Equal("Build Project", prefixMatches[0].Title);
        Assert.Equal("Open Folder", Assert.Single(fuzzyMatches).Title);
    }

    [Fact]
    public void CreateFiles_SkipsToolDirectoriesAndKeepsRelativePath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-quick-access-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(Path.Combine(root, "node_modules", "package"));
            File.WriteAllText(Path.Combine(root, "src", "main.jnx"), "intent Main {}");
            File.WriteAllText(Path.Combine(root, ".git", "config"), "ignored");
            File.WriteAllText(Path.Combine(root, "node_modules", "package", "index.js"), "ignored");

            var files = QuickAccessCatalog.CreateFiles(root);

            var item = Assert.Single(files);
            Assert.Equal("main.jnx", item.Title);
            Assert.Equal(Path.Combine("src", "main.jnx"), item.Description);
            Assert.Equal(QuickAccessItemKind.File, item.Kind);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
