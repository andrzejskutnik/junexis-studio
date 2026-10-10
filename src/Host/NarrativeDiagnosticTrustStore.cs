using System.Text;
using System.Text.Json;

namespace Junexis.Studio.Host;

internal sealed record NarrativeTrustedSigner(
    string Fingerprint,
    string Name,
    string PublicKeySpki,
    DateTimeOffset TrustedAt);

internal sealed record NarrativeDiagnosticTrustDocument(
    string SchemaVersion,
    IReadOnlyList<NarrativeTrustedSigner> Signers);

internal sealed class NarrativeDiagnosticTrustStore
{
    private const string SchemaVersion = "1.0";
    private const long MaximumStoreBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    internal NarrativeDiagnosticTrustStore(string? path = null)
    {
        _path = System.IO.Path.GetFullPath(path ?? DefaultPath());
    }

    internal bool IsTrusted(string fingerprint, string publicKeySpki) =>
        Load().Any(signer =>
            signer.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase)
            && signer.PublicKeySpki.Equals(publicKeySpki, StringComparison.Ordinal));

    internal void Trust(
        string fingerprint,
        string name,
        string publicKeySpki)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeySpki);
        var signers = Load()
            .Where(signer =>
                !signer.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase))
            .Append(new NarrativeTrustedSigner(
                fingerprint.ToLowerInvariant(),
                name.Trim(),
                publicKeySpki,
                DateTimeOffset.UtcNow))
            .OrderBy(signer => signer.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signer => signer.Fingerprint, StringComparer.Ordinal)
            .ToArray();
        Save(signers);
    }

    internal void Remove(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var signers = Load()
            .Where(signer =>
                !signer.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Save(signers);
    }

    private IReadOnlyList<NarrativeTrustedSigner> Load()
    {
        if (!File.Exists(_path))
            return [];
        var info = new FileInfo(_path);
        if (info.Length > MaximumStoreBytes)
            throw new InvalidDataException(
                "Narrative diagnostic trust store exceeds the 1 MiB safety limit.");
        try
        {
            var document = JsonSerializer.Deserialize<NarrativeDiagnosticTrustDocument>(
                File.ReadAllText(_path),
                JsonOptions);
            if (document is null
                || !string.Equals(document.SchemaVersion, SchemaVersion, StringComparison.Ordinal)
                || document.Signers is null)
            {
                throw new InvalidDataException(
                    "Narrative diagnostic trust store has an unsupported schema.");
            }
            return document.Signers
                .Where(signer =>
                    signer is not null
                    && !string.IsNullOrWhiteSpace(signer.Fingerprint)
                    && !string.IsNullOrWhiteSpace(signer.Name)
                    && !string.IsNullOrWhiteSpace(signer.PublicKeySpki))
                .ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Narrative diagnostic trust store contains invalid JSON.",
                ex);
        }
    }

    private void Save(IReadOnlyList<NarrativeTrustedSigner> signers)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    new NarrativeDiagnosticTrustDocument(SchemaVersion, signers),
                    JsonOptions),
                Encoding.UTF8);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string DefaultPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JunexisStudio",
            "narrative-diagnostic-trust.json");
}
