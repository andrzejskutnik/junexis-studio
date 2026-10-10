using Junexis.Studio.Abstractions;
using System.Text.Json;

namespace Junexis.Studio.Core;

/// <summary>
/// Parseuje i waliduje manifest rozszerzenia (extension.json).
/// Zwraca <see cref="ExtensionManifest"/> lub listę błędów – nigdy nie rzuca.
/// </summary>
public static class ExtensionManifestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Wynik parsowania manifestu.
    /// </summary>
    /// <param name="Manifest">Poprawny manifest (lub <see langword="null"/> przy błędzie).</param>
    /// <param name="Errors">Lista błędów walidacji (pusta przy sukcesie).</param>
    public sealed record ParseResult(ExtensionManifest? Manifest, IReadOnlyList<string> Errors);

    /// <summary>
    /// Parsuje JSON manifestu i waliduje wymagane pola.
    /// </summary>
    /// <param name="json">Tekst JSON manifestu.</param>
    /// <param name="currentIdeVersion">Bieżąca wersja IDE do porównania z MinimalIdeVersion (opcjonalna).</param>
    /// <returns>Wynik parsowania z manifestem lub błędami.</returns>
    public static ParseResult Parse(string json, string? currentIdeVersion = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParseResult(null, new[] { "Manifest JSON is empty." });
        }

        ManifestDto? dto;

        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new ParseResult(null, new[] { $"Invalid JSON: {ex.Message}" });
        }

        if (dto is null)
        {
            return new ParseResult(null, new[] { "Manifest deserialized to null." });
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            errors.Add("Missing required field: Id.");
        }

        if (string.IsNullOrWhiteSpace(dto.Version))
        {
            errors.Add("Missing required field: Version.");
        }

        if (string.IsNullOrWhiteSpace(dto.EntryType))
        {
            errors.Add("Missing required field: EntryType.");
        }

        if (errors.Count > 0)
        {
            return new ParseResult(null, errors);
        }

        // Check MinimalIdeVersion compatibility.
        if (!string.IsNullOrWhiteSpace(dto.MinimalIdeVersion) && !string.IsNullOrWhiteSpace(currentIdeVersion))
        {
            if (Version.TryParse(NormalizeSemVer(dto.MinimalIdeVersion), out var minVersion) &&
                Version.TryParse(NormalizeSemVer(currentIdeVersion), out var ideVersion))
            {
                if (ideVersion < minVersion)
                {
                    errors.Add($"IDE version {currentIdeVersion} is below required {dto.MinimalIdeVersion}.");
                    return new ParseResult(null, errors);
                }
            }
        }

        var manifest = new ExtensionManifest(
            dto.Id!,
            dto.Version!,
            dto.EntryType!,
            dto.MinimalIdeVersion,
            dto.ExposedCapabilities?.AsReadOnly(),
            dto.Dependencies?.AsReadOnly(),
            dto.Permissions?.AsReadOnly());

        return new ParseResult(manifest, Array.Empty<string>());
    }

    /// <summary>
    /// Normalizuje SemVer na format akceptowalny przez <see cref="Version.TryParse"/>.
    /// Usuwa suffix pre-release (np. "-beta") i dopełnia do co najmniej Major.Minor.
    /// </summary>
    private static string NormalizeSemVer(string semver)
    {
        var dashIndex = semver.IndexOf('-');
        var core = dashIndex >= 0 ? semver[..dashIndex] : semver;

        // Version.TryParse needs at least Major.Minor
        if (!core.Contains('.'))
        {
            core += ".0";
        }

        return core;
    }

    private sealed class ManifestDto
    {
        public string? Id { get; set; }
        public string? Version { get; set; }
        public string? EntryType { get; set; }
        public string? MinimalIdeVersion { get; set; }
        public List<string>? ExposedCapabilities { get; set; }
        public List<string>? Dependencies { get; set; }
        public List<string>? Permissions { get; set; }
    }
}
