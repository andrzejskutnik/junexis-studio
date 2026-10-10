namespace Junexis.Studio.Abstractions;

/// <summary>
/// Zdarzenie domenowe publikowane, gdy użytkownik chce otworzyć okno preferencji.
/// </summary>
/// <param name="IsWorkspace">
/// <see langword="true"/> — preferencje workspace, <see langword="false"/> — globalne.
/// </param>
public sealed record OpenPreferencesRequested(bool IsWorkspace);
