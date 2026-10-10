namespace Junexis.Studio.Abstractions;

/// <summary>
/// Rejestr paneli dla powłoki UI.
/// W v0.1 Shell może budować layout dynamicznie na bazie zarejestrowanych paneli.
/// </summary>
public interface IPanelRegistry
{
    /// <summary>
    /// Rejestruje panel narzędziowy (tool panel).
    /// </summary>
    /// <param name="id">Id panelu.</param>
    /// <param name="factory">Fabryka tworząca view model / obiekt panelu.</param>
    void RegisterToolPanel(string id, Func<object> factory);

    /// <summary>
    /// Rejestruje panel dokumentu (document panel).
    /// </summary>
    /// <param name="id">Id panelu.</param>
    /// <param name="factory">Fabryka tworząca view model / obiekt panelu.</param>
    void RegisterDocumentPanel(string id, Func<object> factory);

    /// <summary>
    /// Rejestruje panel z pełnymi metadanymi (typ, tytuł, grupa dokowania).
    /// </summary>
    /// <param name="id">Id panelu.</param>
    /// <param name="type">Typ panelu (Tool/Document/Temporary).</param>
    /// <param name="factory">Fabryka tworząca view model / obiekt panelu.</param>
    /// <param name="title">Wyświetlany tytuł panelu (opcjonalny).</param>
    /// <param name="dockGroup">Preferowana grupa dokowania (opcjonalna; domyślnie Left).</param>
    void RegisterPanel(string id, PanelType type, Func<object> factory, string? title = null, DockGroup dockGroup = DockGroup.Left);

    /// <summary>
    /// Zwraca snapshot zarejestrowanych paneli.
    /// </summary>
    /// <returns>Snapshot rejestracji paneli.</returns>
    IReadOnlyList<PanelRegistration> GetRegisteredPanels();
}

/// <summary>Requests that Shell activate an already registered panel.</summary>
/// <param name="PanelId">Stable panel identifier.</param>
public sealed record ActivatePanelRequested(string PanelId);
