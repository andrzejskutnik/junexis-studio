namespace Junexis.Studio.Abstractions;

/// <summary>
/// Model manifestu rozszerzenia (extension.json).
/// </summary>
/// <param name="Id">Unikalny identyfikator rozszerzenia.</param>
/// <param name="Version">Wersja rozszerzenia (SemVer).</param>
/// <param name="EntryType">Pełna nazwa typu wejściowego (implementującego IJunexisExtension).</param>
/// <param name="MinimalIdeVersion">Minimalna wymagana wersja IDE (SemVer, opcjonalna).</param>
/// <param name="ExposedCapabilities">Lista oferowanych zdolności (opcjonalna).</param>
/// <param name="Dependencies">Lista zależności (extensionId:versionRange) — opcjonalna.</param>
/// <param name="Permissions">Lista wymaganych uprawnień (np. "fs.read", "process.exec") — opcjonalna.</param>
public sealed record ExtensionManifest(
    string Id,
    string Version,
    string EntryType,
    string? MinimalIdeVersion = null,
    IReadOnlyList<string>? ExposedCapabilities = null,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyList<string>? Permissions = null);
