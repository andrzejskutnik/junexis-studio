using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Junexis.Studio.Host;

/// <summary>Semantic role of a human or Mentor review statement.</summary>
public enum NarrativeDiagnosticReviewKind
{
    /// <summary>A statement directly grounded in captured evidence.</summary>
    Observation,

    /// <summary>A proposed explanation that remains distinct from evidence.</summary>
    Hypothesis,

    /// <summary>An explicit reviewer conclusion.</summary>
    Decision,

    /// <summary>A follow-up task derived from the review.</summary>
    Action
}

/// <summary>Lifecycle state of a diagnostic case review.</summary>
public enum NarrativeDiagnosticReviewStatus
{
    /// <summary>The case has been created but not investigated.</summary>
    Open,

    /// <summary>The evidence is actively being investigated.</summary>
    Investigating,

    /// <summary>The reviewer accepted a resolution.</summary>
    Resolved,

    /// <summary>The case or proposed explanation was rejected.</summary>
    Rejected
}

/// <summary>Declares who originated a review statement.</summary>
public enum NarrativeDiagnosticReviewSource
{
    /// <summary>A human reviewer authored the statement directly.</summary>
    Human,

    /// <summary>The user explicitly accepted a deterministic Mentor proposal.</summary>
    Mentor
}

/// <summary>A review statement linked to immutable diagnostic evidence.</summary>
/// <param name="Id">Stable review-entry identifier.</param>
/// <param name="Kind">Statement semantics.</param>
/// <param name="Author">Declared reviewer identity.</param>
/// <param name="CreatedAt">Creation timestamp.</param>
/// <param name="Text">Reviewer-authored content.</param>
/// <param name="EventId">Optional referenced Narrative event.</param>
/// <param name="SymbolId">Optional referenced stable Junexis symbol.</param>
/// <param name="EvidencePath">Optional referenced verified bundle entry.</param>
public sealed record NarrativeDiagnosticReviewEntry(
    string Id,
    NarrativeDiagnosticReviewKind Kind,
    string Author,
    DateTimeOffset CreatedAt,
    string Text,
    ulong? EventId,
    string? SymbolId,
    string? EvidencePath)
{
    /// <summary>Origin derived without changing the signed review JSON contract.</summary>
    [JsonIgnore]
    public NarrativeDiagnosticReviewSource Source =>
        Author.Equals("Junexis Mentor", StringComparison.Ordinal)
        && Id.StartsWith("mentor:", StringComparison.Ordinal)
            ? NarrativeDiagnosticReviewSource.Mentor
            : NarrativeDiagnosticReviewSource.Human;

    /// <summary>Stable Mentor rule/build reference encoded as the entry identifier.</summary>
    [JsonIgnore]
    public string? SourceReference =>
        Source == NarrativeDiagnosticReviewSource.Mentor ? Id : null;
}

/// <summary>A non-binding Mentor proposal derived from evidence already captured in a Diagnostic Story.</summary>
/// <param name="Id">Stable proposal identifier.</param>
/// <param name="Kind">Proposed semantic review role; Mentor never proposes a Decision.</param>
/// <param name="FindingCode">Stable Mentor rule code.</param>
/// <param name="Severity">Finding severity.</param>
/// <param name="Text">Proposed review statement.</param>
/// <param name="RelatedSymbolId">Stable symbol grounding the proposal.</param>
/// <param name="EventId">Diagnostic event presented when the proposal was derived.</param>
/// <param name="EvidencePath">Verified bundle entry used as the portable evidence reference.</param>
/// <param name="BuildId">Exact compiler/runtime build identity.</param>
/// <param name="SourceReference">Stable finding/build reference copied to an accepted statement.</param>
public sealed record NarrativeDiagnosticMentorProposal(
    string Id,
    NarrativeDiagnosticReviewKind Kind,
    string FindingCode,
    string Severity,
    string Text,
    string RelatedSymbolId,
    ulong EventId,
    string EvidencePath,
    string BuildId,
    string SourceReference);

/// <summary>An immutable saved revision of a diagnostic review.</summary>
/// <param name="Number">Monotonic one-based revision number.</param>
/// <param name="SavedAt">Revision timestamp.</param>
/// <param name="SavedBy">Declared reviewer who saved the revision.</param>
/// <param name="Status">Case status at this revision.</param>
/// <param name="Entries">Complete review-entry snapshot.</param>
public sealed record NarrativeDiagnosticReviewRevision(
    int Number,
    DateTimeOffset SavedAt,
    string SavedBy,
    NarrativeDiagnosticReviewStatus Status,
    IReadOnlyList<NarrativeDiagnosticReviewEntry> Entries);

/// <summary>Optional detached signature embedded in a review document.</summary>
/// <param name="SchemaVersion">Signature contract version.</param>
/// <param name="Algorithm">Signing algorithm identifier.</param>
/// <param name="SignerName">Declared signer identity.</param>
/// <param name="PublicKeySpki">Base64 public SubjectPublicKeyInfo.</param>
/// <param name="SignedPayloadSha256">SHA-256 of the canonical unsigned review payload.</param>
/// <param name="Signature">Base64 IEEE P1363 signature.</param>
public sealed record NarrativeDiagnosticReviewSignature(
    string SchemaVersion,
    string Algorithm,
    string SignerName,
    string PublicKeySpki,
    string SignedPayloadSha256,
    string Signature);

/// <summary>Versioned review sidecar bound to one immutable diagnostic Bundle ID.</summary>
/// <param name="SchemaVersion">Review schema version.</param>
/// <param name="BundleId">Exact diagnostic Bundle ID under review.</param>
/// <param name="Status">Current case status.</param>
/// <param name="Entries">Current working review entries.</param>
/// <param name="Revisions">Saved immutable revision history.</param>
/// <param name="Signature">Optional signature of the canonical current document.</param>
public sealed record NarrativeDiagnosticReviewDocument(
    string SchemaVersion,
    string BundleId,
    NarrativeDiagnosticReviewStatus Status,
    IReadOnlyList<NarrativeDiagnosticReviewEntry> Entries,
    IReadOnlyList<NarrativeDiagnosticReviewRevision> Revisions,
    NarrativeDiagnosticReviewSignature? Signature);

/// <summary>Directional difference between the two latest saved review revisions.</summary>
/// <param name="PreviousRevision">Earlier revision number.</param>
/// <param name="CurrentRevision">Later revision number.</param>
/// <param name="Summary">Compact revision comparison.</param>
/// <param name="AddedEntries">Entries added in the current revision.</param>
/// <param name="RemovedEntries">Entries removed from the current revision.</param>
/// <param name="StatusChanged">Whether case status changed.</param>
public sealed record NarrativeDiagnosticReviewComparison(
    int PreviousRevision,
    int CurrentRevision,
    string Summary,
    IReadOnlyList<NarrativeDiagnosticReviewEntry> AddedEntries,
    IReadOnlyList<NarrativeDiagnosticReviewEntry> RemovedEntries,
    bool StatusChanged);

internal sealed record NarrativeDiagnosticReviewLoadResult(
    NarrativeDiagnosticReviewDocument Document,
    NarrativeDiagnosticSignatureVerification Signature);

internal sealed record NarrativeDiagnosticCaseMetadata(
    string SchemaVersion,
    string BundleId,
    int ReviewRevision);

internal sealed record NarrativeDiagnosticCaseManifest(
    string SchemaVersion,
    IReadOnlyDictionary<string, string> Sha256);

internal sealed record NarrativeDiagnosticCasePayload(
    byte[] Bundle,
    NarrativeDiagnosticReviewDocument Review);

internal static class NarrativeDiagnosticReviewStore
{
    private const string SchemaVersion = "1.0";
    private const long MaximumReviewBytes = 4L * 1024 * 1024;
    private const long MaximumCaseBytes = 80L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal static string SidecarPath(string bundlePath) =>
        Path.GetFullPath(bundlePath) + ".review.json";

    internal static NarrativeDiagnosticReviewDocument Create(string bundleId) =>
        new(SchemaVersion, bundleId, NarrativeDiagnosticReviewStatus.Open, [], [], null);

    internal static NarrativeDiagnosticReviewLoadResult Load(
        string path,
        string bundleId,
        NarrativeDiagnosticTrustStore trustStore)
    {
        if (!File.Exists(path))
        {
            return new NarrativeDiagnosticReviewLoadResult(
                Create(bundleId),
                Unsigned("Review has not been signed."));
        }
        var file = new FileInfo(path);
        if (file.Length > MaximumReviewBytes)
            throw new InvalidDataException("Diagnostic review exceeds the 4 MiB safety limit.");
        var document = DeserializeReview(File.ReadAllBytes(path));
        Validate(document, bundleId);
        return new NarrativeDiagnosticReviewLoadResult(
            document,
            Verify(document, trustStore));
    }

    internal static void Save(
        string path,
        NarrativeDiagnosticReviewDocument document)
    {
        Validate(document, document.BundleId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            var content = Serialize(document);
            if (content.LongLength > MaximumReviewBytes)
                throw new InvalidDataException("Diagnostic review exceeds the 4 MiB safety limit.");
            File.WriteAllBytes(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static NarrativeDiagnosticReviewDocument Sign(
        NarrativeDiagnosticReviewDocument document,
        NarrativeDiagnosticSigningCredentials credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.SignerName)
            || credentials.SignerName.Trim().Length > 200)
        {
            throw new InvalidDataException(
                "Diagnostic review signer name must contain between 1 and 200 characters.");
        }
        var unsigned = document with { Signature = null };
        var payload = Serialize(unsigned);
        try
        {
            using var signer = ECDsa.Create();
            signer.ImportFromPem(credentials.PrivateKeyPem);
            if (signer.KeySize != 256)
                throw new CryptographicException("Signing key must use the NIST P-256 curve.");
            var publicKey = signer.ExportSubjectPublicKeyInfo();
            var signature = new NarrativeDiagnosticReviewSignature(
                SchemaVersion,
                "ECDSA_P256_SHA256",
                credentials.SignerName.Trim(),
                Convert.ToBase64String(publicKey),
                Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
                Convert.ToBase64String(signer.SignData(
                    payload,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
            return unsigned with { Signature = signature };
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException(
                "Diagnostic review signing key is not a usable ECDSA P-256 private key.",
                ex);
        }
    }

    internal static NarrativeDiagnosticSignatureVerification Verify(
        NarrativeDiagnosticReviewDocument document,
        NarrativeDiagnosticTrustStore trustStore)
    {
        if (document.Signature is not { } signature)
            return Unsigned("Review is unsigned.");
        try
        {
            if (!string.Equals(signature.SchemaVersion, SchemaVersion, StringComparison.Ordinal)
                || !string.Equals(signature.Algorithm, "ECDSA_P256_SHA256", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(signature.SignerName))
            {
                return Invalid("Review signature contract is unsupported.", signature);
            }
            var payload = Serialize(document with { Signature = null });
            var payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            var publicKey = Convert.FromBase64String(signature.PublicKeySpki);
            var fingerprint = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (!payloadHash.Equals(signature.SignedPayloadSha256, StringComparison.OrdinalIgnoreCase)
                || bytesRead != publicKey.Length
                || verifier.KeySize != 256
                || !verifier.VerifyData(
                    payload,
                    Convert.FromBase64String(signature.Signature),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return Invalid("Review signature verification failed.", signature, fingerprint);
            }
            bool trusted;
            try
            {
                trusted = trustStore.IsTrusted(fingerprint, signature.PublicKeySpki);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return new NarrativeDiagnosticSignatureVerification(
                    NarrativeDiagnosticAuthenticityStatus.SignatureVerified,
                    signature.Algorithm,
                    signature.SignerName,
                    fingerprint,
                    signature.PublicKeySpki,
                    $"Review signature verified; local trust could not be read: {ex.Message}");
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
                    ? $"Review signature verified; signer '{signature.SignerName}' is locally trusted."
                    : $"Review signature verified; signer '{signature.SignerName}' is not locally trusted.");
        }
        catch (Exception ex) when (
            ex is CryptographicException
                or FormatException
                or ArgumentException
                or InvalidDataException)
        {
            return Invalid($"Review signature is invalid: {ex.Message}", signature);
        }
    }

    internal static NarrativeDiagnosticReviewComparison? CompareLatest(
        NarrativeDiagnosticReviewDocument document)
    {
        if (document.Revisions.Count < 2)
            return null;
        var current = document.Revisions[^1];
        var previous = document.Revisions[^2];
        var added = current.Entries
            .Where(entry => previous.Entries.All(item => item.Id != entry.Id))
            .ToArray();
        var removed = previous.Entries
            .Where(entry => current.Entries.All(item => item.Id != entry.Id))
            .ToArray();
        var statusChanged = previous.Status != current.Status;
        return new NarrativeDiagnosticReviewComparison(
            previous.Number,
            current.Number,
            $"Review r{previous.Number} → r{current.Number} · " +
            $"+{added.Length}/-{removed.Length} entries · " +
            $"{(statusChanged ? $"{previous.Status} → {current.Status}" : $"status {current.Status}")}",
            added,
            removed,
            statusChanged);
    }

    internal static async Task ExportCaseAsync(
        string path,
        string bundlePath,
        NarrativeDiagnosticReviewDocument review,
        CancellationToken cancellationToken = default)
    {
        var bundle = await File.ReadAllBytesAsync(bundlePath, cancellationToken).ConfigureAwait(false);
        await ExportCaseAsync(path, bundle, review, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task ExportCaseAsync(
        string path,
        byte[] bundle,
        NarrativeDiagnosticReviewDocument review,
        CancellationToken cancellationToken = default)
    {
        Validate(review, review.BundleId);
        if (bundle.LongLength > MaximumCaseBytes)
            throw new InvalidDataException("Diagnostic bundle exceeds the case safety limit.");
        var reviewBytes = Serialize(review);
        if (reviewBytes.LongLength > MaximumReviewBytes)
            throw new InvalidDataException("Diagnostic review exceeds the 4 MiB safety limit.");
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["bundle.zip"] = bundle,
            ["review.json"] = reviewBytes
        };
        entries["case.json"] = JsonSerializer.SerializeToUtf8Bytes(
            new NarrativeDiagnosticCaseMetadata(
                SchemaVersion,
                review.BundleId,
                review.Revisions.LastOrDefault()?.Number ?? 0),
            JsonOptions);
        var hashes = entries.ToDictionary(
            item => item.Key,
            item => Convert.ToHexString(SHA256.HashData(item.Value)).ToLowerInvariant(),
            StringComparer.Ordinal);
        entries["case-manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(
            new NarrativeDiagnosticCaseManifest(SchemaVersion, hashes),
            JsonOptions);
        await WriteZipAsync(path, entries, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<NarrativeDiagnosticCasePayload> ImportCaseAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists)
            throw new FileNotFoundException("Diagnostic case was not found.", file.FullName);
        if (file.Length > MaximumCaseBytes)
            throw new InvalidDataException("Diagnostic case exceeds the 80 MiB safety limit.");
        using var archive = ZipFile.OpenRead(file.FullName);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "bundle.zip", "review.json", "case.json", "case-manifest.json"
        };
        if (archive.Entries.Count != expected.Count
            || archive.Entries.Any(entry => !expected.Contains(entry.FullName))
            || archive.Entries.Select(entry => entry.FullName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Count)
        {
            throw new InvalidDataException("Diagnostic case contains an unexpected structure.");
        }
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaximumCaseBytes)
                throw new InvalidDataException($"Diagnostic case entry '{entry.FullName}' is too large.");
            totalLength += entry.Length;
            if (totalLength > MaximumCaseBytes)
                throw new InvalidDataException("Diagnostic case exceeds the uncompressed safety limit.");
            await using var input = entry.Open();
            using var output = new MemoryStream((int)entry.Length);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            if (output.Length != entry.Length)
                throw new InvalidDataException(
                    $"Diagnostic case entry '{entry.FullName}' has an inconsistent length.");
            entries[entry.FullName] = output.ToArray();
        }
        var manifest = JsonSerializer.Deserialize<NarrativeDiagnosticCaseManifest>(
            entries["case-manifest.json"],
            JsonOptions)
            ?? throw new InvalidDataException("Diagnostic case manifest is empty.");
        if (!string.Equals(manifest.SchemaVersion, SchemaVersion, StringComparison.Ordinal)
            || manifest.Sha256 is null
            || manifest.Sha256.Count != 3)
        {
            throw new InvalidDataException("Diagnostic case manifest has an unsupported schema.");
        }
        foreach (var name in expected.Where(name => name != "case-manifest.json"))
        {
            var actual = Convert.ToHexString(SHA256.HashData(entries[name])).ToLowerInvariant();
            if (!manifest.Sha256.TryGetValue(name, out var expectedHash)
                || !actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Diagnostic case integrity check failed for '{name}'.");
            }
        }
        var metadata = JsonSerializer.Deserialize<NarrativeDiagnosticCaseMetadata>(
            entries["case.json"],
            JsonOptions)
            ?? throw new InvalidDataException("Diagnostic case metadata is empty.");
        var review = DeserializeReview(entries["review.json"]);
        Validate(review, metadata.BundleId);
        if (!string.Equals(metadata.SchemaVersion, SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(metadata.BundleId, review.BundleId, StringComparison.Ordinal)
            || metadata.ReviewRevision != (review.Revisions.LastOrDefault()?.Number ?? 0))
        {
            throw new InvalidDataException("Diagnostic case metadata does not match its review.");
        }
        return new NarrativeDiagnosticCasePayload(entries["bundle.zip"], review);
    }

    private static NarrativeDiagnosticReviewDocument DeserializeReview(byte[] content)
    {
        try
        {
            return JsonSerializer.Deserialize<NarrativeDiagnosticReviewDocument>(
                content,
                JsonOptions)
                ?? throw new InvalidDataException("Diagnostic review is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Diagnostic review contains invalid JSON.", ex);
        }
    }

    private static byte[] Serialize(NarrativeDiagnosticReviewDocument document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);

    private static void Validate(
        NarrativeDiagnosticReviewDocument document,
        string bundleId)
    {
        if (!string.Equals(document.SchemaVersion, SchemaVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.BundleId)
            || !string.Equals(document.BundleId, bundleId, StringComparison.Ordinal)
            || document.Entries is null
            || document.Revisions is null
            || !Enum.IsDefined(document.Status)
            || !ValidEntries(document.Entries)
            || document.Revisions.Select((revision, index) => (revision, index)).Any(item =>
                item.revision is null
                || item.revision.Number != item.index + 1
                || string.IsNullOrWhiteSpace(item.revision.SavedBy)
                || !Enum.IsDefined(item.revision.Status)
                || item.revision.Entries is null
                || !ValidEntries(item.revision.Entries)))
        {
            throw new InvalidDataException(
                "Diagnostic review is invalid or belongs to a different Bundle ID.");
        }
    }

    private static bool ValidEntries(
        IReadOnlyList<NarrativeDiagnosticReviewEntry> entries) =>
        entries.All(entry =>
            entry is not null
            && !string.IsNullOrWhiteSpace(entry.Id)
            && entry.Id.Length <= 128
            && !string.IsNullOrWhiteSpace(entry.Author)
            && entry.Author.Length <= 200
            && !string.IsNullOrWhiteSpace(entry.Text)
            && entry.Text.Length <= 16_384
            && Enum.IsDefined(entry.Kind)
            && (entry.SymbolId is null || entry.SymbolId.Length <= 1024)
            && (entry.EvidencePath is null || entry.EvidencePath.Length <= 2048))
        && entries.Select(entry => entry.Id)
            .Distinct(StringComparer.Ordinal).Count() == entries.Count;

    private static NarrativeDiagnosticSignatureVerification Unsigned(string message) =>
        new(
            NarrativeDiagnosticAuthenticityStatus.Unsigned,
            null,
            null,
            null,
            null,
            message);

    private static NarrativeDiagnosticSignatureVerification Invalid(
        string message,
        NarrativeDiagnosticReviewSignature signature,
        string? fingerprint = null) =>
        new(
            NarrativeDiagnosticAuthenticityStatus.InvalidSignature,
            signature.Algorithm,
            signature.SignerName,
            fingerprint,
            signature.PublicKeySpki,
            message);

    private static async Task WriteZipAsync(
        string path,
        IReadOnlyDictionary<string, byte[]> entries,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        try
        {
            {
                await using var file = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);
                using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);
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
}
