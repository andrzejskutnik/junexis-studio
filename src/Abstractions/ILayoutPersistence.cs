namespace Junexis.Studio.Abstractions;

/// <summary>
/// Persystencja layoutu paneli: zapis/odczyt rozmieszczenia paneli między uruchomieniami.
/// </summary>
public interface ILayoutPersistence
{
    /// <summary>
    /// Wczytuje zapisany layout. Zwraca <see langword="null"/> jeśli brak zapisu
    /// lub schemat jest nieczytelny.
    /// </summary>
    /// <returns>Deskryptor layoutu lub <see langword="null"/>.</returns>
    LayoutDescriptor? Load();

    /// <summary>
    /// Zapisuje layout do trwałego magazynu.
    /// </summary>
    /// <param name="layout">Deskryptor layoutu do zapisu.</param>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="layout"/> jest <see langword="null"/>.</exception>
    void Save(LayoutDescriptor layout);
}

/// <summary>
/// Deskryptor layoutu paneli — wersjonowany schemat.
/// </summary>
/// <param name="Version">Numer wersji schematu.</param>
/// <param name="Panels">Lista rozmieszczonych paneli.</param>
public sealed record LayoutDescriptor(int Version, IReadOnlyList<PanelPlacement> Panels);

/// <summary>
/// Położenie pojedynczego panelu w layoucie.
/// </summary>
/// <param name="PanelId">Identyfikator panelu (zgodny z <see cref="PanelRegistration.Id"/>).</param>
/// <param name="DockGroup">Grupa dokowania (Left/Right/Bottom/Center).</param>
/// <param name="Order">Kolejność w grupie (niższe = wyżej/bardziej na lewo).</param>
/// <param name="IsVisible">Czy panel jest widoczny (domyślnie <see langword="true"/>).</param>
public sealed record PanelPlacement(
    string PanelId,
    DockGroup DockGroup,
    int Order,
    bool IsVisible = true);
