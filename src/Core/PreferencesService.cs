using Junexis.Studio.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Junexis.Studio.Core;

/// <summary>
/// Implementacja <see cref="IPreferencesService"/> oparta na plikach YAML.
/// Ładuje preferencje z dwóch źródeł (globalny + workspace) i merguje je.
/// </summary>
public sealed class PreferencesService : IPreferencesService
{
    /// <summary>
    /// Nazwa katalogu konfiguracyjnego JunexisStudio.
    /// </summary>
    public const string ConfigDirName = ".junexis-studio";

    /// <summary>
    /// Nazwa pliku preferencji.
    /// </summary>
    public const string FileName = "preferences.yml";

    private readonly IWorkspace _workspace;
    private readonly IOutput _output;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve)
        .Build();

    /// <summary>
    /// Tworzy serwis preferencji.
    /// </summary>
    /// <param name="workspace">Serwis workspace (do wykrywania ścieżki workspace).</param>
    /// <param name="output">Output do logowania błędów ładowania.</param>
    /// <exception cref="ArgumentNullException">Gdy workspace lub output jest null.</exception>
    public PreferencesService(IWorkspace workspace, IOutput output)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _output = output ?? throw new ArgumentNullException(nameof(output));

        Reload();
    }

    /// <inheritdoc />
    public JunexisPreferences Current { get; private set; } = new();

    /// <inheritdoc />
    public string GlobalFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ConfigDirName,
            FileName);

    /// <inheritdoc />
    public string? WorkspaceFilePath
    {
        get
        {
            var root = _workspace.RootPath;
            if (string.IsNullOrEmpty(root))
                return null;
            return Path.Combine(root, ConfigDirName, FileName);
        }
    }

    /// <inheritdoc />
    public void Reload()
    {
        var globalPrefs = LoadFromFile(GlobalFilePath);
        var workspacePrefs = LoadFromFile(WorkspaceFilePath);

        Current = Merge(globalPrefs, workspacePrefs);
    }

    /// <inheritdoc />
    public void SaveGlobal()
    {
        SaveTo(GlobalFilePath, "globalnych");
    }

    /// <inheritdoc />
    public void SaveWorkspace()
    {
        if (WorkspaceFilePath is not { } path)
        {
            _output.Write(
                "Preferences",
                "Nie można zapisać preferencji workspace bez otwartego folderu.",
                OutputSeverity.Warning);
            return;
        }

        SaveTo(path, "workspace");
    }

    private void SaveTo(string path, string scope)
    {
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var yaml = Serializer.Serialize(Current);
            File.WriteAllText(path, yaml);
        }
        catch (Exception ex)
        {
            _output.Write("Preferences", $"Nie udało się zapisać preferencji {scope}: {ex.Message}",
                OutputSeverity.Error, "Core", ex.ToString());
        }
    }

    /// <summary>
    /// Ładuje preferencje z pliku YAML. Zwraca null gdy plik nie istnieje lub jest uszkodzony.
    /// </summary>
    /// <param name="path">Ścieżka do pliku YAML.</param>
    /// <returns>Załadowane preferencje lub null.</returns>
    internal JunexisPreferences? LoadFromFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var yaml = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(yaml))
                return null;

            return Deserializer.Deserialize<JunexisPreferences>(yaml);
        }
        catch (Exception ex)
        {
            _output.Write("Preferences", $"Błąd ładowania {path}: {ex.Message}",
                OutputSeverity.Warning, "Core", ex.ToString());
            return null;
        }
    }

    /// <summary>
    /// Łączy preferencje globalne i workspace. Workspace nadpisuje globalne.
    /// Null-e oznaczają „użyj globalnego/domyślnego".
    /// </summary>
    internal static JunexisPreferences Merge(JunexisPreferences? global, JunexisPreferences? workspace)
    {
        var g = global ?? new JunexisPreferences();
        if (workspace is null)
            return g;

        var w = workspace;

        var merged = new JunexisPreferences
        {
            Theme = !string.IsNullOrEmpty(w.Theme) && w.Theme != new JunexisPreferences().Theme
                ? w.Theme : g.Theme,
        };

        merged.Editor = MergeEditor(g.Editor, w.Editor);
        merged.Syntax = MergeSyntax(g.Syntax, w.Syntax);
        merged.Mentor = MergeMentor(g.Mentor ?? new MentorPreferences(), w.Mentor);

        return merged;
    }

    private static EditorPreferences MergeEditor(EditorPreferences g, EditorPreferences? w)
    {
        if (w is null) return g;
        var defaults = new EditorPreferences();

        return new EditorPreferences
        {
            FontFamily = w.FontFamily != defaults.FontFamily ? w.FontFamily : g.FontFamily,
            FontSize = w.FontSize != defaults.FontSize ? w.FontSize : g.FontSize,
            TabSize = w.TabSize != defaults.TabSize ? w.TabSize : g.TabSize,
            WordWrap = w.WordWrap != defaults.WordWrap ? w.WordWrap : g.WordWrap,
            AutoSave = w.AutoSave != defaults.AutoSave ? w.AutoSave : g.AutoSave,
            ShowLineNumbers = w.ShowLineNumbers != defaults.ShowLineNumbers ? w.ShowLineNumbers : g.ShowLineNumbers,
        };
    }

    private static SyntaxColorPreferences MergeSyntax(SyntaxColorPreferences g, SyntaxColorPreferences? w)
    {
        if (w is null) return g;

        return new SyntaxColorPreferences
        {
            Junexis = MergePalette(g.Junexis, w.Junexis),
            CSharp = MergePalette(g.CSharp, w.CSharp),
        };
    }

    private static MentorPreferences MergeMentor(MentorPreferences g, MentorPreferences? w)
    {
        if (w is null) return g;
        var defaults = new MentorPreferences();

        return new MentorPreferences
        {
            Presence = w.Presence != defaults.Presence ? w.Presence : g.Presence,
        };
    }

    private static LanguageColorPalette MergePalette(LanguageColorPalette g, LanguageColorPalette? w)
    {
        if (w is null) return g;

        var merged = new LanguageColorPalette
        {
            Comment = w.Comment ?? g.Comment,
            DocComment = w.DocComment ?? g.DocComment,
            String = w.String ?? g.String,
            Number = w.Number ?? g.Number,
            Keyword = w.Keyword ?? g.Keyword,
            ControlFlow = w.ControlFlow ?? g.ControlFlow,
            Declaration = w.Declaration ?? g.Declaration,
            Type = w.Type ?? g.Type,
            Function = w.Function ?? g.Function,
            Operator = w.Operator ?? g.Operator,
            BooleanNull = w.BooleanNull ?? g.BooleanNull,
            Modifier = w.Modifier ?? g.Modifier,
        };

        // Merge Extra: workspace nadpisuje globalne klucze, dodaje nowe
        foreach (var kv in g.Extra)
            merged.Extra[kv.Key] = kv.Value;
        foreach (var kv in w.Extra)
            merged.Extra[kv.Key] = kv.Value;

        return merged;
    }
}
