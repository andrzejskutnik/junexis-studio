using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Testy PreferencesViewModel — budowanie drzewa, edycja wartości, zapis.
/// </summary>
public sealed class PreferencesViewModelTests
{
    // --- BuildNodes ---

    [Fact]
    public void BuildNodes_FromDefaults_ContainsThemeEditorSyntax()
    {
        var prefs = new JunexisPreferences();
        var nodes = PreferencesViewModel.BuildNodes(prefs, string.Empty);

        Assert.Contains(nodes, n => n.Key == "Theme" && n.IsLeaf);
        Assert.Contains(nodes, n => n.Key == "Editor" && n.IsGroup);
        Assert.Contains(nodes, n => n.Key == "Syntax" && n.IsGroup);
        Assert.Contains(nodes, n => n.Key == "Mentor" && n.IsGroup);
    }

    [Fact]
    public void BuildNodes_Theme_HasCorrectValue()
    {
        var prefs = new JunexisPreferences { Theme = "Light" };
        var nodes = PreferencesViewModel.BuildNodes(prefs, string.Empty);

        var theme = nodes.First(n => n.Key == "Theme");
        Assert.Equal("Light", theme.Value);
        Assert.False(theme.IsGroup);
    }

    [Fact]
    public void BuildNodes_EditorGroup_ContainsLeaves()
    {
        var prefs = new JunexisPreferences();
        var nodes = PreferencesViewModel.BuildNodes(prefs, string.Empty);
        var editor = nodes.First(n => n.Key == "Editor");

        Assert.True(editor.IsGroup);
        Assert.Contains(editor.Children, c => c.Key == "FontSize");
        Assert.Contains(editor.Children, c => c.Key == "WordWrap");
        Assert.Contains(editor.Children, c => c.Key == "TabSize");
        Assert.Contains(editor.Children, c => c.Key == "ShowLineNumbers");
    }

    [Fact]
    public void BuildNodes_BoolProperty_MarkedAsBool()
    {
        var prefs = new JunexisPreferences();
        var nodes = PreferencesViewModel.BuildNodes(prefs, string.Empty);
        var editor = nodes.First(n => n.Key == "Editor");
        var wordWrap = editor.Children.First(c => c.Key == "WordWrap");

        Assert.True(wordWrap.IsBool);
        Assert.False(wordWrap.IsText);
        Assert.Equal(typeof(bool), wordWrap.ValueType);
    }

    [Fact]
    public void BuildNodes_IntProperty_MarkedAsText()
    {
        var prefs = new JunexisPreferences();
        var nodes = PreferencesViewModel.BuildNodes(prefs, string.Empty);
        var editor = nodes.First(n => n.Key == "Editor");
        var fontSize = editor.Children.First(c => c.Key == "FontSize");

        Assert.False(fontSize.IsBool);
        Assert.True(fontSize.IsText);
        Assert.Equal(typeof(int), fontSize.ValueType);
    }

    [Fact]
    public void BuildNodes_DictionaryProperty_IsGroupWithChildren()
    {
        var prefs = new JunexisPreferences();
        var nodes = PreferencesViewModel.BuildNodes(prefs, string.Empty);
        var syntax = nodes.First(n => n.Key == "Syntax");
        var junexis = syntax.Children.First(c => c.Key == "Junexis");
        var extra = junexis.Children.FirstOrDefault(c => c.Key == "Extra");

        Assert.NotNull(extra);
        Assert.True(extra!.IsGroup);
        Assert.NotEmpty(extra.Children);
    }

    [Fact]
    public void BuildNodes_Path_IsCorrectForNestedNodes()
    {
        var prefs = new JunexisPreferences();
        var nodes = PreferencesViewModel.BuildNodes(prefs, string.Empty);
        var editor = nodes.First(n => n.Key == "Editor");
        var fontSize = editor.Children.First(c => c.Key == "FontSize");

        Assert.Equal("Editor", editor.Path);
        Assert.Equal("Editor.FontSize", fontSize.Path);
    }

    [Fact]
    public void BuildNodes_MentorPresenceIsClosedEnumChoice()
    {
        var nodes = PreferencesViewModel.BuildNodes(new JunexisPreferences(), string.Empty);
        var presence = nodes
            .First(node => node.Key == "Mentor")
            .Children
            .First(node => node.Key == "Presence");

        Assert.True(presence.IsEnum);
        Assert.False(presence.IsText);
        Assert.Equal("Teaching", presence.Value);
        Assert.Equal(["Quiet", "Balanced", "Teaching"], presence.EnumValues);
    }

    // --- PreferenceNode ---

    [Fact]
    public void PreferenceNode_SetValue_RaisesPropertyChanged()
    {
        var node = new PreferenceNode("Test", "Test", "old", false, typeof(string));
        var changed = new List<string>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        node.Value = "new";

        Assert.Contains("Value", changed);
    }

    [Fact]
    public void PreferenceNode_BoolValue_SetTrue_UpdatesValue()
    {
        var node = new PreferenceNode("Test", "Test", "False", false, typeof(bool));

        node.BoolValue = true;

        Assert.Equal("True", node.Value);
        Assert.True(node.BoolValue);
    }

    // --- ViewModel construction ---

    [Fact]
    public void Constructor_GlobalPreferences_SetsTitle()
    {
        var vm = CreateViewModel(isWorkspace: false);

        Assert.Equal("Preferences", vm.Title);
    }

    [Fact]
    public void Constructor_WorkspacePreferences_SetsTitle()
    {
        var vm = CreateViewModel(isWorkspace: true);

        Assert.Equal("Workspace Preferences", vm.Title);
    }

    [Fact]
    public void Constructor_BuildsNonEmptyTree()
    {
        var vm = CreateViewModel(isWorkspace: false);

        Assert.NotEmpty(vm.Roots);
    }

    // --- ApplyToModel ---

    [Fact]
    public void Save_ChangedTheme_AppliedToModel()
    {
        var prefs = new StubPreferencesService();
        var vm = new PreferencesViewModel(prefs, new OutputService(), isWorkspace: false);

        var themeNode = vm.Roots.First(n => n.Key == "Theme");
        themeNode.Value = "Light";

        vm.ApplyToModel(prefs.Current);

        Assert.Equal("Light", prefs.Current.Theme);
    }

    [Fact]
    public void Save_ChangedFontSize_AppliedToModel()
    {
        var prefs = new StubPreferencesService();
        var vm = new PreferencesViewModel(prefs, new OutputService(), isWorkspace: false);

        var editor = vm.Roots.First(n => n.Key == "Editor");
        var fontSize = editor.Children.First(c => c.Key == "FontSize");
        fontSize.Value = "18";

        vm.ApplyToModel(prefs.Current);

        Assert.Equal(18, prefs.Current.Editor.FontSize);
    }

    [Fact]
    public void Save_ChangedBool_AppliedToModel()
    {
        var prefs = new StubPreferencesService();
        var vm = new PreferencesViewModel(prefs, new OutputService(), isWorkspace: false);

        var editor = vm.Roots.First(n => n.Key == "Editor");
        var wordWrap = editor.Children.First(c => c.Key == "WordWrap");
        wordWrap.BoolValue = true;

        vm.ApplyToModel(prefs.Current);

        Assert.True(prefs.Current.Editor.WordWrap);
    }

    [Fact]
    public void Save_ChangedMentorPresence_AppliedToModel()
    {
        var prefs = new StubPreferencesService();
        var vm = new PreferencesViewModel(prefs, new OutputService(), isWorkspace: false);
        var presence = vm.Roots
            .First(node => node.Key == "Mentor")
            .Children
            .First(node => node.Key == "Presence");
        presence.Value = "Teaching";

        vm.ApplyToModel(prefs.Current);

        Assert.Equal(MentorPresenceMode.Teaching, prefs.Current.Mentor.Presence);
    }

    [Fact]
    public void Save_CallsSaveGlobal()
    {
        var prefs = new StubPreferencesService();
        var vm = new PreferencesViewModel(prefs, new OutputService(), isWorkspace: false);

        vm.Save();

        Assert.True(prefs.SaveGlobalCalled);
    }

    [Fact]
    public void Save_WorkspacePreferencesCallsSaveWorkspace()
    {
        var prefs = new StubPreferencesService();
        var vm = new PreferencesViewModel(prefs, new OutputService(), isWorkspace: true);

        vm.Save();

        Assert.True(prefs.SaveWorkspaceCalled);
        Assert.False(prefs.SaveGlobalCalled);
    }

    // --- Helpers ---

    private static PreferencesViewModel CreateViewModel(bool isWorkspace)
    {
        var prefs = new StubPreferencesService();
        return new PreferencesViewModel(prefs, new OutputService(), isWorkspace);
    }

    private sealed class StubPreferencesService : IPreferencesService
    {
        public JunexisPreferences Current { get; } = new();
        public string GlobalFilePath => "/tmp/preferences.yml";
        public string? WorkspaceFilePath => null;
        public bool SaveGlobalCalled { get; private set; }
        public bool SaveWorkspaceCalled { get; private set; }
        public void Reload() { }
        public void SaveGlobal() { SaveGlobalCalled = true; }
        public void SaveWorkspace() { SaveWorkspaceCalled = true; }
    }
}
