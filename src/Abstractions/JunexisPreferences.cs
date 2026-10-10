namespace Junexis.Studio.Abstractions;

/// <summary>
/// Główny model preferencji JunexisStudio, odpowiadający strukturze pliku preferences.yml.
/// Każda sekcja jest zagnieżdżonym obiektem z domyślnymi wartościami.
/// </summary>
public sealed class JunexisPreferences
{
    /// <summary>
    /// Motyw kolorystyczny aplikacji: "Dark" lub "Light".
    /// </summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// Preferencje edytora kodu.
    /// </summary>
    public EditorPreferences Editor { get; set; } = new();

    /// <summary>
    /// Preferencje kolorów składni per język.
    /// </summary>
    public SyntaxColorPreferences Syntax { get; set; } = new();

    /// <summary>
    /// Ustawienia auto-builda (wyzwalanie po zapisie pliku).
    /// </summary>
    public AutoBuildPreferences AutoBuild { get; set; } = new();

    /// <summary>
    /// Ustawienia obecności Junexis Mentor.
    /// </summary>
    public MentorPreferences Mentor { get; set; } = new();
}

/// <summary>
/// Ustawienia edytora kodu (czcionka, tabulacja, zawijanie, autozapis).
/// </summary>
public sealed class EditorPreferences
{
    /// <summary>
    /// Rodzina czcionki edytora.
    /// </summary>
    public string FontFamily { get; set; } = "Cascadia Code,Consolas,Menlo,monospace";

    /// <summary>
    /// Rozmiar czcionki edytora (w jednostkach logicznych).
    /// </summary>
    public int FontSize { get; set; } = 14;

    /// <summary>
    /// Liczba spacji na tabulację.
    /// </summary>
    public int TabSize { get; set; } = 4;

    /// <summary>
    /// Czy zawijać tekst w edytorze.
    /// </summary>
    public bool WordWrap { get; set; } = false;

    /// <summary>
    /// Czy automatycznie zapisywać zmiany.
    /// </summary>
    public bool AutoSave { get; set; } = false;

    /// <summary>
    /// Czy wyświetlać numery linii.
    /// </summary>
    public bool ShowLineNumbers { get; set; } = true;
}

/// <summary>
/// Sekcja kolorów składni — klucz = identyfikator języka, wartość = paleta.
/// </summary>
public sealed class SyntaxColorPreferences
{
    /// <summary>
    /// Kolory składni dla JunexisLang.
    /// </summary>
    public LanguageColorPalette Junexis { get; set; } = LanguageColorPalette.JunexisDefaults();

    /// <summary>
    /// Kolory składni dla C#.
    /// </summary>
    public LanguageColorPalette CSharp { get; set; } = LanguageColorPalette.CSharpDefaults();
}

/// <summary>
/// Konfigurowalna paleta kolorów składni dla jednego języka.
/// Wartości to stringi hex, np. "#569CD6". Null oznacza "użyj domyślnego".
/// </summary>
public sealed class LanguageColorPalette
{
    /// <summary>Kolor komentarzy.</summary>
    public string? Comment { get; set; }

    /// <summary>Kolor komentarzy dokumentacyjnych.</summary>
    public string? DocComment { get; set; }

    /// <summary>Kolor stringów.</summary>
    public string? String { get; set; }

    /// <summary>Kolor literałów liczbowych.</summary>
    public string? Number { get; set; }

    /// <summary>Kolor słów kluczowych.</summary>
    public string? Keyword { get; set; }

    /// <summary>Kolor słów kluczowych sterowania przepływem.</summary>
    public string? ControlFlow { get; set; }

    /// <summary>Kolor deklaracji (task, fn, struct, class).</summary>
    public string? Declaration { get; set; }

    /// <summary>Kolor nazw typów.</summary>
    public string? Type { get; set; }

    /// <summary>Kolor nazw funkcji/metod.</summary>
    public string? Function { get; set; }

    /// <summary>Kolor operatorów.</summary>
    public string? Operator { get; set; }

    /// <summary>Kolor literałów boolowskich / null.</summary>
    public string? BooleanNull { get; set; }

    /// <summary>Kolor modyfikatorów (async, public, static).</summary>
    public string? Modifier { get; set; }

    /// <summary>
    /// Kolory specyficzne dla danego języka (np. Communication, Resilience w JunexisLang).
    /// Klucz = nazwa koloru w xshd, wartość = hex.
    /// </summary>
    public Dictionary<string, string> Extra { get; set; } = new();

    /// <summary>
    /// Tworzy domyślną paletę kolorów dla JunexisLang (ciemny motyw, styl VS Code Dark+).
    /// </summary>
    public static LanguageColorPalette JunexisDefaults() => new()
    {
        Comment = "#6A9955",
        DocComment = "#608B4E",
        String = "#CE9178",
        Number = "#B5CEA8",
        Keyword = "#569CD6",
        ControlFlow = "#C586C0",
        Declaration = "#569CD6",
        Type = "#4EC9B0",
        Function = "#FFE484",
        Operator = "#D4D4D4",
        BooleanNull = "#569CD6",
        Modifier = "#569CD6",
        Extra = new Dictionary<string, string>
        {
            ["Communication"] = "#4EC9B0",
            ["Resilience"] = "#D7BA7D",
            ["Lifecycle"] = "#D7BA7D",
            ["PlatformAnnotation"] = "#D16969",
            ["NarrativeAttribute"] = "#DCDCAA",
            ["PrimitiveType"] = "#4EC9B0",
            ["Duration"] = "#B5CEA8",
            ["InterpolatedString"] = "#CE9178",
            ["CommunicationOp"] = "#4EC9B0",
            ["PipelineOp"] = "#4EC9B0",
        }
    };

    /// <summary>
    /// Tworzy domyślną paletę kolorów dla C# (ciemny motyw, styl VS Code Dark+).
    /// </summary>
    public static LanguageColorPalette CSharpDefaults() => new()
    {
        Comment = "#6A9955",
        DocComment = "#608B4E",
        String = "#CE9178",
        Number = "#B5CEA8",
        Keyword = "#569CD6",
        ControlFlow = "#C586C0",
        Declaration = "#569CD6",
        Type = "#4EC9B0",
        Function = "#DCDCAA",
        Operator = "#D4D4D4",
        BooleanNull = "#569CD6",
        Modifier = "#569CD6",
        Extra = new Dictionary<string, string>
        {
            ["Preprocessor"] = "#9B9B9B",
            ["Namespace"] = "#4EC9B0",
        }
    };
}
/// <summary>
/// Ustawienia funkcji auto-build — automatyczny build po zapisaniu pliku.
/// </summary>
public sealed class AutoBuildPreferences
{
    /// <summary>
    /// Czy auto-build jest aktywny.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Opóźnienie debounce w milisekundach przed wyzwoleniem builda po zdarzeniu zapisu.
    /// </summary>
    public int DebounceMs { get; set; } = 500;
}

/// <summary>Controls how Mentor participates in the Studio workflow.</summary>
public sealed class MentorPreferences
{
    /// <summary>Gets or sets the Mentor presentation mode.</summary>
    public MentorPresenceMode Presence { get; set; } = MentorPresenceMode.Teaching;
}
