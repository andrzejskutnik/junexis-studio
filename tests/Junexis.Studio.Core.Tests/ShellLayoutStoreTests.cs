using Junexis.Studio.Core;
using System;
using System.IO;
using Xunit;

namespace Junexis.Studio.Core.Tests;

/// <summary>
/// Tests for <see cref="ShellLayoutStore"/> and <see cref="ShellLayoutData"/>.
/// </summary>
[Trait("Issue", "116")]
public sealed class ShellLayoutStoreTests
{
    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"nexus_layout_{Guid.NewGuid():N}.json");

    // ── Load ─────────────────────────────────────────────────────────

    [Fact]
    public void Load_WhenFileAbsent_ReturnsDefaults()
    {
        var store = new ShellLayoutStore(TempPath());

        var data = store.Load();

        Assert.Equal(ShellLayoutData.Default.ExplorerWidth, data.ExplorerWidth);
        Assert.Equal(ShellLayoutData.Default.ToolTabsHeight, data.ToolTabsHeight);
        Assert.Equal(ShellLayoutData.Default.RightPanelWidth, data.RightPanelWidth);
    }

    [Fact]
    public void Load_AfterSave_ReturnsRoundTrippedValues()
    {
        var path = TempPath();
        var store = new ShellLayoutStore(path);
        var original = new ShellLayoutData(320, 220, 410, "problems", "junexis.debugger.dap");

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(320, loaded.ExplorerWidth);
        Assert.Equal(220, loaded.ToolTabsHeight);
        Assert.Equal(410, loaded.RightPanelWidth);
        Assert.Equal("problems", loaded.BottomPanelId);
        Assert.Equal("junexis.debugger.dap", loaded.RightPanelId);

        File.Delete(path);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsDefaults()
    {
        var path = TempPath();
        File.WriteAllText(path, "this is not json {{{");

        var store = new ShellLayoutStore(path);
        var data = store.Load();

        Assert.Equal(ShellLayoutData.Default.ExplorerWidth, data.ExplorerWidth);
        File.Delete(path);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(7000)]
    public void Load_WhenValueOutOfRange_ClampsToDefault(double badWidth)
    {
        var path = TempPath();
        File.WriteAllText(path, $"{{\"ExplorerWidth\":{badWidth},\"ToolTabsHeight\":180}}");

        var store = new ShellLayoutStore(path);
        var data = store.Load();

        Assert.Equal(ShellLayoutData.Default.ExplorerWidth, data.ExplorerWidth);
        File.Delete(path);
    }

    // ── Save ─────────────────────────────────────────────────────────

    [Fact]
    public void Save_CreatesFileAndDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"nexus_{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "sub", "shell-layout.json");
        var store = new ShellLayoutStore(path);

        store.Save(new ShellLayoutData(400, 250));

        Assert.True(File.Exists(path));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Save_ThrowsArgumentNullException_WhenDataNull()
    {
        var store = new ShellLayoutStore(TempPath());

        Assert.Throws<ArgumentNullException>(() => store.Save(null!));
    }

    // ── ShellLayoutData ───────────────────────────────────────────────

    [Fact]
    public void Default_HasExpectedValues()
    {
        Assert.Equal(248, ShellLayoutData.Default.ExplorerWidth);
        Assert.Equal(190, ShellLayoutData.Default.ToolTabsHeight);
        Assert.Equal(380, ShellLayoutData.Default.RightPanelWidth);
    }

    [Fact]
    public void Record_EqualityByValues()
    {
        var a = new ShellLayoutData(300, 200);
        var b = new ShellLayoutData(300, 200);

        Assert.Equal(a, b);
    }
}
