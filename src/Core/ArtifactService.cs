using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>Bezpieczny czytnik inventory artefaktów Junexis.</summary>
public sealed class ArtifactService : IArtifactService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public async Task<ArtifactInventorySnapshot> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var outputRoot = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidDataException("Artifact manifest has no parent directory.");
        await using var stream = File.OpenRead(fullManifestPath);
        var dto = await JsonSerializer.DeserializeAsync<InventoryDto>(
            stream,
            JsonOptions,
            cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Artifact manifest is empty.");

        if (!dto.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported artifact schema '{dto.SchemaVersion}'.");
        if (string.IsNullOrWhiteSpace(dto.BuildId))
            throw new InvalidDataException("Artifact manifest has no buildId.");

        var rootPrefix = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var items = new List<ArtifactItem>();
        foreach (var artifact in dto.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Path) || Path.IsPathRooted(artifact.Path))
                throw new InvalidDataException($"Unsafe artifact path '{artifact.Path}'.");
            var fullPath = Path.GetFullPath(Path.Combine(
                outputRoot,
                artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Artifact escapes output root: '{artifact.Path}'.");
            items.Add(new ArtifactItem(
                artifact.Path,
                fullPath,
                artifact.Target,
                artifact.Kind,
                artifact.Ownership,
                artifact.Sha256));
        }

        return new ArtifactInventorySnapshot(dto.BuildId, fullManifestPath, items);
    }

    private sealed record InventoryDto
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public string BuildId { get; init; } = string.Empty;
        public List<ArtifactDto> Artifacts { get; init; } = [];
    }

    private sealed record ArtifactDto
    {
        public string Path { get; init; } = string.Empty;
        public string Target { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string Ownership { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
    }
}
