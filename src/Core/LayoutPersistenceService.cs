using System.Text.Json;
using System.Text.Json.Serialization;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Serwis persystencji layoutu — zapisuje/odczytuje rozmieszczenie paneli
/// w pliku konfiguracyjnym workspace (<see cref="WorkspaceConfig.FileName"/>).
/// Obsługuje wersjonowanie schematu i ignoruje nieznane panele.
/// </summary>
public sealed class LayoutPersistenceService : ILayoutPersistence
{
    /// <summary>
    /// Bieżąca wersja schematu layoutu.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Klucz JSON pod którym przechowywany jest layout w pliku konfiguracyjnym.
    /// </summary>
    internal const string LayoutKey = "layout";

    private readonly IWorkspace _workspace;
    private readonly IOutput? _output;
    private readonly IPanelRegistry? _panels;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Tworzy serwis persystencji layoutu.
    /// </summary>
    /// <param name="workspace">Workspace (do odczytu/zapisu konfiguracji).</param>
    /// <param name="output">Output do logowania (opcjonalny).</param>
    /// <param name="panels">Rejestr paneli do walidacji (opcjonalny).</param>
    public LayoutPersistenceService(IWorkspace workspace, IOutput? output = null, IPanelRegistry? panels = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _output = output;
        _panels = panels;
    }

    /// <inheritdoc />
    public LayoutDescriptor? Load()
    {
        string? raw;
        try
        {
            raw = _workspace.GetWorkspaceConfigRaw();
        }
        catch (Exception ex)
        {
            _output?.Write("Layout", $"Failed to read workspace config: {ex.Message}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            _output?.Write("Layout", $"Workspace config is not valid JSON: {ex.Message}");
            return null;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty(LayoutKey, out var layoutElement))
            {
                return null;
            }

            LayoutDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<LayoutDto>(layoutElement.GetRawText(), JsonOptions);
            }
            catch (JsonException ex)
            {
                _output?.Write("Layout", $"Layout section is malformed: {ex.Message}");
                return null;
            }

            if (dto is null)
            {
                return null;
            }

            if (dto.Version < 1)
            {
                _output?.Write("Layout", $"Unknown layout schema version: {dto.Version}, ignoring.");
                return null;
            }

            if (dto.Version > CurrentSchemaVersion)
            {
                _output?.Write("Layout", $"Layout schema version {dto.Version} is newer than supported ({CurrentSchemaVersion}). Ignoring.");
                return null;
            }

            var knownPanelIds = GetRegisteredPanelIds();
            var placements = new List<PanelPlacement>();

            foreach (var p in dto.Panels ?? [])
            {
                if (string.IsNullOrWhiteSpace(p.PanelId))
                {
                    continue;
                }

                if (knownPanelIds is not null && !knownPanelIds.Contains(p.PanelId))
                {
                    _output?.Write("Layout", $"Skipping unknown panel '{p.PanelId}' from saved layout.");
                    continue;
                }

                if (!Enum.TryParse<DockGroup>(p.DockGroup, ignoreCase: true, out var dock))
                {
                    dock = DockGroup.Left;
                }

                placements.Add(new PanelPlacement(p.PanelId, dock, p.Order, p.IsVisible));
            }

            return new LayoutDescriptor(dto.Version, placements);
        }
    }

    /// <inheritdoc />
    public void Save(LayoutDescriptor layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var dto = new LayoutDto
        {
            Version = CurrentSchemaVersion,
            Panels = layout.Panels
                .Select(p => new PanelPlacementDto
                {
                    PanelId = p.PanelId,
                    DockGroup = p.DockGroup.ToString(),
                    Order = p.Order,
                    IsVisible = p.IsVisible,
                })
                .ToList(),
        };

        string existingRaw;
        try
        {
            existingRaw = _workspace.GetWorkspaceConfigRaw() ?? "{}";
        }
        catch
        {
            existingRaw = "{}";
        }

        Dictionary<string, JsonElement> existing;
        try
        {
            existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingRaw, JsonOptions)
                       ?? new Dictionary<string, JsonElement>();
        }
        catch
        {
            existing = new Dictionary<string, JsonElement>();
        }

        var layoutJson = JsonSerializer.SerializeToElement(dto, JsonOptions);
        existing[LayoutKey] = layoutJson;

        var merged = JsonSerializer.Serialize(existing, JsonOptions);

        try
        {
            _workspace.SetWorkspaceConfigRaw(merged);
        }
        catch (Exception ex)
        {
            _output?.Write("Layout", $"Failed to save layout: {ex.Message}");
        }
    }

    private HashSet<string>? GetRegisteredPanelIds()
    {
        if (_panels is null)
        {
            return null;
        }

        try
        {
            var regs = _panels.GetRegisteredPanels();
            return new HashSet<string>(regs.Select(r => r.Id), StringComparer.Ordinal);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// DTO do serializacji layoutu.
    /// </summary>
    internal sealed class LayoutDto
    {
        public int Version { get; set; }
        public List<PanelPlacementDto>? Panels { get; set; }
    }

    /// <summary>
    /// DTO panelu w layoucie.
    /// </summary>
    internal sealed class PanelPlacementDto
    {
        public string PanelId { get; set; } = string.Empty;
        public string DockGroup { get; set; } = "Left";
        public int Order { get; set; }
        public bool IsVisible { get; set; } = true;
    }
}
