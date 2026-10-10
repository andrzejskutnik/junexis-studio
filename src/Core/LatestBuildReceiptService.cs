using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>Bezpieczny czytnik kontraktu latest-build 1.0.</summary>
public sealed class LatestBuildReceiptService : ILatestBuildReceiptService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public async Task<LatestBuildReceipt> LoadAsync(
        string receiptPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
        var fullReceiptPath = Path.GetFullPath(receiptPath);
        await using var stream = File.OpenRead(fullReceiptPath);
        var dto = await JsonSerializer.DeserializeAsync<ReceiptDto>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Latest-build receipt is empty.");

        if (!string.Equals(dto.SchemaVersion, "1.0", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported latest-build schema '{dto.SchemaVersion}'.");
        if (string.IsNullOrWhiteSpace(dto.BuildId)
            || dto.BuildId.Length != 64
            || dto.BuildId.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Latest-build buildId must be a 64-character SHA-256.");
        if (dto.Mode is not ("project" or "file"))
            throw new InvalidDataException("Latest-build mode must be 'project' or 'file'.");
        if (dto.Targets is null
            || dto.Targets.Count == 0
            || dto.Targets.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Latest-build receipt has no valid targets.");

        var projectRoot = RequireAbsolute(dto.ProjectRoot, "projectRoot");
        var entryFile = RequireChild(projectRoot, dto.EntryFile, "entryFile");
        var outputRoot = RequireAbsolute(dto.OutputRoot, "outputRoot");
        var bundleManifest = RequireExistingChild(outputRoot, dto.BundleManifest, "bundleManifest");
        var artifactManifest = RequireExistingChild(outputRoot, dto.ArtifactManifest, "artifactManifest");
        var debugManifest = RequireExistingChild(outputRoot, dto.DebugManifest, "debugManifest");
        var analysisSnapshot = RequireExistingChild(outputRoot, dto.AnalysisSnapshot, "analysisSnapshot");
        await RequireBuildIdentityAsync(
            bundleManifest,
            dto.BuildId,
            "bundleManifest",
            cancellationToken).ConfigureAwait(false);
        await RequireBuildIdentityAsync(
            artifactManifest,
            dto.BuildId,
            "artifactManifest",
            cancellationToken).ConfigureAwait(false);
        await RequireBuildIdentityAsync(
            analysisSnapshot,
            dto.BuildId,
            "analysisSnapshot",
            cancellationToken).ConfigureAwait(false);

        var expectedReceiptPath = dto.Mode == "project"
            ? Path.Combine(projectRoot, ".junexis", "state", "latest-build.json")
            : Path.Combine(
                projectRoot,
                ".junexis",
                "state",
                Path.GetFileNameWithoutExtension(entryFile),
                "latest-build.json");
        if (!PathsEqual(fullReceiptPath, expectedReceiptPath))
            throw new InvalidDataException("Latest-build receipt is outside its deterministic state location.");

        return new LatestBuildReceipt(
            fullReceiptPath,
            dto.BuildId,
            dto.Mode,
            projectRoot,
            entryFile,
            outputRoot,
            dto.Targets,
            bundleManifest,
            artifactManifest,
            debugManifest,
            analysisSnapshot);
    }

    /// <inheritdoc />
    public async Task<LatestBuildReceipt?> TryLoadForContextAsync(
        string workspaceRoot,
        string? activeDocumentPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root))
            return null;

        var projectRoot = FindManifestRoot(activeDocumentPath, root);
        string? receiptPath;
        if (projectRoot is not null)
        {
            receiptPath = Path.Combine(projectRoot, ".junexis", "state", "latest-build.json");
        }
        else if (!string.IsNullOrWhiteSpace(activeDocumentPath)
                 && Path.GetExtension(activeDocumentPath).Equals(".jnx", StringComparison.OrdinalIgnoreCase))
        {
            var entry = Path.GetFullPath(activeDocumentPath);
            receiptPath = Path.Combine(
                Path.GetDirectoryName(entry)!,
                ".junexis",
                "state",
                Path.GetFileNameWithoutExtension(entry),
                "latest-build.json");
        }
        else
        {
            receiptPath = null;
        }

        return receiptPath is not null && File.Exists(receiptPath)
            ? await LoadAsync(receiptPath, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private static string? FindManifestRoot(string? activeDocumentPath, string workspaceRoot)
    {
        var directory = !string.IsNullOrWhiteSpace(activeDocumentPath)
            ? new DirectoryInfo(Directory.Exists(activeDocumentPath)
                ? Path.GetFullPath(activeDocumentPath)
                : Path.GetDirectoryName(Path.GetFullPath(activeDocumentPath))!)
            : new DirectoryInfo(workspaceRoot);
        var boundary = Path.GetFullPath(workspaceRoot);
        while (directory is not null && IsWithinOrEqual(directory.FullName, boundary))
        {
            if (File.Exists(Path.Combine(directory.FullName, "junexis.project.toml")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return File.Exists(Path.Combine(boundary, "junexis.project.toml")) ? boundary : null;
    }

    private static string RequireAbsolute(string path, string property)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException($"Latest-build {property} must be an absolute path.");
        return Path.GetFullPath(path);
    }

    private static string RequireChild(string root, string path, string property)
    {
        var fullPath = RequireAbsolute(path, property);
        if (!IsStrictChild(fullPath, root))
            throw new InvalidDataException($"Latest-build {property} escapes its owning root.");
        return fullPath;
    }

    private static string RequireExistingChild(string root, string path, string property)
    {
        var fullPath = RequireChild(root, path, property);
        if (!File.Exists(fullPath))
            throw new InvalidDataException($"Latest-build {property} does not exist: '{fullPath}'.");
        return fullPath;
    }

    private static bool IsStrictChild(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative is not "." and not ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relative);
    }

    private static bool IsWithinOrEqual(string path, string root) =>
        PathsEqual(path, root) || IsStrictChild(path, root);

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

    private static async Task RequireBuildIdentityAsync(
        string path,
        string expectedBuildId,
        string property,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("buildId", out var buildId)
            || buildId.ValueKind != JsonValueKind.String
            || !string.Equals(buildId.GetString(), expectedBuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Latest-build {property} buildId does not match the receipt.");
        }
    }

    private sealed record ReceiptDto
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public string BuildId { get; init; } = string.Empty;
        public string Mode { get; init; } = string.Empty;
        public string ProjectRoot { get; init; } = string.Empty;
        public string EntryFile { get; init; } = string.Empty;
        public string OutputRoot { get; init; } = string.Empty;
        public List<string> Targets { get; init; } = [];
        public string BundleManifest { get; init; } = string.Empty;
        public string ArtifactManifest { get; init; } = string.Empty;
        public string DebugManifest { get; init; } = string.Empty;
        public string AnalysisSnapshot { get; init; } = string.Empty;
    }
}
