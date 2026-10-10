using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace JunexisLang.Studio.CompilerExtension;

/// <summary>
/// Parser JSON output CLI junexis zgodny ze schema v1.0.
/// Obsługuje envelope v1.0 oraz legacy plain-array fallback.
/// </summary>
public static class JunexisCliOutputParser
{
    /// <summary>
    /// Parsuje stdout CLI jako envelope v1.0 lub legacy array.
    /// </summary>
    /// <param name="stdout">Surowy output stdout procesu CLI.</param>
    /// <returns>
    /// Krotka (<c>Success</c>, lista <see cref="Diagnostic"/>) odpowiadająca
    /// wyniku parsowania lub błędowi.
    /// </returns>
    public static (bool Success, IReadOnlyList<Diagnostic> Diagnostics) Parse(string stdout)
    {
        var result = ParseBuildResult(stdout);
        return (result.Success, result.Diagnostics);
    }

    /// <summary>
    /// Parsuje pełny wynik builda, w tym opcjonalne pola Tooling Protocol 2.0.
    /// </summary>
    /// <param name="stdout">Surowy output stdout procesu CLI.</param>
    /// <returns>Wynik builda oraz ścieżki kontraktów artefaktów.</returns>
    public static JunexisCliBuildResult ParseBuildResult(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return new JunexisCliBuildResult(true, []);

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            // ── Schema v1.0 envelope ──────────────────────────────────────
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("schemaVersion", out _)
                && root.TryGetProperty("diagnostics", out var diagArr))
            {
                var success = root.TryGetProperty("success", out var successEl)
                    && successEl.GetBoolean();

                var diagnostics = ParseDiagnosticsArray(diagArr);
                return new JunexisCliBuildResult(
                    success,
                    diagnostics,
                    GetString(root, "buildId"),
                    GetString(root, "projectRoot"),
                    GetString(root, "outputRoot"),
                    GetStringArray(root, "targets"),
                    GetString(root, "bundleManifest"),
                    GetString(root, "artifactManifest"),
                    GetString(root, "debugManifest"),
                    GetString(root, "analysisSnapshot"),
                    GetString(root, "latestBuildReceipt"));
            }

            // ── Legacy: plain array fallback ──────────────────────────────
            if (root.ValueKind == JsonValueKind.Array)
            {
                var diagnostics = ParseDiagnosticsArray(root);
                var success = !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
                return new JunexisCliBuildResult(success, diagnostics);
            }

            return new JunexisCliBuildResult(true, []);
        }
        catch (JsonException)
        {
            return new JunexisCliBuildResult(false, [new Diagnostic(
                DiagnosticSeverity.Error,
                "Compiler output is not valid JSON — check CLI installation.",
                source: "junexis-lang")]);
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToArray()
            : [];

    private static IReadOnlyList<Diagnostic> ParseDiagnosticsArray(JsonElement array)
    {
        var result = new List<Diagnostic>();

        foreach (var item in array.EnumerateArray())
        {
            var level   = item.TryGetProperty("level",   out var lvl) ? lvl.GetString()    : "info";
            var message = item.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
            var code = item.TryGetProperty("code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.String
                ? codeElement.GetString()
                : null;
            var file    = item.TryGetProperty("file", out var f) && f.ValueKind != JsonValueKind.Null
                ? f.GetString() : null;
            var line    = item.TryGetProperty("line",   out var l) ? l.GetInt32() : 0;
            var column  = item.TryGetProperty("column", out var c) ? c.GetInt32() : 0;

            var severity = level switch
            {
                "error"   => DiagnosticSeverity.Error,
                "warning" => DiagnosticSeverity.Warning,
                _         => DiagnosticSeverity.Info,
            };

            result.Add(new Diagnostic(
                severity,
                message,
                filePath: file,
                line:     line   > 0 ? line   : null,
                column:   column > 0 ? column : null,
                source:   "junexis-lang",
                code:     code));
        }

        return result;
    }
}

/// <summary>Pełny wynik odpowiedzi Junexis CLI.</summary>
/// <param name="Success">Czy build zakończył się powodzeniem.</param>
/// <param name="Diagnostics">Diagnostyki builda.</param>
/// <param name="BuildId">Identyfikator zestawu artefaktów.</param>
/// <param name="ProjectRoot">Katalog projektu.</param>
/// <param name="OutputRoot">Katalog wynikowy.</param>
/// <param name="Targets">Targety builda.</param>
/// <param name="BundleManifestPath">Manifest analysis bundle.</param>
/// <param name="ArtifactManifestPath">Inventory artefaktów.</param>
/// <param name="DebugManifestPath">Manifest debuggera.</param>
/// <param name="AnalysisSnapshotPath">Snapshot analizy dla Studio i Mentora.</param>
/// <param name="LatestBuildReceiptPath">Atomowo opublikowany receipt ostatniego builda.</param>
public sealed record JunexisCliBuildResult(
    bool Success,
    IReadOnlyList<Diagnostic> Diagnostics,
    string? BuildId = null,
    string? ProjectRoot = null,
    string? OutputRoot = null,
    IReadOnlyList<string>? Targets = null,
    string? BundleManifestPath = null,
    string? ArtifactManifestPath = null,
    string? DebugManifestPath = null,
    string? AnalysisSnapshotPath = null,
    string? LatestBuildReceiptPath = null);
