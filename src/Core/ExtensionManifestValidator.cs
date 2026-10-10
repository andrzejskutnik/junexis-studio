using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Wynik walidacji manifestu — deterministyczny, testowalny.
/// </summary>
/// <param name="Manifest">Walidowany manifest.</param>
/// <param name="IsValid">Czy manifest przeszedł walidację.</param>
/// <param name="Errors">Lista błędów walidacji.</param>
public sealed record ManifestValidationResult(
    ExtensionManifest Manifest,
    bool IsValid,
    IReadOnlyList<string> Errors);

/// <summary>
/// Waliduje manifest extension: schema, dependencies, permissions, capabilities.
/// Blokuje ładowanie przy błędach (zwraca czytelne diagnostyki).
/// </summary>
public static class ExtensionManifestValidator
{
    /// <summary>Zestaw znanych uprawnień — rozszerzalny.</summary>
    private static readonly HashSet<string> KnownPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "fs.read",
        "fs.write",
        "process.exec",
        "network.http",
        "ui.panel",
        "diagnostics.write",
        "audit.read",
    };

    /// <summary>
    /// Waliduje manifest extension.
    /// </summary>
    /// <param name="manifest">Manifest do walidacji.</param>
    /// <param name="availableExtensionIds">Zbiór dostępnych extension IDs (do sprawdzania dependencies).</param>
    /// <returns>Wynik walidacji z listą błędów.</returns>
    public static ManifestValidationResult Validate(
        ExtensionManifest manifest,
        IReadOnlySet<string>? availableExtensionIds = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<string>();

        ValidateSchema(manifest, errors);
        ValidateDependencies(manifest, availableExtensionIds, errors);
        ValidatePermissions(manifest, errors);
        ValidateCapabilities(manifest, errors);

        return new ManifestValidationResult(manifest, errors.Count == 0, errors);
    }

    private static void ValidateSchema(ExtensionManifest manifest, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            errors.Add("Schema: 'Id' is required.");
        }
        else
        {
            // Id should be a reasonable identifier (lowercase, alphanumeric + hyphens).
            if (manifest.Id.Length > 128)
            {
                errors.Add("Schema: 'Id' exceeds maximum length (128).");
            }
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            errors.Add("Schema: 'Version' is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryType))
        {
            errors.Add("Schema: 'EntryType' is required.");
        }
        else if (!manifest.EntryType.Contains('.'))
        {
            errors.Add("Schema: 'EntryType' should be a fully-qualified type name (Namespace.Type).");
        }
    }

    private static void ValidateDependencies(
        ExtensionManifest manifest,
        IReadOnlySet<string>? availableIds,
        List<string> errors)
    {
        if (manifest.Dependencies is null || manifest.Dependencies.Count == 0)
        {
            return;
        }

        foreach (var dep in manifest.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dep))
            {
                errors.Add("Dependencies: empty dependency entry.");
                continue;
            }

            // Format: "extensionId" or "extensionId:versionRange"
            var parts = dep.Split(':', 2);
            var depId = parts[0].Trim();

            if (string.IsNullOrWhiteSpace(depId))
            {
                errors.Add($"Dependencies: invalid format '{dep}'.");
                continue;
            }

            if (availableIds is not null && !availableIds.Contains(depId))
            {
                errors.Add($"Dependencies: required extension '{depId}' is not available.");
            }
        }
    }

    private static void ValidatePermissions(ExtensionManifest manifest, List<string> errors)
    {
        if (manifest.Permissions is null || manifest.Permissions.Count == 0)
        {
            return;
        }

        foreach (var perm in manifest.Permissions)
        {
            if (string.IsNullOrWhiteSpace(perm))
            {
                errors.Add("Permissions: empty permission entry.");
                continue;
            }

            if (!KnownPermissions.Contains(perm))
            {
                errors.Add($"Permissions: unknown permission '{perm}'.");
            }
        }
    }

    private static void ValidateCapabilities(ExtensionManifest manifest, List<string> errors)
    {
        if (manifest.ExposedCapabilities is null || manifest.ExposedCapabilities.Count == 0)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cap in manifest.ExposedCapabilities)
        {
            if (string.IsNullOrWhiteSpace(cap))
            {
                errors.Add("Capabilities: empty capability entry.");
                continue;
            }

            if (!seen.Add(cap))
            {
                errors.Add($"Capabilities: duplicate capability '{cap}'.");
            }
        }
    }
}
