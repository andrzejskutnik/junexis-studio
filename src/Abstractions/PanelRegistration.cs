namespace Junexis.Studio.Abstractions;

/// <summary>
/// Rejestracja panelu (Tool/Document/Temporary) dla powłoki UI.
/// </summary>
/// <param name="Id">Id panelu.</param>
/// <param name="Type">Typ panelu.</param>
/// <param name="Factory">Fabryka tworząca obiekt panelu (view lub view model).</param>
/// <param name="Title">Tytuł wyświetlany w UI (opcjonalny; domyślnie <see langword="null"/> — Shell użyje Id).</param>
/// <param name="DockGroup">Preferowana grupa dokowania (opcjonalna; domyślnie <see cref="Abstractions.DockGroup.Left"/>).</param>
public sealed record PanelRegistration(
    string Id,
    PanelType Type,
    Func<object> Factory,
    string? Title = null,
    DockGroup DockGroup = DockGroup.Left);
