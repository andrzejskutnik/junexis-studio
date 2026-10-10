using System;
using System.IO;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace Junexis.Studio.Shell;

/// <summary>
/// Rejestruje niestandardowe definicje kolorowania składni (xshd)
/// w <see cref="HighlightingManager.Instance"/>.
/// </summary>
public static class JunexisHighlightingRegistrar
{
    private static bool _registered;
    private static readonly object _lock = new();

    /// <summary>
    /// Nazwa zarejestrowanej definicji kolorowania JunexisLang.
    /// </summary>
    public const string JunexisLangName = "JunexisLang";

    /// <summary>
    /// Rozszerzenie pliku JunexisLang.
    /// </summary>
    public const string JunexisLangExtension = ".jnx";

    /// <summary>Extensions covered by the custom C/C++ highlighting.</summary>
    public static readonly string[] CppExtensions = { ".cpp", ".c", ".h", ".hpp", ".cc", ".cxx", ".hxx" };

    /// <summary>
    /// Nazwa zarejestrowanej definicji kolorowania YAML.
    /// </summary>
    public const string YamlName = "YAML";

    /// <summary>
    /// Rozszerzenia plików YAML.
    /// </summary>
    public static readonly string[] YamlExtensions = { ".yml", ".yaml" };

    /// <summary>
    /// Rejestruje wszystkie niestandardowe definicje kolorowania składni.
    /// Bezpieczne do wielokrotnego wywoływania (idempotent).
    /// </summary>
    public static void RegisterAll()
    {
        if (_registered) return;
        lock (_lock)
        {
            if (_registered) return;

            RegisterJunexisLang();
            RegisterYaml();
            RegisterCpp();
            _registered = true;
        }
    }

    /// <summary>
    /// Zwraca definicję kolorowania pasującą do rozszerzenia pliku,
    /// lub <c>null</c> jeśli brak dopasowania.
    /// </summary>
    /// <param name="filePath">Ścieżka pliku (wystarczy sama nazwa z rozszerzeniem).</param>
    /// <returns>Definicja kolorowania lub <c>null</c>.</returns>
    public static IHighlightingDefinition? GetHighlightingForFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var ext = Path.GetExtension(filePath);

        // Najpierw sprawdzamy nasze niestandardowe definicje
        if (string.Equals(ext, JunexisLangExtension, StringComparison.OrdinalIgnoreCase))
        {
            return HighlightingManager.Instance.GetDefinition(JunexisLangName);
        }

        if (Array.Exists(YamlExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
        {
            return HighlightingManager.Instance.GetDefinition(YamlName);
        }

        if (Array.Exists(CppExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
        {
            return HighlightingManager.Instance.GetDefinition("C++");
        }

        // Potem fallback do wbudowanych AvaloniaEdit (C#, XML, JSON, JS itd.)
        return HighlightingManager.Instance.GetDefinitionByExtension(ext);
    }

    private static void RegisterJunexisLang()
    {
        const string resourceName = "Junexis.Studio.Shell.Highlighting.JunexisLang.xshd";

        using var stream = typeof(JunexisHighlightingRegistrar).Assembly
            .GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. Check EmbeddedResource in Shell.csproj.");
        }

        using var reader = new XmlTextReader(stream);
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);

        HighlightingManager.Instance.RegisterHighlighting(
            JunexisLangName,
            new[] { JunexisLangExtension },
            definition);
    }

    private static void RegisterYaml()
    {
        const string resourceName = "Junexis.Studio.Shell.Highlighting.Yaml.xshd";

        using var stream = typeof(JunexisHighlightingRegistrar).Assembly
            .GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. Check EmbeddedResource in Shell.csproj.");
        }

        using var reader = new XmlTextReader(stream);
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);

        HighlightingManager.Instance.RegisterHighlighting(
            YamlName,
            YamlExtensions,
            definition);
    }

    private static void RegisterCpp()
    {
        const string resourceName = "Junexis.Studio.Shell.Highlighting.Cpp.xshd";

        using var stream = typeof(JunexisHighlightingRegistrar).Assembly
            .GetManifestResourceStream(resourceName);

        if (stream is null) return; // non-fatal — falls back to built-in

        using var reader = new XmlTextReader(stream);
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);

        HighlightingManager.Instance.RegisterHighlighting(
            "C++",
            CppExtensions,
            definition);
    }
}
