using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Węzeł drzewa preferencji — reprezentuje sekcję (grupę) lub konkretną wartość.
/// </summary>
public sealed class PreferenceNode : INotifyPropertyChanged
{
    private string _value = string.Empty;

    /// <summary>
    /// Tworzy węzeł preferencji.
    /// </summary>
    /// <param name="key">Nazwa klucza (np. "FontSize", "Theme").</param>
    /// <param name="path">Pełna ścieżka w drzewie (np. "Editor.FontSize").</param>
    /// <param name="value">Bieżąca wartość (string). Puste dla węzłów-grup.</param>
    /// <param name="isGroup">Czy węzeł jest grupą (ma dzieci).</param>
    /// <param name="valueType">Typ wartości (np. typeof(int), typeof(bool)).</param>
    public PreferenceNode(string key, string path, string value, bool isGroup, Type? valueType = null)
    {
        Key = key;
        Path = path;
        _value = value;
        IsGroup = isGroup;
        ValueType = valueType;
    }

    /// <summary>Klucz wyświetlany w drzewie.</summary>
    public string Key { get; }

    /// <summary>Pełna ścieżka klucza (np. "Editor.FontSize").</summary>
    public string Path { get; }

    /// <summary>Czy jest węzłem-grupą (nie edytowalny bezpośrednio).</summary>
    public bool IsGroup { get; }

    /// <summary>Typ wartości CLR.</summary>
    public Type? ValueType { get; }

    /// <summary>Czy węzeł jest liściem (edytowalny).</summary>
    public bool IsLeaf => !IsGroup;

    /// <summary>Czy wartość to boolean (do renderowania jako CheckBox).</summary>
    public bool IsBool => ValueType == typeof(bool);

    /// <summary>Czy wartość jest enumem wybieranym z zamkniętej listy.</summary>
    public bool IsEnum => ValueType?.IsEnum == true;

    /// <summary>Dostępne nazwy wartości enum.</summary>
    public IReadOnlyList<string> EnumValues => ValueType?.IsEnum == true
        ? Enum.GetNames(ValueType)
        : [];

    /// <summary>Czy wartość to tekst/liczba (TextBox).</summary>
    public bool IsText => IsLeaf && !IsBool && !IsEnum;

    /// <summary>Bieżąca wartość boolean (wygodna bindowalność dla CheckBox).</summary>
    public bool BoolValue
    {
        get => bool.TryParse(_value, out var b) && b;
        set
        {
            Value = value.ToString();
        }
    }

    /// <summary>Bieżąca wartość jako string.</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoolValue)));
        }
    }

    /// <summary>Węzły potomne.</summary>
    public ObservableCollection<PreferenceNode> Children { get; } = new();

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// ViewModel okna preferencji — buduje drzewo z <see cref="JunexisPreferences"/>
/// i obsługuje zapis zmian.
/// </summary>
public sealed class PreferencesViewModel : INotifyPropertyChanged
{
    private readonly IPreferencesService _preferences;
    private readonly IOutput _output;
    private readonly bool _isWorkspace;

    /// <summary>
    /// Tworzy ViewModel preferencji.
    /// </summary>
    /// <param name="preferences">Serwis preferencji.</param>
    /// <param name="output">Output do logowania.</param>
    /// <param name="isWorkspace">Czy edytujemy preferencje workspace (true) czy globalne (false).</param>
    public PreferencesViewModel(IPreferencesService preferences, IOutput output, bool isWorkspace)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _isWorkspace = isWorkspace;

        Title = isWorkspace ? "Workspace Preferences" : "Preferences";
        LoadFields();
        BuildTree();
    }

    // ─── Typed form fields ────────────────────────────────────────────────

    /// <summary>Tytuł okna.</summary>
    public string Title { get; }

    /// <summary>Motyw: "Dark" lub "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Rodzina czcionki edytora.</summary>
    public string EditorFontFamily { get; set; } = string.Empty;

    /// <summary>Rozmiar czcionki edytora.</summary>
    public int EditorFontSize { get; set; }

    /// <summary>Szerokość tabulacji w spacjach.</summary>
    public int EditorTabSize { get; set; }

    /// <summary>Zawijanie tekstu.</summary>
    public bool EditorWordWrap { get; set; }

    /// <summary>Automatyczny zapis po modyfikacji.</summary>
    public bool EditorAutoSave { get; set; }

    /// <summary>Wyświetlanie numerów linii.</summary>
    public bool EditorShowLineNumbers { get; set; }

    /// <summary>Automatyczny build po zapisie pliku.</summary>
    public bool AutoBuildEnabled { get; set; }

    /// <summary>Opóźnienie auto-builda w ms.</summary>
    public int AutoBuildDebounceMs { get; set; }

    /// <summary>Tryb obecności Mentora.</summary>
    public MentorPresenceMode MentorPresence { get; set; }

    /// <summary>String representation of <see cref="MentorPresence"/> for ComboBox binding.</summary>
    public string MentorPresenceString
    {
        get => MentorPresence.ToString();
        set
        {
            if (Enum.TryParse<MentorPresenceMode>(value, out var mode))
                MentorPresence = mode;
        }
    }

    /// <summary>Dostępne wartości enum dla Mentor Presence (do ComboBox).</summary>
    public IReadOnlyList<string> MentorPresenceModes { get; } = Enum.GetNames<MentorPresenceMode>();

    /// <summary>Korzenie drzewa preferencji (legacy, zachowane dla kompatybilności).</summary>
    public ObservableCollection<PreferenceNode> Roots { get; } = new();

    // ─── Load / Save ──────────────────────────────────────────────────────

    private void LoadFields()
    {
        var p = _preferences.Current;
        Theme = p.Theme ?? "Dark";
        EditorFontFamily = p.Editor.FontFamily;
        EditorFontSize = p.Editor.FontSize;
        EditorTabSize = p.Editor.TabSize;
        EditorWordWrap = p.Editor.WordWrap;
        EditorAutoSave = p.Editor.AutoSave;
        EditorShowLineNumbers = p.Editor.ShowLineNumbers;
        AutoBuildEnabled = p.AutoBuild.Enabled;
        AutoBuildDebounceMs = p.AutoBuild.DebounceMs;
        MentorPresence = p.Mentor.Presence;
    }

    /// <summary>Zapisuje pola formularza do modelu i persystuje.</summary>
    public void SaveFromFields()
    {
        try
        {
            var p = _preferences.Current;
            p.Theme = Theme;
            p.Editor.FontFamily = EditorFontFamily;
            p.Editor.FontSize = EditorFontSize;
            p.Editor.TabSize = EditorTabSize;
            p.Editor.WordWrap = EditorWordWrap;
            p.Editor.AutoSave = EditorAutoSave;
            p.Editor.ShowLineNumbers = EditorShowLineNumbers;
            p.AutoBuild.Enabled = AutoBuildEnabled;
            p.AutoBuild.DebounceMs = AutoBuildDebounceMs;
            p.Mentor.Presence = MentorPresence;

            if (_isWorkspace)
                _preferences.SaveWorkspace();
            else
                _preferences.SaveGlobal();

            _output.Write("Preferences",
                _isWorkspace ? "Preferencje workspace zapisane." : "Preferencje globalne zapisane.");
        }
        catch (Exception ex)
        {
            _output.Write("Preferences", $"Błąd zapisu preferencji: {ex.Message}",
                OutputSeverity.Error, "Host", ex.ToString());
        }
    }

    /// <summary>Zapisuje preferencje z drzewa refleksji (legacy).</summary>
    public void Save()
    {
        try
        {
            ApplyToModel(_preferences.Current);
            if (_isWorkspace)
                _preferences.SaveWorkspace();
            else
                _preferences.SaveGlobal();
            _output.Write(
                "Preferences",
                _isWorkspace ? "Preferencje workspace zapisane." : "Preferencje globalne zapisane.");
        }
        catch (Exception ex)
        {
            _output.Write("Preferences", $"Błąd zapisu preferencji: {ex.Message}",
                OutputSeverity.Error, "Host", ex.ToString());
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Buduje drzewo z bieżącego modelu preferencji.</summary>
    internal void BuildTree()
    {
        Roots.Clear();
        var prefs = _preferences.Current;
        var nodes = BuildNodes(prefs, string.Empty);
        foreach (var node in nodes)
        {
            Roots.Add(node);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Roots)));
    }

    /// <summary>Rekurencyjnie buduje węzły z obiektu preferencji.</summary>
    internal static List<PreferenceNode> BuildNodes(object obj, string prefix)
    {
        var result = new List<PreferenceNode>();
        if (obj is null) return result;

        var type = obj.GetType();
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .OrderBy(p => p.Name);

        foreach (var prop in props)
        {
            var path = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
            var value = prop.GetValue(obj);
            var propType = prop.PropertyType;

            if (IsSimpleType(propType))
            {
                var strValue = value?.ToString() ?? string.Empty;
                result.Add(new PreferenceNode(prop.Name, path, strValue, isGroup: false, valueType: propType));
            }
            else if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                // Dictionary<string,string> — traktuj jako grupę z kluczami
                var dictNode = new PreferenceNode(prop.Name, path, string.Empty, isGroup: true);
                if (value is Dictionary<string, string> dict)
                {
                    foreach (var kvp in dict.OrderBy(k => k.Key))
                    {
                        var childPath = $"{path}.{kvp.Key}";
                        dictNode.Children.Add(new PreferenceNode(kvp.Key, childPath, kvp.Value, isGroup: false, valueType: typeof(string)));
                    }
                }
                result.Add(dictNode);
            }
            else if (propType.IsClass)
            {
                // Zagnieżdżony obiekt — grupa
                var groupNode = new PreferenceNode(prop.Name, path, string.Empty, isGroup: true);
                if (value is not null)
                {
                    var children = BuildNodes(value, path);
                    foreach (var child in children)
                    {
                        groupNode.Children.Add(child);
                    }
                }
                result.Add(groupNode);
            }
        }

        return result;
    }

    /// <summary>
    /// Aplikuje wartości z drzewa node'ów z powrotem do modelu.
    /// </summary>
    internal void ApplyToModel(JunexisPreferences prefs)
    {
        ApplyNodes(Roots, prefs, string.Empty);
    }

    private static void ApplyNodes(IEnumerable<PreferenceNode> nodes, object target, string prefix)
    {
        foreach (var node in nodes)
        {
            var propName = node.Key;
            var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null || !prop.CanWrite) continue;

            if (node.IsGroup)
            {
                var child = prop.GetValue(target);
                if (child is Dictionary<string, string> dict)
                {
                    dict.Clear();
                    foreach (var cn in node.Children)
                    {
                        dict[cn.Key] = cn.Value;
                    }
                }
                else if (child is not null)
                {
                    ApplyNodes(node.Children, child, node.Path);
                }
            }
            else
            {
                var converted = ConvertValue(node.Value, prop.PropertyType);
                if (converted is not null)
                {
                    prop.SetValue(target, converted);
                }
            }
        }
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        try
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int) && int.TryParse(value, out var i)) return i;
            if (targetType == typeof(bool) && bool.TryParse(value, out var b)) return b;
            if (targetType == typeof(double) && double.TryParse(value, out var d)) return d;
            if (targetType == typeof(float) && float.TryParse(value, out var f)) return f;
            if (targetType.IsEnum && Enum.TryParse(targetType, value, ignoreCase: true, out var enumValue))
                return enumValue;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSimpleType(Type type)
    {
        return type == typeof(string)
            || type == typeof(int)
            || type == typeof(bool)
            || type == typeof(double)
            || type == typeof(float)
            || type == typeof(long)
            || type.IsEnum;
    }
}
