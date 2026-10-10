using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>A named, workspace-persistent Diagnostic Story baseline.</summary>
/// <param name="Id">Stable bookmark identifier.</param>
/// <param name="Name">User-facing bookmark name.</param>
/// <param name="CreatedAt">Time at which the Story was captured.</param>
/// <param name="Story">Complete evidence-backed Diagnostic Story.</param>
public sealed record NarrativeStoryBookmark(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    DiagnosticStoryResult Story);

/// <summary>A single evidence file verified against an imported bundle manifest.</summary>
/// <param name="Path">Canonical path inside the ZIP bundle.</param>
/// <param name="SizeBytes">Uncompressed evidence size.</param>
/// <param name="Sha256">Lowercase SHA-256 recorded by the manifest.</param>
public sealed record NarrativeDiagnosticEvidenceFile(
    string Path,
    long SizeBytes,
    string Sha256);

/// <summary>Authenticity state established independently from bundle integrity.</summary>
public enum NarrativeDiagnosticAuthenticityStatus
{
    /// <summary>The bundle contains no signature claim.</summary>
    Unsigned,

    /// <summary>The signature is valid, but its signer is not locally trusted.</summary>
    SignatureVerified,

    /// <summary>The signature is valid and its exact public key is locally trusted.</summary>
    TrustedSigner,

    /// <summary>The bundle contains a signature claim that could not be verified.</summary>
    InvalidSignature
}

/// <summary>Result of verifying the optional diagnostic bundle signature.</summary>
/// <param name="Status">Authenticity status independent from integrity.</param>
/// <param name="Algorithm">Claimed signing algorithm.</param>
/// <param name="SignerName">Claimed human-facing signer name.</param>
/// <param name="SignerFingerprint">SHA-256 fingerprint of the embedded public key.</param>
/// <param name="PublicKeySpki">Base64 SubjectPublicKeyInfo used for verification.</param>
/// <param name="Message">Evidence-based verification explanation.</param>
public sealed record NarrativeDiagnosticSignatureVerification(
    NarrativeDiagnosticAuthenticityStatus Status,
    string? Algorithm,
    string? SignerName,
    string? SignerFingerprint,
    string? PublicKeySpki,
    string Message);

/// <summary>A verified diagnostic bundle opened without a live TraceCollector.</summary>
/// <param name="SourcePath">Absolute path of the imported ZIP bundle.</param>
/// <param name="ExportedAt">Time recorded by the exporting Studio instance.</param>
/// <param name="WorkspaceName">Optional source workspace name.</param>
/// <param name="BuildId">Optional compiler build identity.</param>
/// <param name="NarrativeFilter">Optional captured Narrative filter.</param>
/// <param name="EventKindFilter">Optional captured event-kind filter.</param>
/// <param name="Current">Optional current Diagnostic Story.</param>
/// <param name="Pinned">Optional pinned Diagnostic Story.</param>
/// <param name="Comparison">Optional captured comparison.</param>
/// <param name="DebugCorrelation">Optional stopped-frame correlation evidence.</param>
/// <param name="Bookmarks">Verified bookmarks contained in the bundle.</param>
/// <param name="EvidenceFiles">Evidence files verified against the manifest.</param>
/// <param name="BundleId">Stable SHA-256 identity of the exact manifest bytes.</param>
/// <param name="Signature">Optional signature verification and local trust result.</param>
public sealed record ImportedNarrativeDiagnosticSession(
    string SourcePath,
    DateTimeOffset ExportedAt,
    string? WorkspaceName,
    string? BuildId,
    string? NarrativeFilter,
    string? EventKindFilter,
    DiagnosticStoryResult? Current,
    DiagnosticStoryResult? Pinned,
    DiagnosticStoryComparison? Comparison,
    WhyDebugCorrelationResult? DebugCorrelation,
    IReadOnlyList<NarrativeStoryBookmark> Bookmarks,
    IReadOnlyList<NarrativeDiagnosticEvidenceFile> EvidenceFiles,
    string BundleId,
    NarrativeDiagnosticSignatureVerification Signature)
{
    /// <summary>Number of evidence files verified against the manifest.</summary>
    public int VerifiedFileCount => EvidenceFiles.Count;
}

internal sealed record NarrativeDiagnosticSessionDocument(
    string SchemaVersion,
    IReadOnlyList<NarrativeStoryBookmark> Bookmarks)
{
    internal const string CurrentVersion = "1.0";
}

internal sealed record NarrativeDiagnosticBundleMetadata(
    string SchemaVersion,
    DateTimeOffset ExportedAt,
    string? WorkspaceName,
    string? BuildId,
    string? NarrativeFilter,
    string? EventKindFilter,
    ulong? CurrentEventId,
    ulong? PinnedEventId,
    WhyDebugCorrelationResult? DebugCorrelation,
    int BookmarkCount);

internal sealed record NarrativeDiagnosticBundleManifest(
    string SchemaVersion,
    IReadOnlyDictionary<string, string> Sha256,
    bool SignatureRequired = false);

internal sealed record NarrativeDiagnosticBundleSignature(
    string SchemaVersion,
    string Algorithm,
    string SignerName,
    string PublicKeySpki,
    string SignedManifestSha256,
    string Signature);

internal sealed record NarrativeDiagnosticSigningCredentials(
    string SignerName,
    string PrivateKeyPem);

internal static class NarrativeDiagnosticSessionStore
{
    private const long MaximumBundleBytes = 64L * 1024 * 1024;
    private const long MaximumEntryBytes = 16L * 1024 * 1024;
    private const int MaximumEntryCount = 256;

    internal static readonly string RelativePath =
        Path.Combine(".junexis", "narrative-session.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal static IReadOnlyList<NarrativeStoryBookmark> Load(IWorkspace workspace)
    {
        var path = Resolve(workspace);
        if (path is null || !File.Exists(path))
            return [];
        try
        {
            var document = JsonSerializer.Deserialize<NarrativeDiagnosticSessionDocument>(
                File.ReadAllText(path),
                JsonOptions);
            if (document is null
                || !string.Equals(
                    document.SchemaVersion,
                    NarrativeDiagnosticSessionDocument.CurrentVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Unsupported or empty Narrative diagnostic session document.");
            }
            return document.Bookmarks
                .Where(IsValidBookmark)
                .OrderByDescending(bookmark => bookmark.CreatedAt)
                .ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Narrative diagnostic session JSON is invalid.",
                ex);
        }
    }

    internal static void Save(
        IWorkspace workspace,
        IReadOnlyList<NarrativeStoryBookmark> bookmarks)
    {
        var path = Resolve(workspace);
        if (path is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var document = new NarrativeDiagnosticSessionDocument(
            NarrativeDiagnosticSessionDocument.CurrentVersion,
            bookmarks.Where(IsValidBookmark).ToArray());
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(document, JsonOptions),
            Encoding.UTF8);
        File.Move(temporaryPath, path, overwrite: true);
    }

    internal static async Task ExportBundleAsync(
        string path,
        IWorkspace? workspace,
        DiagnosticStoryResult? current,
        DiagnosticStoryResult? pinned,
        DiagnosticStoryComparison? comparison,
        WhyDebugCorrelationResult? debugCorrelation,
        IReadOnlyList<NarrativeStoryBookmark> bookmarks,
        string? narrativeFilter,
        string? eventKindFilter,
        CancellationToken cancellationToken = default,
        NarrativeDiagnosticSigningCredentials? signingCredentials = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        AddJson(entries, "session.json", new NarrativeDiagnosticBundleMetadata(
            "1.0",
            DateTimeOffset.UtcNow,
            WorkspaceName(workspace),
            current?.BuildId ?? pinned?.BuildId,
            Normalize(narrativeFilter),
            Normalize(eventKindFilter),
            current?.TargetEventId,
            pinned?.TargetEventId,
            debugCorrelation,
            bookmarks.Count));
        if (current is not null)
            AddJson(entries, "stories/current.json", current);
        if (pinned is not null)
            AddJson(entries, "stories/pinned.json", pinned);
        if (comparison is not null)
            AddJson(entries, "comparison.json", comparison);
        foreach (var bookmark in bookmarks.OrderBy(item => item.Id, StringComparer.Ordinal))
            AddJson(entries, $"bookmarks/{bookmark.Id}.json", bookmark);
        entries["README.md"] = Encoding.UTF8.GetBytes(
            BuildReadme(
                current,
                pinned,
                bookmarks.Count,
                signed: signingCredentials is not null));

        var checksums = entries.ToDictionary(
            item => item.Key,
            item => Convert.ToHexString(SHA256.HashData(item.Value)).ToLowerInvariant(),
            StringComparer.Ordinal);
        AddJson(
            entries,
            "manifest.json",
            new NarrativeDiagnosticBundleManifest(
                "1.0",
                checksums,
                SignatureRequired: signingCredentials is not null));
        if (signingCredentials is not null)
            AddJson(
                entries,
                "signature.json",
                CreateSignature(entries["manifest.json"], signingCredentials));

        var temporaryPath = fullPath + ".tmp";
        try
        {
            await using (var file = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var item in entries)
                {
                    var entry = archive.CreateEntry(item.Key, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    await using var stream = entry.Open();
                    await stream.WriteAsync(item.Value, cancellationToken).ConfigureAwait(false);
                }
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static async Task<ImportedNarrativeDiagnosticSession> ImportBundleAsync(
        string path,
        CancellationToken cancellationToken = default,
        NarrativeDiagnosticTrustStore? trustStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("Narrative diagnostic bundle was not found.", fullPath);
        if (file.Length > MaximumBundleBytes)
            throw new InvalidDataException("Narrative diagnostic bundle exceeds the 64 MiB safety limit.");

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count is 0 or > MaximumEntryCount)
            throw new InvalidDataException(
                $"Narrative diagnostic bundle must contain between 1 and {MaximumEntryCount} files.");

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEntryPath(entry.FullName);
            if (!IsAllowedEntry(entry.FullName))
                throw new InvalidDataException($"Unexpected diagnostic bundle entry '{entry.FullName}'.");
            if (!entryNames.Add(entry.FullName) || !entries.TryAdd(entry.FullName, []))
                throw new InvalidDataException($"Duplicate diagnostic bundle entry '{entry.FullName}'.");
            if (entry.Length < 0 || entry.Length > MaximumEntryBytes)
                throw new InvalidDataException(
                    $"Diagnostic bundle entry '{entry.FullName}' exceeds the 16 MiB safety limit.");
            totalLength += entry.Length;
            if (totalLength > MaximumBundleBytes)
                throw new InvalidDataException(
                    "Narrative diagnostic bundle exceeds the 64 MiB uncompressed safety limit.");
            await using var entryStream = entry.Open();
            using var buffer = new MemoryStream((int)entry.Length);
            await entryStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (buffer.Length != entry.Length)
                throw new InvalidDataException(
                    $"Diagnostic bundle entry '{entry.FullName}' has an inconsistent length.");
            entries[entry.FullName] = buffer.ToArray();
        }

        var manifest = DeserializeRequired<NarrativeDiagnosticBundleManifest>(
            entries,
            "manifest.json");
        if (!string.Equals(manifest.SchemaVersion, "1.0", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Unsupported diagnostic bundle manifest schema '{manifest.SchemaVersion}'.");
        var evidencePaths = entries.Keys
            .Where(name =>
                !name.Equals("manifest.json", StringComparison.Ordinal)
                && !name.Equals("signature.json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (manifest.Sha256 is null
            || manifest.Sha256.Count != evidencePaths.Length
            || !evidencePaths.SequenceEqual(
                manifest.Sha256.Keys.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Diagnostic bundle manifest does not describe exactly every evidence file.");
        }
        foreach (var evidencePath in evidencePaths)
        {
            if (!manifest.Sha256.TryGetValue(evidencePath, out var expected)
                || !ChecksumMatches(entries[evidencePath], expected))
            {
                throw new InvalidDataException(
                    $"Diagnostic bundle integrity check failed for '{evidencePath}'.");
            }
        }
        var manifestBytes = entries["manifest.json"];
        var bundleId = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
        var signature = VerifySignature(
            entries,
            manifestBytes,
            manifest.SignatureRequired,
            trustStore);

        var metadata = DeserializeRequired<NarrativeDiagnosticBundleMetadata>(
            entries,
            "session.json");
        if (!string.Equals(metadata.SchemaVersion, "1.0", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Unsupported diagnostic session schema '{metadata.SchemaVersion}'.");
        var current = DeserializeOptional<DiagnosticStoryResult>(entries, "stories/current.json");
        var pinned = DeserializeOptional<DiagnosticStoryResult>(entries, "stories/pinned.json");
        var comparison = DeserializeOptional<DiagnosticStoryComparison>(entries, "comparison.json");
        ValidateStory(current, "current");
        ValidateStory(pinned, "pinned");
        if (metadata.CurrentEventId != current?.TargetEventId
            || metadata.PinnedEventId != pinned?.TargetEventId
            || !string.Equals(
                metadata.BuildId,
                current?.BuildId ?? pinned?.BuildId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Diagnostic bundle Story identities do not match session metadata.");
        }
        if (comparison is not null
            && (pinned is null
                || current is null
                || comparison.PinnedEventId != pinned.TargetEventId
                || comparison.CurrentEventId != current.TargetEventId
                || comparison.AddedEvidence is null
                || comparison.RemovedEvidence is null
                || comparison.Summary is null))
        {
            throw new InvalidDataException(
                "Diagnostic bundle comparison does not match its current and pinned Stories.");
        }
        var bookmarkEntries = entries
            .Where(item => item.Key.StartsWith("bookmarks/", StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => (
                Path: item.Key,
                Bookmark: Deserialize<NarrativeStoryBookmark>(item.Value, item.Key)))
            .ToArray();
        if (bookmarkEntries.Any(item =>
                !IsValidImportedBookmark(item.Bookmark)
                || !item.Path.Equals(
                    $"bookmarks/{item.Bookmark.Id}.json",
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Diagnostic bundle contains an invalid bookmark.");
        }
        var bookmarks = bookmarkEntries.Select(item => item.Bookmark).ToArray();
        if (bookmarks.Select(bookmark => bookmark.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != bookmarks.Length)
        {
            throw new InvalidDataException(
                "Diagnostic bundle contains duplicate bookmark identifiers.");
        }
        foreach (var bookmark in bookmarks)
            ValidateStory(bookmark.Story, $"bookmark '{bookmark.Name}'");
        if (metadata.BookmarkCount != bookmarks.Length)
            throw new InvalidDataException(
                "Diagnostic bundle bookmark count does not match session metadata.");
        if (current is null && pinned is null && bookmarks.Length == 0)
            throw new InvalidDataException("Diagnostic bundle contains no Diagnostic Story evidence.");
        if (metadata.DebugCorrelation is not null
            && (string.IsNullOrWhiteSpace(metadata.DebugCorrelation.SchemaVersion)
                || !metadata.DebugCorrelation.SchemaVersion.StartsWith("1.", StringComparison.Ordinal)
                || metadata.DebugCorrelation.Evidence is null))
        {
            throw new InvalidDataException(
                "Diagnostic bundle contains invalid debugger correlation evidence.");
        }
        var verifiedFiles = evidencePaths
            .Select(evidencePath => new NarrativeDiagnosticEvidenceFile(
                evidencePath,
                entries[evidencePath].LongLength,
                manifest.Sha256[evidencePath].ToLowerInvariant()))
            .ToArray();

        return new ImportedNarrativeDiagnosticSession(
            fullPath,
            metadata.ExportedAt,
            metadata.WorkspaceName,
            metadata.BuildId,
            metadata.NarrativeFilter,
            metadata.EventKindFilter,
            current,
            pinned,
            comparison,
            metadata.DebugCorrelation,
            bookmarks,
            verifiedFiles,
            bundleId,
            signature);
    }

    internal static async Task<ImportedNarrativeDiagnosticSession> ImportBundleBytesAsync(
        byte[] content,
        string sourcePath,
        CancellationToken cancellationToken = default,
        NarrativeDiagnosticTrustStore? trustStore = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.LongLength > MaximumBundleBytes)
            throw new InvalidDataException("Narrative diagnostic bundle exceeds the 64 MiB safety limit.");
        var directory = Path.Combine(Path.GetTempPath(), "JunexisStudio");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}.diagnostic.zip");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken)
                .ConfigureAwait(false);
            var imported = await ImportBundleAsync(
                temporaryPath,
                cancellationToken,
                trustStore).ConfigureAwait(false);
            return imported with { SourcePath = Path.GetFullPath(sourcePath) };
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool IsValidBookmark(NarrativeStoryBookmark bookmark) =>
        !string.IsNullOrWhiteSpace(bookmark.Id)
        && !string.IsNullOrWhiteSpace(bookmark.Name)
        && bookmark.Story is not null;

    private static bool IsValidImportedBookmark(NarrativeStoryBookmark bookmark) =>
        IsValidBookmark(bookmark)
        && IsSafeIdentifier(bookmark.Id);

    private static bool IsSafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsAllowedEntry(string path) =>
        path is "manifest.json"
            or "signature.json"
            or "session.json"
            or "comparison.json"
            or "README.md"
            or "stories/current.json"
            or "stories/pinned.json"
        || path.StartsWith("bookmarks/", StringComparison.Ordinal)
            && path.EndsWith(".json", StringComparison.Ordinal)
            && path.Length > "bookmarks/.json".Length;

    private static NarrativeDiagnosticBundleSignature CreateSignature(
        byte[] manifestBytes,
        NarrativeDiagnosticSigningCredentials credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.SignerName)
            || credentials.SignerName.Trim().Length > 200)
        {
            throw new InvalidDataException(
                "Diagnostic bundle signer name must contain between 1 and 200 characters.");
        }
        try
        {
            using var signer = ECDsa.Create();
            signer.ImportFromPem(credentials.PrivateKeyPem);
            if (signer.KeySize != 256)
                throw new CryptographicException("Signing key must use the NIST P-256 curve.");
            var publicKey = signer.ExportSubjectPublicKeyInfo();
            var manifestHash = SHA256.HashData(manifestBytes);
            return new NarrativeDiagnosticBundleSignature(
                "1.0",
                "ECDSA_P256_SHA256",
                credentials.SignerName.Trim(),
                Convert.ToBase64String(publicKey),
                Convert.ToHexString(manifestHash).ToLowerInvariant(),
                Convert.ToBase64String(
                    signer.SignData(
                        manifestBytes,
                        HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException(
                "Diagnostic bundle signing key is not a usable ECDSA private key.",
                ex);
        }
    }

    private static NarrativeDiagnosticSignatureVerification VerifySignature(
        IReadOnlyDictionary<string, byte[]> entries,
        byte[] manifestBytes,
        bool signatureRequired,
        NarrativeDiagnosticTrustStore? trustStore)
    {
        if (!entries.TryGetValue("signature.json", out var signatureBytes))
        {
            return new NarrativeDiagnosticSignatureVerification(
                signatureRequired
                    ? NarrativeDiagnosticAuthenticityStatus.InvalidSignature
                    : NarrativeDiagnosticAuthenticityStatus.Unsigned,
                null,
                null,
                null,
                null,
                signatureRequired
                    ? "Manifest requires a signature, but signature.json is missing."
                    : "Bundle is unsigned; integrity and consistency were verified independently.");
        }
        try
        {
            var signature = Deserialize<NarrativeDiagnosticBundleSignature>(
                signatureBytes,
                "signature.json");
            if (!string.Equals(signature.SchemaVersion, "1.0", StringComparison.Ordinal)
                || !string.Equals(signature.Algorithm, "ECDSA_P256_SHA256", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(signature.SignerName)
                || signature.SignerName.Length > 200)
            {
                return InvalidSignature(signature, "Unsupported or incomplete signature contract.");
            }
            var publicKey = Convert.FromBase64String(signature.PublicKeySpki);
            var fingerprint = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
            var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            if (!manifestHash.Equals(signature.SignedManifestSha256, StringComparison.OrdinalIgnoreCase))
                return InvalidSignature(signature, "Signed manifest identity does not match this bundle.", fingerprint);
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (bytesRead != publicKey.Length
                || verifier.KeySize != 256
                || !verifier.VerifyData(
                    manifestBytes,
                    Convert.FromBase64String(signature.Signature),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return InvalidSignature(signature, "Cryptographic signature verification failed.", fingerprint);
            }
            var trusted = false;
            var trustMessage = string.Empty;
            try
            {
                trusted = trustStore?.IsTrusted(fingerprint, signature.PublicKeySpki) == true;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                trustMessage = $" Local trust store unavailable: {ex.Message}";
            }
            return new NarrativeDiagnosticSignatureVerification(
                trusted
                    ? NarrativeDiagnosticAuthenticityStatus.TrustedSigner
                    : NarrativeDiagnosticAuthenticityStatus.SignatureVerified,
                signature.Algorithm,
                signature.SignerName,
                fingerprint,
                signature.PublicKeySpki,
                trusted
                    ? $"Signature verified; signer '{signature.SignerName}' is locally trusted."
                    : $"Signature verified; signer '{signature.SignerName}' is not locally trusted.{trustMessage}");
        }
        catch (Exception ex) when (
            ex is InvalidDataException
                or JsonException
                or CryptographicException
                or FormatException
                or ArgumentException)
        {
            return new NarrativeDiagnosticSignatureVerification(
                NarrativeDiagnosticAuthenticityStatus.InvalidSignature,
                null,
                null,
                null,
                null,
                $"Signature claim is invalid: {ex.Message}");
        }
    }

    private static NarrativeDiagnosticSignatureVerification InvalidSignature(
        NarrativeDiagnosticBundleSignature signature,
        string message,
        string? fingerprint = null) =>
        new(
            NarrativeDiagnosticAuthenticityStatus.InvalidSignature,
            signature.Algorithm,
            signature.SignerName,
            fingerprint,
            signature.PublicKeySpki,
            message);

    private static void ValidateEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains('\\', StringComparison.Ordinal)
            || path.StartsWith("/", StringComparison.Ordinal)
            || path.EndsWith("/", StringComparison.Ordinal)
            || path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"Unsafe diagnostic bundle entry path '{path}'.");
        }
    }

    private static bool ChecksumMatches(byte[] content, string? expected)
    {
        if (expected is null || expected.Length != 64)
            return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(content),
                Convert.FromHexString(expected));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static T DeserializeRequired<T>(
        IReadOnlyDictionary<string, byte[]> entries,
        string path)
    {
        if (!entries.TryGetValue(path, out var content))
            throw new InvalidDataException($"Diagnostic bundle is missing required entry '{path}'.");
        return Deserialize<T>(content, path);
    }

    private static T? DeserializeOptional<T>(
        IReadOnlyDictionary<string, byte[]> entries,
        string path) where T : class =>
        entries.TryGetValue(path, out var content)
            ? Deserialize<T>(content, path)
            : null;

    private static T Deserialize<T>(byte[] content, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions)
                ?? throw new InvalidDataException(
                    $"Diagnostic bundle entry '{path}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Diagnostic bundle entry '{path}' contains invalid JSON.",
                ex);
        }
    }

    private static void ValidateStory(DiagnosticStoryResult? story, string label)
    {
        if (story is null)
            return;
        if (string.IsNullOrWhiteSpace(story.SchemaVersion)
            || !story.SchemaVersion.StartsWith("1.", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(story.TargetSymbolId)
            || story.Timeline is null
            || story.CausalChain is null
            || story.ConcurrentEvents is null
            || story.Communications is null
            || story.Evidence is null
            || story.MentorFindings is null)
        {
            throw new InvalidDataException(
                $"Diagnostic bundle contains an invalid {label} Story.");
        }
    }

    private static void AddJson<T>(
        IDictionary<string, byte[]> entries,
        string path,
        T value) =>
        entries[path] = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    private static string BuildReadme(
        DiagnosticStoryResult? current,
        DiagnosticStoryResult? pinned,
        int bookmarkCount,
        bool signed) =>
        $"""
         # Junexis Narrative diagnostic bundle

         Schema: `1.0`
         Current event: `{current?.TargetEventId.ToString() ?? "not captured"}`
         Pinned event: `{pinned?.TargetEventId.ToString() ?? "not captured"}`
         Bookmarks: `{bookmarkCount}`
         Authenticity: `{(signed ? "ECDSA P-256 signature required" : "unsigned")}`

         `manifest.json` contains SHA-256 for every evidence file in this archive.
         Diagnostic Stories are immutable captured evidence; opening the bundle does
         not require a running TraceCollector.
         """;

    private static string? Resolve(IWorkspace workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.RootPath))
            return null;
        var root = Path.GetFullPath(workspace.RootPath);
        var path = Path.GetFullPath(Path.Combine(root, RelativePath));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Narrative diagnostic session path escapes the workspace root.");
        return path;
    }

    private static string? WorkspaceName(IWorkspace? workspace) =>
        string.IsNullOrWhiteSpace(workspace?.RootPath)
            ? null
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(workspace.RootPath));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
