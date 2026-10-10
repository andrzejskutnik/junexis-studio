using Junexis.Studio.Abstractions;

namespace JunexisLang.Studio.CompilerExtension;

/// <summary>
/// Konfiguracja serwisu kompilatora JunexisLang dla Studio.
/// </summary>
public sealed class JunexisCompilerOptions
{
    /// <summary>
    /// Ścieżka do CLI `junexis` lub jego DLL. Jeśli <see langword="null"/>,
    /// używa publicznego polecenia z profilu użytkownika albo PATH.
    /// </summary>
    public string? CliPath { get; set; }

    /// <summary>Resolver publicznych narzędzi przekazany przez Host Studio.</summary>
    public IPlatformToolResolver? ToolResolver { get; set; }

    /// <summary>
    /// Ścieżka do publicznego CLI <c>junexis</c>. Jeśli <see langword="null"/>,
    /// używa <c>JUNEXIS_CLI</c> albo polecenia <c>junexis</c> z PATH.
    /// </summary>
    public string? PublicCliPath { get; set; }

    /// <summary>
    /// Opcjonalny katalog lokalnego zestawu wydania przekazywany do
    /// <c>junexis restore --local-packages</c>.
    /// </summary>
    public string? LocalPackagesPath { get; set; }

    /// <summary>
    /// Opcjonalny adres bazowy Registry przekazywany wyłącznie do publicznego
    /// <c>junexis restore --registry</c>.
    /// </summary>
    public string? RegistryBaseAddress { get; set; }

    /// <summary>
    /// Timeout operacji kompilacji w milisekundach. Domyślnie: 30 sekund.
    /// </summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// Opcjonalny endpoint HTTP observability ingress, np. `http://127.0.0.1:5088/ingress/events`.
    /// </summary>
    public string? ObservabilityIngressUrl { get; set; }

    /// <summary>
    /// PlatformIO executable used by the explicit embedded probe/select/flash flow.
    /// </summary>
    public string? PlatformIoExecutable { get; set; }

    /// <summary>
    /// Rezolwuje efektywną ścieżkę do CLI.
    /// </summary>
    public string ResolvedCliPath =>
        CliPath
        ?? ToolResolver?.ResolveCommand("junexis", "JUNEXIS_CLI")
        ?? Environment.GetEnvironmentVariable("JUNEXIS_CLI")
        ?? "junexis";

    /// <summary>Rezolwuje publiczne wejście CLI używane przez komendy właściciela Installera.</summary>
    public string ResolvedPublicCliPath =>
        PublicCliPath
        ?? ToolResolver?.ResolveCommand("junexis", "JUNEXIS_CLI")
        ?? Environment.GetEnvironmentVariable("JUNEXIS_CLI")
        ?? "junexis";

    /// <summary>Rezolwuje opcjonalny lokalny zestaw wydania dla odtwarzania providerów.</summary>
    public string? ResolvedLocalPackagesPath =>
        LocalPackagesPath
        ?? Environment.GetEnvironmentVariable("JUNEXIS_LOCAL_PACKAGES");

    /// <summary>Rezolwuje opcjonalny adres Registry dla publicznego polecenia odtwarzania.</summary>
    public string? ResolvedRegistryBaseAddress =>
        RegistryBaseAddress
        ?? Environment.GetEnvironmentVariable("JUNEXIS_REGISTRY");

    /// <summary>
    /// Rezolwuje efektywny endpoint HTTP observability ingress.
    /// </summary>
    public string? ResolvedObservabilityIngressUrl =>
        ObservabilityIngressUrl
        ?? Environment.GetEnvironmentVariable("JUNEXIS_OBSERVABILITY_INGRESS_URL");

    /// <summary>Resolves the PlatformIO executable used for embedded deployment.</summary>
    public string ResolvedPlatformIoExecutable =>
        PlatformIoExecutable
        ?? Environment.GetEnvironmentVariable("JUNEXIS_PLATFORMIO_EXE")
        ?? "pio";

}
