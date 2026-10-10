namespace Junexis.Studio.Abstractions;

/// <summary>Zweryfikowany kontrakt ostatniego ukończonego builda Junexis.</summary>
/// <param name="ReceiptPath">Bezwzględna ścieżka wczytanego receiptu.</param>
/// <param name="BuildId">Identyfikator spójnego zestawu artefaktów.</param>
/// <param name="Mode">Tryb builda: project albo file.</param>
/// <param name="ProjectRoot">Katalog projektu lub katalog wejściowego pliku.</param>
/// <param name="EntryFile">Główny plik źródłowy Junexis.</param>
/// <param name="OutputRoot">Katalog wynikowy builda.</param>
/// <param name="Targets">Targety objęte buildem.</param>
/// <param name="BundleManifestPath">Ścieżka manifestu analysis bundle.</param>
/// <param name="ArtifactManifestPath">Ścieżka inventory artefaktów.</param>
/// <param name="DebugManifestPath">Ścieżka manifestu debuggera.</param>
/// <param name="AnalysisSnapshotPath">Ścieżka snapshotu analizy.</param>
public sealed record LatestBuildReceipt(
    string ReceiptPath,
    string BuildId,
    string Mode,
    string ProjectRoot,
    string EntryFile,
    string OutputRoot,
    IReadOnlyList<string> Targets,
    string BundleManifestPath,
    string ArtifactManifestPath,
    string DebugManifestPath,
    string AnalysisSnapshotPath);

/// <summary>Odczytuje i waliduje trwały kontrakt latest-build 1.0.</summary>
public interface ILatestBuildReceiptService
{
    /// <summary>Wczytuje receipt ze wskazanej ścieżki i sprawdza jego granice bezpieczeństwa.</summary>
    /// <param name="receiptPath">Ścieżka pliku latest-build.json.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    /// <returns>Zweryfikowany receipt.</returns>
    Task<LatestBuildReceipt> LoadAsync(
        string receiptPath,
        CancellationToken cancellationToken = default);

    /// <summary>Odnajduje i wczytuje receipt dla otwartego kontekstu bez skanowania outputu.</summary>
    /// <param name="workspaceRoot">Root otwartego workspace.</param>
    /// <param name="activeDocumentPath">Opcjonalna ścieżka aktywnego dokumentu.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    /// <returns>Receipt albo <see langword="null"/>, gdy deterministyczna ścieżka nie istnieje.</returns>
    Task<LatestBuildReceipt?> TryLoadForContextAsync(
        string workspaceRoot,
        string? activeDocumentPath,
        CancellationToken cancellationToken = default);
}

/// <summary>Informuje Studio, że zweryfikowano nowy lub odtworzony latest-build receipt.</summary>
/// <param name="CompilerId">Id providera kompilacji.</param>
/// <param name="Receipt">Zweryfikowany kontrakt ostatniego builda.</param>
/// <param name="Restored">Czy stan został odtworzony po otwarciu workspace.</param>
public sealed record LatestBuildUpdatedEvent(
    string CompilerId,
    LatestBuildReceipt Receipt,
    bool Restored);
