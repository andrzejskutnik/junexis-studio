namespace Junexis.Studio.Abstractions;

/// <summary>Pojedynczy artefakt wyprodukowany przez build Junexis.</summary>
/// <param name="RelativePath">Ścieżka względem katalogu output.</param>
/// <param name="FullPath">Zweryfikowana ścieżka bezwzględna.</param>
/// <param name="Target">Target artefaktu.</param>
/// <param name="Kind">Rodzaj artefaktu.</param>
/// <param name="Ownership">Własność: generated lub user-overlay.</param>
/// <param name="Sha256">Hash zawartości zapisany przez kompilator.</param>
public sealed record ArtifactItem(
    string RelativePath,
    string FullPath,
    string Target,
    string Kind,
    string Ownership,
    string Sha256);

/// <summary>Zweryfikowane inventory artefaktów jednego builda.</summary>
/// <param name="BuildId">Identyfikator builda.</param>
/// <param name="ManifestPath">Ścieżka wczytanego manifestu.</param>
/// <param name="Items">Artefakty builda.</param>
public sealed record ArtifactInventorySnapshot(
    string BuildId,
    string ManifestPath,
    IReadOnlyList<ArtifactItem> Items);

/// <summary>Wczytuje i waliduje inventory artefaktów kompilatora.</summary>
public interface IArtifactService
{
    /// <summary>Wczytuje inventory bez dopuszczania ścieżek poza output root.</summary>
    /// <param name="manifestPath">Ścieżka do `junexis.artifacts.json`.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    /// <returns>Zweryfikowane inventory.</returns>
    Task<ArtifactInventorySnapshot> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken = default);
}

/// <summary>Informuje Studio o opublikowaniu nowego inventory artefaktów.</summary>
/// <param name="CompilerId">Id providera kompilacji.</param>
/// <param name="BuildId">Id builda.</param>
/// <param name="ArtifactManifestPath">Ścieżka inventory.</param>
public sealed record ArtifactsUpdatedEvent(
    string CompilerId,
    string BuildId,
    string ArtifactManifestPath);
