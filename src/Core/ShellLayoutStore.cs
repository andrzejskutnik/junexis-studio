using Junexis.Studio.Abstractions;
using System;
using System.IO;
using System.Text.Json;

namespace Junexis.Studio.Core;

/// <summary>
/// Model persystencji rozmiarów paneli okna głównego (GridSplitter).
/// </summary>
/// <param name="ExplorerWidth">Szerokość panelu Explorer [px].</param>
/// <param name="ToolTabsHeight">Wysokość paska narzędzi (ToolTabs) [px].</param>
/// <param name="RightPanelWidth">Szerokość prawego panelu narzędzi [px].</param>
/// <param name="BottomPanelId">Ostatnio wybrany panel dolny.</param>
/// <param name="RightPanelId">Ostatnio wybrany panel prawy.</param>
public sealed record ShellLayoutData(
    double ExplorerWidth,
    double ToolTabsHeight,
    double RightPanelWidth = 380,
    string? BottomPanelId = null,
    string? RightPanelId = null)
{
    /// <summary>Domyślny układ odpowiadający wartościom w XAML.</summary>
    public static ShellLayoutData Default { get; } = new(248, 190, 380);
}

/// <summary>
/// Zapisuje i wczytuje pozycje GridSplitter okna głównego do/z pliku JSON.
/// Domyślna ścieżka: <c>%APPDATA%/JunexisStudio/shell-layout.json</c>.
/// Błędy odczytu/zapisu są łknięte — store jest non-critical.
/// </summary>
public sealed class ShellLayoutStore
{
    private const double MinSize = 40;
    private const double MaxSize = 6000;

    private readonly string _filePath;

    /// <summary>
    /// Tworzy store z domyślną ścieżką (<c>%APPDATA%/JunexisStudio/shell-layout.json</c>).
    /// </summary>
    public ShellLayoutStore()
        : this(filePath: null)
    {
    }

    /// <summary>
    /// Tworzy store z jawną ścieżką (przydatne w testach).
    /// </summary>
    /// <param name="filePath">Ścieżka do pliku JSON lub <see langword="null"/> dla wartości domyślnej.</param>
    public ShellLayoutStore(string? filePath)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JunexisStudio",
            "shell-layout.json");
    }

    /// <summary>
    /// Wczytuje layout z pliku. Zwraca <see cref="ShellLayoutData.Default"/> przy dowolnym błędzie.
    /// </summary>
    /// <returns>Wczytane dane lub wartości domyślne.</returns>
    public ShellLayoutData Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return ShellLayoutData.Default;
            }

            var json = File.ReadAllText(_filePath);
            var dto = JsonSerializer.Deserialize<LayoutDto>(json);

            if (dto is null)
            {
                return ShellLayoutData.Default;
            }

            var w = Clamp(dto.ExplorerWidth, ShellLayoutData.Default.ExplorerWidth);
            var h = Clamp(dto.ToolTabsHeight, ShellLayoutData.Default.ToolTabsHeight);
            var r = Clamp(dto.RightPanelWidth, ShellLayoutData.Default.RightPanelWidth);
            return new ShellLayoutData(w, h, r, dto.BottomPanelId, dto.RightPanelId);
        }
        catch
        {
            return ShellLayoutData.Default;
        }
    }

    /// <summary>
    /// Zapisuje layout do pliku. Tworzy katalog jeśli nie istnieje.
    /// Błędy zapisu są łknięte.
    /// </summary>
    /// <param name="data">Dane do zapisu.</param>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="data"/> jest <see langword="null"/>.</exception>
    public void Save(ShellLayoutData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(
                new LayoutDto(
                    data.ExplorerWidth,
                    data.ToolTabsHeight,
                    data.RightPanelWidth,
                    data.BottomPanelId,
                    data.RightPanelId),
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Non-critical — brak zapisu nie przerywa działania aplikacji.
        }
    }

    private static double Clamp(double value, double fallback)
        => double.IsNaN(value) || value < MinSize || value > MaxSize ? fallback : value;

    private sealed record LayoutDto(
        double ExplorerWidth,
        double ToolTabsHeight,
        double RightPanelWidth = 380,
        string? BottomPanelId = null,
        string? RightPanelId = null);
}
