using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Serwis wyboru aktywnego kompilatora.
/// Reguły: explicit config > SupportsFile mapping > default (pierwszy zarejestrowany).
/// Fallback: brak kompilatora = info w Output.
/// </summary>
public sealed class CompilerSelector : ICompilerRegistry
{
    private readonly List<ICompilerService> _compilers = new();
    private string? _explicitCompilerId;
    private readonly IOutput? _output;

    /// <summary>Kanał Output dla logów selekcji.</summary>
    public const string OutputChannel = "Compiler";

    /// <summary>
    /// Tworzy selektor kompilatorów.
    /// </summary>
    /// <param name="output">Opcjonalny Output do logowania.</param>
    public CompilerSelector(IOutput? output = null)
    {
        _output = output;
    }

    /// <summary>
    /// Rejestruje kompilator.
    /// </summary>
    /// <param name="compiler">Instancja ICompilerService.</param>
    public void Register(ICompilerService compiler)
    {
        ArgumentNullException.ThrowIfNull(compiler);

        _compilers.Add(compiler);
        _output?.Write(OutputChannel, $"Compiler registered: {compiler.CompilerId}", OutputSeverity.Info);
    }

    /// <summary>
    /// Ustawia explicit kompilator wg ID (config workspace).
    /// Nadpisuje selekcję opartą na SupportsFile.
    /// </summary>
    /// <param name="compilerId">Identyfikator kompilatora (lub <see langword="null"/> aby usunąć override).</param>
    public void SetExplicit(string? compilerId)
    {
        _explicitCompilerId = compilerId;
    }

    /// <summary>
    /// Zwraca listę zarejestrowanych kompilatorów.
    /// </summary>
    public IReadOnlyList<ICompilerService> GetRegistered() => _compilers;

    /// <summary>
    /// Wybiera kompilator dla danego rozszerzenia pliku.
    /// Priorytet: explicit > SupportsFile > default.
    /// </summary>
    /// <param name="fileExtension">Rozszerzenie pliku (np. ".cpp").</param>
    /// <returns>Kompilator lub <see langword="null"/> jeśli brak pasującego.</returns>
    public ICompilerService? Select(string fileExtension)
    {
        // 1. Explicit config.
        if (_explicitCompilerId is not null)
        {
            var explicit_ = _compilers.FirstOrDefault(c =>
                string.Equals(c.CompilerId, _explicitCompilerId, StringComparison.OrdinalIgnoreCase));

            if (explicit_ is not null)
            {
                return explicit_;
            }

            _output?.Write(OutputChannel,
                $"Explicit compiler '{_explicitCompilerId}' not found, falling back to auto-select.",
                OutputSeverity.Warning);
        }

        // 2. SupportsFile mapping.
        var byFile = _compilers.FirstOrDefault(c => c.SupportsFile(fileExtension));

        if (byFile is not null)
        {
            return byFile;
        }

        // 3. Default = first registered.
        if (_compilers.Count > 0)
        {
            return _compilers[0];
        }

        _output?.Write(OutputChannel,
            $"No compiler available for '{fileExtension}'.",
            OutputSeverity.Warning);

        return null;
    }
}
