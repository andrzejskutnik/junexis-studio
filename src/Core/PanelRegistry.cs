using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Rejestr paneli (Tool/Document/Temporary) dla powłoki UI.
/// </summary>
public sealed class PanelRegistry : IPanelRegistry
{
    private readonly Dictionary<string, PanelEntry> _panels = new(StringComparer.Ordinal);

    /// <summary>
    /// Deskryptor wewnętrzny wpisu w rejestrze paneli.
    /// </summary>
    private sealed record PanelEntry(PanelType Type, Func<object> Factory, string? Title, DockGroup DockGroup);

    /// <inheritdoc />
    public void RegisterToolPanel(string id, Func<object> factory)
        => RegisterPanel(id, PanelType.Tool, factory);

    /// <inheritdoc />
    public void RegisterDocumentPanel(string id, Func<object> factory)
        => RegisterPanel(id, PanelType.Document, factory);

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Gdy <paramref name="id"/> jest puste.</exception>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="factory"/> jest <see langword="null"/>.</exception>
    public void RegisterPanel(string id, PanelType type, Func<object> factory, string? title = null, DockGroup dockGroup = DockGroup.Left)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Panel id cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(factory);

        _panels[id] = new PanelEntry(type, factory, title, dockGroup);
    }

    /// <inheritdoc />
    public IReadOnlyList<PanelRegistration> GetRegisteredPanels()
    {
        var registrations = new List<PanelRegistration>(_panels.Count);

        foreach (var (id, entry) in _panels)
        {
            registrations.Add(new PanelRegistration(id, entry.Type, entry.Factory, entry.Title, entry.DockGroup));
        }

        return registrations;
    }
}
