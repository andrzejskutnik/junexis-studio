using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Wynik odkrycia pojedynczego rozszerzenia.
/// </summary>
/// <param name="FolderPath">Ścieżka folderu rozszerzenia.</param>
/// <param name="Manifest">Poprawny manifest (lub <see langword="null"/> gdy odrzucone).</param>
/// <param name="Accepted">Czy rozszerzenie zostało zaakceptowane do ładowania.</param>
/// <param name="RejectReason">Powód odrzucenia (jeśli dotyczy).</param>
public sealed record DiscoveryResult(
    string FolderPath,
    ExtensionManifest? Manifest,
    bool Accepted,
    string? RejectReason = null);

/// <summary>
/// Odkrywa rozszerzenia z folderu <c>./extensions</c>.
/// Wczytuje manifesty, filtruje po <c>MinimalIdeVersion</c> i raportuje do <see cref="IOutput"/>.
/// </summary>
public sealed class ExtensionDiscoveryService
{
    /// <summary>Kanał Output, na który raportowane są wyniki discovery.</summary>
    public const string OutputChannel = "Extensions";

    private readonly IOutput _output;
    private readonly string? _currentIdeVersion;

    /// <summary>
    /// Tworzy serwis discovery.
    /// </summary>
    /// <param name="output">Serwis logowania.</param>
    /// <param name="currentIdeVersion">Bieżąca wersja IDE do porównania z MinimalIdeVersion (opcjonalna).</param>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="output"/> jest <see langword="null"/>.</exception>
    public ExtensionDiscoveryService(IOutput output, string? currentIdeVersion = null)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _currentIdeVersion = currentIdeVersion;
    }

    /// <summary>
    /// Odkrywa rozszerzenia w katalogu <c>extensions/</c> obok podanego katalogu bazowego.
    /// Brak katalogu extensions nie jest błędem — zwraca pustą listę.
    /// </summary>
    /// <param name="baseDirectory">Katalog bazowy (np. <c>AppContext.BaseDirectory</c>).</param>
    /// <returns>Lista wyników discovery (zaakceptowane + odrzucone).</returns>
    public IReadOnlyList<DiscoveryResult> Discover(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);

        var folders = ExtensionPaths.EnumerateExtensionFolders(baseDirectory);

        if (folders.Count == 0)
        {
            _output.Write(OutputChannel, "No extension folders found.", OutputSeverity.Info);
            return Array.Empty<DiscoveryResult>();
        }

        var results = new List<DiscoveryResult>(folders.Count);

        foreach (var folder in folders)
        {
            var result = DiscoverSingle(folder);
            results.Add(result);

            if (result.Accepted)
            {
                _output.Write(OutputChannel,
                    $"Discovered extension '{result.Manifest!.Id}' v{result.Manifest.Version} in {Path.GetFileName(folder)}.",
                    OutputSeverity.Info);
            }
            else
            {
                _output.Write(OutputChannel,
                    $"Rejected extension in {Path.GetFileName(folder)}: {result.RejectReason}",
                    OutputSeverity.Warning);
            }
        }

        _output.Write(OutputChannel,
            $"Discovery complete: {results.Count(r => r.Accepted)} accepted, {results.Count(r => !r.Accepted)} rejected.",
            OutputSeverity.Info);

        return results;
    }

    /// <summary>
    /// Odkrywa jedno rozszerzenie z podanego katalogu.
    /// </summary>
    private DiscoveryResult DiscoverSingle(string extensionFolder)
    {
        var manifestPath = Path.Combine(extensionFolder, ExtensionPaths.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return new DiscoveryResult(extensionFolder, null, false, "Missing extension.json manifest.");
        }

        string json;

        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception ex)
        {
            return new DiscoveryResult(extensionFolder, null, false, $"Cannot read manifest: {ex.Message}");
        }

        var parseResult = ExtensionManifestParser.Parse(json, _currentIdeVersion);

        if (parseResult.Manifest is null)
        {
            var errors = string.Join("; ", parseResult.Errors);
            return new DiscoveryResult(extensionFolder, null, false, errors);
        }

        return new DiscoveryResult(extensionFolder, parseResult.Manifest, true);
    }
}
