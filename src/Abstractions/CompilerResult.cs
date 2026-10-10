namespace Junexis.Studio.Abstractions;

/// <summary>
/// Wynik kompilacji (Build/Clean).
/// </summary>
/// <param name="Success">Czy operacja zakończyła się sukcesem.</param>
/// <param name="Diagnostics">Lista diagnostyk (błędów/ostrzeżeń) zwróconych przez kompilator.</param>
/// <param name="CompilerId">Identyfikator kompilatora, który wyprodukował wynik.</param>
/// <param name="BuildId">Identyfikator spójnego zestawu artefaktów.</param>
/// <param name="ProjectRoot">Katalog projektu rozpoznany przez provider.</param>
/// <param name="OutputRoot">Katalog wynikowy builda.</param>
/// <param name="Targets">Targety uwzględnione w buildzie.</param>
/// <param name="BundleManifestPath">Ścieżka do manifestu analysis bundle.</param>
/// <param name="ArtifactManifestPath">Ścieżka do inventory artefaktów.</param>
/// <param name="DebugManifestPath">Ścieżka do manifestu debuggera.</param>
/// <param name="AnalysisSnapshotPath">Ścieżka do stabilnego snapshotu analizy.</param>
/// <param name="LatestBuildReceiptPath">Ścieżka do atomowo opublikowanego receiptu latest-build.</param>
public sealed record CompilerResult(
    bool Success,
    IReadOnlyList<Diagnostic> Diagnostics,
    string CompilerId,
    string? BuildId = null,
    string? ProjectRoot = null,
    string? OutputRoot = null,
    IReadOnlyList<string>? Targets = null,
    string? BundleManifestPath = null,
    string? ArtifactManifestPath = null,
    string? DebugManifestPath = null,
    string? AnalysisSnapshotPath = null,
    string? LatestBuildReceiptPath = null);
