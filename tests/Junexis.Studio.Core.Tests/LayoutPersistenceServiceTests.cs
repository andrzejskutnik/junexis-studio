using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class LayoutPersistenceServiceTests
{
    // ─── Fakes ───────────────────────────────────────────

    private sealed class FakeWorkspace : IWorkspace
    {
        public string? RootPath { get; set; } = "c:\\work";
        public string? ConfigContent { get; set; }
        public string? LastSavedConfig { get; set; }
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnWrite { get; set; }

#pragma warning disable CS0067
        public event Action<string>? FileOpened;
        public event Action<string>? FolderOpened;
        public event Action<string>? FileSaved;
#pragma warning restore CS0067

        public void OpenFolder(string path) { }
        public string ReadFile(string path) => "";
        public void WriteFile(string path, string content) { }

        public string? GetWorkspaceConfigRaw()
        {
            if (ThrowOnRead) throw new IOException("read fail");
            return ConfigContent;
        }

        public void SetWorkspaceConfigRaw(string content)
        {
            if (ThrowOnWrite) throw new IOException("write fail");
            LastSavedConfig = content;
            ConfigContent = content;
        }
    }

    private sealed class RecordingOutput : IOutput
    {
        public List<string> Lines { get; } = new();
        public string RegisterChannel(string ch) => ch;
        public IReadOnlyList<string> GetChannelsSnapshot() => Array.Empty<string>();
        public void Write(string ch, string msg) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev) => Lines.Add($"[{ch}] {msg}");
        public void Write(string ch, string msg, OutputSeverity sev, string? src, string? ex = null) => Lines.Add($"[{ch}] {msg}");
        public IReadOnlyList<OutputEntry> GetSnapshot() => Array.Empty<OutputEntry>();
#pragma warning disable CS0067
        public event Action<OutputEntry>? EntryWritten;
#pragma warning restore CS0067
    }

    // ─── Constructor ─────────────────────────────────────

    [Fact]
    [Trait("Issue", "35")]
    public void Ctor_NullWorkspace_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LayoutPersistenceService(null!));
    }

    // ─── Load ────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "35")]
    public void Load_NoConfig_ReturnsNull()
    {
        var ws = new FakeWorkspace { ConfigContent = null };
        var sut = new LayoutPersistenceService(ws);

        Assert.Null(sut.Load());
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_EmptyConfig_ReturnsNull()
    {
        var ws = new FakeWorkspace { ConfigContent = "" };
        var sut = new LayoutPersistenceService(ws);

        Assert.Null(sut.Load());
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_InvalidJson_ReturnsNull_AndLogs()
    {
        var ws = new FakeWorkspace { ConfigContent = "{not json" };
        var output = new RecordingOutput();
        var sut = new LayoutPersistenceService(ws, output);

        Assert.Null(sut.Load());
        Assert.Contains(output.Lines, l => l.Contains("not valid JSON"));
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_NoLayoutKey_ReturnsNull()
    {
        var ws = new FakeWorkspace { ConfigContent = """{"other": 42}""" };
        var sut = new LayoutPersistenceService(ws);

        Assert.Null(sut.Load());
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_ValidLayout_ReturnsPanels()
    {
        var json = """
        {
            "layout": {
                "version": 1,
                "panels": [
                    { "panelId": "explorer", "dockGroup": "Left", "order": 0, "isVisible": true },
                    { "panelId": "editor", "dockGroup": "Center", "order": 0, "isVisible": true },
                    { "panelId": "output", "dockGroup": "Bottom", "order": 1, "isVisible": false }
                ]
            }
        }
        """;
        var ws = new FakeWorkspace { ConfigContent = json };
        var sut = new LayoutPersistenceService(ws);

        var result = sut.Load();

        Assert.NotNull(result);
        Assert.Equal(1, result.Version);
        Assert.Equal(3, result.Panels.Count);
        Assert.Equal("explorer", result.Panels[0].PanelId);
        Assert.Equal(DockGroup.Left, result.Panels[0].DockGroup);
        Assert.Equal("editor", result.Panels[1].PanelId);
        Assert.Equal(DockGroup.Center, result.Panels[1].DockGroup);
        Assert.Equal("output", result.Panels[2].PanelId);
        Assert.Equal(DockGroup.Bottom, result.Panels[2].DockGroup);
        Assert.False(result.Panels[2].IsVisible);
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_VersionZero_ReturnsNull_AndLogs()
    {
        var json = """{"layout": {"version": 0, "panels": []}}""";
        var ws = new FakeWorkspace { ConfigContent = json };
        var output = new RecordingOutput();
        var sut = new LayoutPersistenceService(ws, output);

        Assert.Null(sut.Load());
        Assert.Contains(output.Lines, l => l.Contains("Unknown layout schema version"));
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_FutureVersion_ReturnsNull_AndLogs()
    {
        var json = """{"layout": {"version": 999, "panels": []}}""";
        var ws = new FakeWorkspace { ConfigContent = json };
        var output = new RecordingOutput();
        var sut = new LayoutPersistenceService(ws, output);

        Assert.Null(sut.Load());
        Assert.Contains(output.Lines, l => l.Contains("newer than supported"));
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_UnknownPanelId_SkippedWithLog()
    {
        var json = """
        {
            "layout": {
                "version": 1,
                "panels": [
                    { "panelId": "explorer", "dockGroup": "Left", "order": 0 },
                    { "panelId": "removed_panel", "dockGroup": "Right", "order": 0 }
                ]
            }
        }
        """;
        var ws = new FakeWorkspace { ConfigContent = json };
        var output = new RecordingOutput();
        var panels = new PanelRegistry();
        panels.RegisterToolPanel("explorer", () => "vm");

        var sut = new LayoutPersistenceService(ws, output, panels);

        var result = sut.Load();

        Assert.NotNull(result);
        Assert.Single(result.Panels);
        Assert.Equal("explorer", result.Panels[0].PanelId);
        Assert.Contains(output.Lines, l => l.Contains("removed_panel") && l.Contains("unknown"));
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_NoPanelRegistry_AcceptsAllPanelIds()
    {
        var json = """
        {
            "layout": {
                "version": 1,
                "panels": [
                    { "panelId": "any_panel", "dockGroup": "Left", "order": 0 }
                ]
            }
        }
        """;
        var ws = new FakeWorkspace { ConfigContent = json };
        var sut = new LayoutPersistenceService(ws);

        var result = sut.Load();

        Assert.NotNull(result);
        Assert.Single(result.Panels);
        Assert.Equal("any_panel", result.Panels[0].PanelId);
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_EmptyPanelId_Skipped()
    {
        var json = """
        {
            "layout": {
                "version": 1,
                "panels": [
                    { "panelId": "", "dockGroup": "Left", "order": 0 },
                    { "panelId": "explorer", "dockGroup": "Left", "order": 1 }
                ]
            }
        }
        """;
        var ws = new FakeWorkspace { ConfigContent = json };
        var sut = new LayoutPersistenceService(ws);

        var result = sut.Load();

        Assert.NotNull(result);
        Assert.Single(result.Panels);
        Assert.Equal("explorer", result.Panels[0].PanelId);
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_InvalidDockGroup_DefaultsToLeft()
    {
        var json = """
        {
            "layout": {
                "version": 1,
                "panels": [
                    { "panelId": "x", "dockGroup": "Nowhere", "order": 0 }
                ]
            }
        }
        """;
        var ws = new FakeWorkspace { ConfigContent = json };
        var sut = new LayoutPersistenceService(ws);

        var result = sut.Load();

        Assert.NotNull(result);
        Assert.Equal(DockGroup.Left, result.Panels[0].DockGroup);
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_WorkspaceReadThrows_ReturnsNull_AndLogs()
    {
        var ws = new FakeWorkspace { ThrowOnRead = true };
        var output = new RecordingOutput();
        var sut = new LayoutPersistenceService(ws, output);

        Assert.Null(sut.Load());
        Assert.Contains(output.Lines, l => l.Contains("Failed to read"));
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Load_MalformedLayoutSection_ReturnsNull_AndLogs()
    {
        var json = """{"layout": "not-an-object"}""";
        var ws = new FakeWorkspace { ConfigContent = json };
        var output = new RecordingOutput();
        var sut = new LayoutPersistenceService(ws, output);

        Assert.Null(sut.Load());
        Assert.Contains(output.Lines, l => l.Contains("malformed"));
    }

    // ─── Save ────────────────────────────────────────────

    [Fact]
    [Trait("Issue", "35")]
    public void Save_NullLayout_Throws()
    {
        var ws = new FakeWorkspace();
        var sut = new LayoutPersistenceService(ws);

        Assert.Throws<ArgumentNullException>(() => sut.Save(null!));
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Save_WritesLayoutToConfig()
    {
        var ws = new FakeWorkspace { ConfigContent = "{}" };
        var sut = new LayoutPersistenceService(ws);

        var layout = new LayoutDescriptor(1, new[]
        {
            new PanelPlacement("explorer", DockGroup.Left, 0),
            new PanelPlacement("editor", DockGroup.Center, 0),
        });

        sut.Save(layout);

        Assert.NotNull(ws.LastSavedConfig);
        Assert.Contains("explorer", ws.LastSavedConfig);
        Assert.Contains("editor", ws.LastSavedConfig);
        Assert.Contains("layout", ws.LastSavedConfig);
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Save_PreservesExistingConfigKeys()
    {
        var ws = new FakeWorkspace { ConfigContent = """{"compiler": "gcc", "version": 1}""" };
        var sut = new LayoutPersistenceService(ws);

        sut.Save(new LayoutDescriptor(1, []));

        Assert.NotNull(ws.LastSavedConfig);
        Assert.Contains("compiler", ws.LastSavedConfig);
        Assert.Contains("layout", ws.LastSavedConfig);
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Save_OverwritesPreviousLayout()
    {
        var ws = new FakeWorkspace { ConfigContent = "{}" };
        var sut = new LayoutPersistenceService(ws);

        sut.Save(new LayoutDescriptor(1, new[] { new PanelPlacement("old", DockGroup.Left, 0) }));
        sut.Save(new LayoutDescriptor(1, new[] { new PanelPlacement("new", DockGroup.Right, 0) }));

        Assert.NotNull(ws.LastSavedConfig);
        Assert.Contains("new", ws.LastSavedConfig);
        Assert.DoesNotContain("old", ws.LastSavedConfig);
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Save_WriteThrows_LogsError()
    {
        var ws = new FakeWorkspace { ThrowOnWrite = true };
        var output = new RecordingOutput();
        var sut = new LayoutPersistenceService(ws, output);

        sut.Save(new LayoutDescriptor(1, []));

        Assert.Contains(output.Lines, l => l.Contains("Failed to save"));
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Save_NoExistingConfig_WritesNewDoc()
    {
        var ws = new FakeWorkspace { ConfigContent = null };
        var sut = new LayoutPersistenceService(ws);

        sut.Save(new LayoutDescriptor(1, new[] { new PanelPlacement("explorer", DockGroup.Left, 0) }));

        Assert.NotNull(ws.LastSavedConfig);
        Assert.Contains("explorer", ws.LastSavedConfig);
    }

    [Fact]
    [Trait("Issue", "35")]
    public void Save_SetsCurrentSchemaVersion()
    {
        var ws = new FakeWorkspace { ConfigContent = "{}" };
        var sut = new LayoutPersistenceService(ws);

        sut.Save(new LayoutDescriptor(99, new[] { new PanelPlacement("x", DockGroup.Left, 0) }));

        Assert.NotNull(ws.LastSavedConfig);
        Assert.Contains($"\"version\": {LayoutPersistenceService.CurrentSchemaVersion}", ws.LastSavedConfig);
    }

    // ─── Round-trip ──────────────────────────────────────

    [Fact]
    [Trait("Issue", "35")]
    public void RoundTrip_Save_LoadReturnsEquivalent()
    {
        var ws = new FakeWorkspace { ConfigContent = "{}" };
        var sut = new LayoutPersistenceService(ws);

        var original = new LayoutDescriptor(1, new[]
        {
            new PanelPlacement("explorer", DockGroup.Left, 0, true),
            new PanelPlacement("editor", DockGroup.Center, 1, true),
            new PanelPlacement("output", DockGroup.Bottom, 2, false),
        });

        sut.Save(original);
        var loaded = sut.Load();

        Assert.NotNull(loaded);
        Assert.Equal(LayoutPersistenceService.CurrentSchemaVersion, loaded.Version);
        Assert.Equal(original.Panels.Count, loaded.Panels.Count);

        for (var i = 0; i < original.Panels.Count; i++)
        {
            Assert.Equal(original.Panels[i].PanelId, loaded.Panels[i].PanelId);
            Assert.Equal(original.Panels[i].DockGroup, loaded.Panels[i].DockGroup);
            Assert.Equal(original.Panels[i].Order, loaded.Panels[i].Order);
            Assert.Equal(original.Panels[i].IsVisible, loaded.Panels[i].IsVisible);
        }
    }

    [Fact]
    [Trait("Issue", "35")]
    public void RoundTrip_WithExistingConfig_PreservesOtherKeys()
    {
        var ws = new FakeWorkspace { ConfigContent = """{"theme": "dark"}""" };
        var sut = new LayoutPersistenceService(ws);

        sut.Save(new LayoutDescriptor(1, new[] { new PanelPlacement("p", DockGroup.Left, 0) }));
        var raw = ws.LastSavedConfig!;

        Assert.Contains("theme", raw);
        Assert.Contains("dark", raw);
        Assert.Contains("layout", raw);
    }

    // ─── Edge cases: DockGroup casing ────────────────────

    [Fact]
    [Trait("Issue", "35")]
    public void Load_DockGroupCaseInsensitive()
    {
        var json = """
        {
            "layout": {
                "version": 1,
                "panels": [
                    { "panelId": "x", "dockGroup": "center", "order": 0 }
                ]
            }
        }
        """;
        var ws = new FakeWorkspace { ConfigContent = json };
        var sut = new LayoutPersistenceService(ws);

        var result = sut.Load();

        Assert.NotNull(result);
        Assert.Equal(DockGroup.Center, result.Panels[0].DockGroup);
    }

    // ─── Deterministic restore ───────────────────────────

    [Fact]
    [Trait("Issue", "35")]
    public void Load_PanelOrderPreserved()
    {
        var json = """
        {
            "layout": {
                "version": 1,
                "panels": [
                    { "panelId": "c", "dockGroup": "Left", "order": 2 },
                    { "panelId": "a", "dockGroup": "Left", "order": 0 },
                    { "panelId": "b", "dockGroup": "Left", "order": 1 }
                ]
            }
        }
        """;
        var ws = new FakeWorkspace { ConfigContent = json };
        var sut = new LayoutPersistenceService(ws);

        var result = sut.Load();

        Assert.NotNull(result);
        Assert.Equal(3, result.Panels.Count);
        Assert.Equal("c", result.Panels[0].PanelId);
        Assert.Equal("a", result.Panels[1].PanelId);
        Assert.Equal("b", result.Panels[2].PanelId);
    }
}
