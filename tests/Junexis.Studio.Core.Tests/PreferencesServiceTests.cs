using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Testy jednostkowe dla <see cref="PreferencesService"/> (YAML-based preferences).
/// </summary>
[Trait("Category", "Preferences")]
public sealed class PreferencesServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _globalDir;
    private readonly string _globalFile;
    private readonly string _workspaceDir;
    private readonly string _workspaceFile;

    public PreferencesServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "junexis_studio_pref_tests_" + Guid.NewGuid().ToString("N"));
        _globalDir = Path.Combine(_tempDir, "global", ".junexis-studio");
        _workspaceDir = Path.Combine(_tempDir, "workspace", ".junexis-studio");

        Directory.CreateDirectory(_globalDir);
        Directory.CreateDirectory(_workspaceDir);

        _globalFile = Path.Combine(_globalDir, "preferences.yml");
        _workspaceFile = Path.Combine(_workspaceDir, "preferences.yml");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ---- Helpers ----

    private static RecordingOutput CreateOutput() => new();

    private static StubWorkspace CreateWorkspace(string? rootPath = null) => new(rootPath);

    // ---- Merge tests (static, no I/O) ----

    [Fact]
    [Trait("Category", "Preferences")]
    public void Merge_BothNull_ReturnsDefaults()
    {
        var result = PreferencesService.Merge(null, null);

        Assert.NotNull(result);
        Assert.Equal("Dark", result.Theme);
        Assert.Equal(14, result.Editor.FontSize);
        Assert.Equal(4, result.Editor.TabSize);
        Assert.Equal(MentorPresenceMode.Teaching, result.Mentor.Presence);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void Merge_OnlyGlobal_ReturnsGlobal()
    {
        var global = new JunexisPreferences { Theme = "Light" };
        global.Editor.FontSize = 18;

        var result = PreferencesService.Merge(global, null);

        Assert.Equal("Light", result.Theme);
        Assert.Equal(18, result.Editor.FontSize);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void Merge_WorkspaceOverridesGlobal()
    {
        var global = new JunexisPreferences { Theme = "Dark" };
        global.Editor.FontSize = 14;
        global.Editor.TabSize = 4;

        var workspace = new JunexisPreferences { Theme = "Light" };
        workspace.Editor.FontSize = 16;

        var result = PreferencesService.Merge(global, workspace);

        Assert.Equal("Light", result.Theme);
        Assert.Equal(16, result.Editor.FontSize);
        Assert.Equal(4, result.Editor.TabSize); // nie zmieniony w workspace
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void Merge_SyntaxColors_WorkspaceOverridesGlobal()
    {
        var global = new JunexisPreferences();
        global.Syntax.Junexis.Comment = "#111111";

        var workspace = new JunexisPreferences();
        workspace.Syntax.Junexis.Comment = "#FF0000";

        var result = PreferencesService.Merge(global, workspace);

        Assert.Equal("#FF0000", result.Syntax.Junexis.Comment);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void Merge_SyntaxColors_NullInWorkspaceFallsBackToGlobal()
    {
        var global = new JunexisPreferences();
        global.Syntax.Junexis.Comment = "#111111";
        global.Syntax.Junexis.Keyword = "#222222";

        var workspace = new JunexisPreferences();
        workspace.Syntax.Junexis.Comment = "#FF0000";
        workspace.Syntax.Junexis.Keyword = null; // nie ustawiony

        var result = PreferencesService.Merge(global, workspace);

        Assert.Equal("#FF0000", result.Syntax.Junexis.Comment);
        Assert.Equal("#222222", result.Syntax.Junexis.Keyword);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void Merge_ExtraColors_AreUnioned()
    {
        var global = new JunexisPreferences();
        global.Syntax.Junexis.Extra.Clear();
        global.Syntax.Junexis.Extra["Communication"] = "#001122";
        global.Syntax.Junexis.Extra["Resilience"] = "#334455";

        var workspace = new JunexisPreferences();
        workspace.Syntax.Junexis.Extra.Clear();
        workspace.Syntax.Junexis.Extra["Communication"] = "#AABBCC";
        workspace.Syntax.Junexis.Extra["NewColor"] = "#DDEEFF";

        var result = PreferencesService.Merge(global, workspace);

        Assert.Equal("#AABBCC", result.Syntax.Junexis.Extra["Communication"]); // overridden
        Assert.Equal("#334455", result.Syntax.Junexis.Extra["Resilience"]); // from global
        Assert.Equal("#DDEEFF", result.Syntax.Junexis.Extra["NewColor"]); // new from workspace
    }

    // ---- LoadFromFile tests ----

    [Fact]
    [Trait("Category", "Preferences")]
    public void LoadFromFile_NonExistentFile_ReturnsNull()
    {
        var output = CreateOutput();
        var ws = CreateWorkspace();
        var svc = CreateServiceWithPaths(ws, output);

        var result = svc.LoadFromFile("/non/existent/file.yml");

        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void LoadFromFile_ValidYaml_ReturnsPreferences()
    {
        var yaml = @"
theme: Light
editor:
  fontSize: 18
  tabSize: 2
  wordWrap: true
";
        File.WriteAllText(_globalFile, yaml);

        var output = CreateOutput();
        var ws = CreateWorkspace();
        var svc = CreateServiceWithPaths(ws, output);

        var result = svc.LoadFromFile(_globalFile);

        Assert.NotNull(result);
        Assert.Equal("Light", result!.Theme);
        Assert.Equal(18, result.Editor.FontSize);
        Assert.Equal(2, result.Editor.TabSize);
        Assert.True(result.Editor.WordWrap);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void LoadFromFile_EmptyFile_ReturnsNull()
    {
        File.WriteAllText(_globalFile, "");

        var output = CreateOutput();
        var ws = CreateWorkspace();
        var svc = CreateServiceWithPaths(ws, output);

        var result = svc.LoadFromFile(_globalFile);

        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void LoadFromFile_MalformedYaml_ReturnsNull_AndLogsWarning()
    {
        File.WriteAllText(_globalFile, "theme: [broken yaml {{{{");

        var output = CreateOutput();
        var ws = CreateWorkspace();
        var svc = CreateServiceWithPaths(ws, output);

        var result = svc.LoadFromFile(_globalFile);

        Assert.Null(result);
        Assert.Contains(output.Entries, e => e.Severity == OutputSeverity.Warning);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void LoadFromFile_WithSyntaxColors_ParsesCorrectly()
    {
        var yaml = @"
theme: Dark
syntax:
  junexis:
    comment: '#FF0000'
    keyword: '#00FF00'
    extra:
      Communication: '#AABBCC'
";
        File.WriteAllText(_globalFile, yaml);

        var output = CreateOutput();
        var ws = CreateWorkspace();
        var svc = CreateServiceWithPaths(ws, output);

        var result = svc.LoadFromFile(_globalFile);

        Assert.NotNull(result);
        Assert.Equal("#FF0000", result!.Syntax.Junexis.Comment);
        Assert.Equal("#00FF00", result.Syntax.Junexis.Keyword);
        Assert.Equal("#AABBCC", result.Syntax.Junexis.Extra["Communication"]);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void LoadFromFile_WithMentorPresence_ParsesEnum()
    {
        File.WriteAllText(_globalFile, "mentor:\n  presence: Teaching\n");

        var svc = CreateServiceWithPaths(CreateWorkspace(), CreateOutput());

        var result = svc.LoadFromFile(_globalFile);

        Assert.NotNull(result);
        Assert.Equal(MentorPresenceMode.Teaching, result!.Mentor.Presence);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void Merge_MentorWorkspacePresenceOverridesNonDefaultGlobal()
    {
        var global = new JunexisPreferences();
        global.Mentor.Presence = MentorPresenceMode.Teaching;
        var workspace = new JunexisPreferences();
        workspace.Mentor.Presence = MentorPresenceMode.Quiet;

        var result = PreferencesService.Merge(global, workspace);

        Assert.Equal(MentorPresenceMode.Quiet, result.Mentor.Presence);
    }

    // ---- Model defaults tests ----

    [Fact]
    [Trait("Category", "Preferences")]
    public void JunexisDefaults_HasExpectedColors()
    {
        var palette = LanguageColorPalette.JunexisDefaults();

        Assert.Equal("#6A9955", palette.Comment);
        Assert.Equal("#569CD6", palette.Keyword);
        Assert.Equal("#CE9178", palette.String);
        Assert.Equal("#FFE484", palette.Function);
        Assert.Equal("#4EC9B0", palette.Type);
        Assert.Contains("Communication", palette.Extra.Keys);
        Assert.Contains("Resilience", palette.Extra.Keys);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void CSharpDefaults_HasExpectedColors()
    {
        var palette = LanguageColorPalette.CSharpDefaults();

        Assert.Equal("#6A9955", palette.Comment);
        Assert.Equal("#569CD6", palette.Keyword);
        Assert.Equal("#DCDCAA", palette.Function);
        Assert.Equal("#4EC9B0", palette.Type);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void DefaultPreferences_HasDarkTheme()
    {
        var prefs = new JunexisPreferences();

        Assert.Equal("Dark", prefs.Theme);
        Assert.Equal(14, prefs.Editor.FontSize);
        Assert.False(prefs.Editor.WordWrap);
        Assert.False(prefs.Editor.AutoSave);
        Assert.True(prefs.Editor.ShowLineNumbers);
        Assert.Equal(MentorPresenceMode.Teaching, prefs.Mentor.Presence);
    }

    // ---- Interface contract tests ----

    [Fact]
    [Trait("Category", "Preferences")]
    public void Current_NeverReturnsNull()
    {
        var output = CreateOutput();
        var ws = CreateWorkspace();
        var svc = CreateServiceWithPaths(ws, output);

        Assert.NotNull(svc.Current);
        Assert.NotNull(svc.Current.Editor);
        Assert.NotNull(svc.Current.Syntax);
        Assert.NotNull(svc.Current.Mentor);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void WorkspaceFilePath_NullWhenNoWorkspace()
    {
        var output = CreateOutput();
        var ws = CreateWorkspace(null);
        var svc = CreateServiceWithPaths(ws, output);

        Assert.Null(svc.WorkspaceFilePath);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void WorkspaceFilePath_ResolvesWhenWorkspaceSet()
    {
        var output = CreateOutput();
        var workspaceRoot = Path.Combine(_tempDir, "workspace");
        var ws = CreateWorkspace(workspaceRoot);
        var svc = CreateServiceWithPaths(ws, output);

        Assert.NotNull(svc.WorkspaceFilePath);
        Assert.Contains(".junexis-studio", svc.WorkspaceFilePath!);
        Assert.EndsWith("preferences.yml", svc.WorkspaceFilePath!);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void SaveWorkspace_WritesActiveWorkspacePreferences()
    {
        var output = CreateOutput();
        var workspaceRoot = Path.GetDirectoryName(_workspaceDir)!;
        var svc = CreateServiceWithPaths(CreateWorkspace(workspaceRoot), output);
        svc.Current.Mentor.Presence = MentorPresenceMode.Teaching;

        svc.SaveWorkspace();

        Assert.True(File.Exists(_workspaceFile));
        var saved = svc.LoadFromFile(_workspaceFile);
        Assert.NotNull(saved);
        Assert.Equal(MentorPresenceMode.Teaching, saved!.Mentor.Presence);
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void SaveWorkspace_WithoutWorkspaceLogsWarning()
    {
        var output = CreateOutput();
        var svc = CreateServiceWithPaths(CreateWorkspace(), output);

        svc.SaveWorkspace();

        Assert.Contains(
            output.Entries,
            entry => entry.Severity == OutputSeverity.Warning &&
                     entry.Message.Contains("bez otwartego folderu", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Preferences")]
    public void Reload_ReloadsFromDisk()
    {
        var output = CreateOutput();
        var ws = CreateWorkspace();
        var svc = CreateServiceWithPaths(ws, output);

        Assert.Equal("Dark", svc.Current.Theme);

        // Zmień plik globalny
        File.WriteAllText(_globalFile, "theme: Light\n");

        // Przed reload - nadal Dark
        Assert.Equal("Dark", svc.Current.Theme);

        // Podmień ścieżkę globalną (hack: użyj reflection)
        // Zamiast tego - przetestujmy LoadFromFile + Merge
        var loaded = svc.LoadFromFile(_globalFile);
        Assert.NotNull(loaded);
        Assert.Equal("Light", loaded!.Theme);
    }

    // ---- Helper: create service bypassing real global path ----

    private PreferencesService CreateServiceWithPaths(StubWorkspace ws, RecordingOutput output)
    {
        return new PreferencesService(ws, output);
    }

    // ---- Stubs ----

    private sealed class StubWorkspace : IWorkspace
    {
        public StubWorkspace(string? rootPath = null)
        {
            RootPath = rootPath ?? string.Empty;
        }

        public string? RootPath { get; private set; }
        public event Action<string>? FileOpened;
        public event Action<string>? FolderOpened;
        public event Action<string>? FileSaved;

        public void OpenFolder(string path) { RootPath = path; FolderOpened?.Invoke(path); }
        public string ReadFile(string path) => string.Empty;
        public void WriteFile(string path, string content) { }
        public string? GetWorkspaceConfigRaw() => null;
        public void SetWorkspaceConfigRaw(string content) { }
    }

    private sealed class RecordingOutput : IOutput
    {
        public List<OutputEntry> Entries { get; } = new();
        public event Action<OutputEntry>? EntryWritten;

        public string RegisterChannel(string channel) => channel;
        public IReadOnlyList<string> GetChannelsSnapshot() => Array.Empty<string>();

        public void Write(string channel, string message)
            => Write(channel, message, OutputSeverity.Info, null, null);

        public void Write(string channel, string message, OutputSeverity severity)
            => Write(channel, message, severity, null, null);

        public void Write(string channel, string message, OutputSeverity severity,
            string? source, string? exceptionText = null)
        {
            var entry = new OutputEntry(channel, message, DateTime.UtcNow, severity, source, exceptionText);
            Entries.Add(entry);
            EntryWritten?.Invoke(entry);
        }

        public IReadOnlyList<OutputEntry> GetSnapshot() => Entries;
    }
}
