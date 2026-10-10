using System.Diagnostics;
using Junexis.Studio.Abstractions;

namespace JunexisLang.Studio.CompilerExtension;

/// <summary>
/// Implementacja <see cref="ICompilerService"/> dla kompilatora JunexisLang (.jnx).
/// Wywołuje CLI <c>junexis</c> jako subprocess i parsuje Tooling Protocol 2.0.
/// </summary>
public sealed class JunexisLangCompilerService : ICompilerService, ICompilerPreflightService
{
    private readonly JunexisCompilerOptions _options;
    private List<Diagnostic> _lastDiagnostics = [];

    /// <inheritdoc/>
    public string CompilerId => "junexis-lang";

    /// <summary>
    /// Tworzy serwis kompilatora z domyślnymi opcjami.
    /// </summary>
    public JunexisLangCompilerService() : this(new JunexisCompilerOptions()) { }

    /// <summary>
    /// Tworzy serwis kompilatora z konfigurowalnymi opcjami.
    /// </summary>
    /// <param name="options">Opcje konfiguracyjne: CliPath, TimeoutMs.</param>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="options"/> jest <see langword="null"/>.</exception>
    public JunexisLangCompilerService(JunexisCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc/>
    public bool SupportsFile(string fileExtension)
        => string.Equals(fileExtension, ".jnx", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<CompilerResult> BuildAsync(BuildContext context) =>
        BuildAsync(context, CancellationToken.None);

    /// <inheritdoc/>
    public async Task<CompilerResult> BuildAsync(
        BuildContext context,
        CancellationToken cancellationToken)
        => await BuildCoreAsync(context, null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<CompilerPreflightResult> PreflightAsync(
        BuildContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var input = ResolveBuildInput(context);
        if (input is null)
        {
            var diagnostic = new Diagnostic(
                DiagnosticSeverity.Warning,
                "No Junexis project or .jnx file found in workspace.",
                source: CompilerId);
            _lastDiagnostics = [diagnostic];
            return new CompilerPreflightResult(false, _lastDiagnostics, CompilerId);
        }

        var execution = await RunCliAsync(
            ["analyze", input, "--json"],
            ResolveWorkingDirectory(input),
            cancellationToken).ConfigureAwait(false);
        if (execution.StartDiagnostic is not null)
        {
            _lastDiagnostics = [execution.StartDiagnostic];
            return new CompilerPreflightResult(false, _lastDiagnostics, CompilerId);
        }

        var parsed = JunexisCliOutputParser.ParseBuildResult(execution.StandardOutput);
        _lastDiagnostics = [.. parsed.Diagnostics];
        if (execution.ExitCode != 0 && _lastDiagnostics.Count == 0)
        {
            var message = string.IsNullOrWhiteSpace(execution.StandardError)
                ? $"Junexis CLI exited with code {execution.ExitCode}."
                : execution.StandardError.Trim();
            _lastDiagnostics = [new Diagnostic(DiagnosticSeverity.Error, message, source: CompilerId)];
        }
        return new CompilerPreflightResult(
            execution.ExitCode == 0 && parsed.Success,
            _lastDiagnostics,
            CompilerId,
            parsed.BuildId,
            parsed.AnalysisSnapshotPath,
            parsed.ProjectRoot);
    }

    /// <inheritdoc />
    public Task<CompilerResult> BuildAfterPreflightAsync(
        BuildContext context,
        CompilerPreflightResult preflight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        if (!preflight.Success || string.IsNullOrWhiteSpace(preflight.BuildId))
            throw new ArgumentException("A successful preflight with a build identity is required.", nameof(preflight));
        return BuildCoreAsync(context, preflight.BuildId, cancellationToken);
    }

    private async Task<CompilerResult> BuildCoreAsync(
        BuildContext context,
        string? expectedBuildId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var input = ResolveBuildInput(context);
        if (input is null)
        {
            var diagnostic = new Diagnostic(
                DiagnosticSeverity.Warning,
                "No Junexis project or .jnx file found in workspace.",
                source: CompilerId);
            _lastDiagnostics = [diagnostic];
            return new CompilerResult(false, _lastDiagnostics, CompilerId);
        }

        var arguments = new List<string>
        {
            "build", input, "--json", "--output-schema", "2.0"
        };
        if (!string.IsNullOrWhiteSpace(expectedBuildId))
        {
            arguments.Add("--build-id");
            arguments.Add(expectedBuildId);
        }
        var execution = await RunCliAsync(
            arguments,
            ResolveWorkingDirectory(input),
            cancellationToken).ConfigureAwait(false);
        if (execution.StartDiagnostic is not null)
        {
            _lastDiagnostics = [execution.StartDiagnostic];
            return new CompilerResult(false, _lastDiagnostics, CompilerId);
        }

        var parsed = JunexisCliOutputParser.ParseBuildResult(execution.StandardOutput);
        _lastDiagnostics = [.. parsed.Diagnostics];
        if (execution.ExitCode != 0 && _lastDiagnostics.Count == 0)
        {
            var message = string.IsNullOrWhiteSpace(execution.StandardError)
                ? $"Junexis CLI exited with code {execution.ExitCode}."
                : execution.StandardError.Trim();
            _lastDiagnostics = [new Diagnostic(DiagnosticSeverity.Error, message, source: CompilerId)];
        }
        return new CompilerResult(
            execution.ExitCode == 0 && parsed.Success,
            _lastDiagnostics,
            CompilerId,
            parsed.BuildId,
            parsed.ProjectRoot,
            parsed.OutputRoot,
            parsed.Targets,
            parsed.BundleManifestPath,
            parsed.ArtifactManifestPath,
            parsed.DebugManifestPath,
            parsed.AnalysisSnapshotPath,
            parsed.LatestBuildReceiptPath);
    }

    /// <inheritdoc/>
    public Task<CompilerResult> CleanAsync(BuildContext context) =>
        CleanAsync(context, CancellationToken.None);

    /// <inheritdoc/>
    public async Task<CompilerResult> CleanAsync(
        BuildContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var input = ResolveBuildInput(context);
        if (input is null)
            return new CompilerResult(true, [], CompilerId);

        var execution = await RunCliAsync(
            ["clean", input],
            ResolveWorkingDirectory(input),
            cancellationToken).ConfigureAwait(false);
        if (execution.StartDiagnostic is not null)
        {
            _lastDiagnostics = [execution.StartDiagnostic];
            return new CompilerResult(false, _lastDiagnostics, CompilerId);
        }

        _lastDiagnostics = [];
        return new CompilerResult(execution.ExitCode == 0, [], CompilerId);
    }

    /// <inheritdoc/>
    public IReadOnlyList<Diagnostic> GetDiagnostics() => _lastDiagnostics;

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Wyznacza WorkingDirectory dla subprocess CLI.
    /// Priorytet: katalog pliku .jnx (jeśli istnieje) → <see cref="Environment.CurrentDirectory"/>.
    /// </summary>
    /// <param name="targetFile">Ścieżka pliku .jnx przekazanego do CLI.</param>
    /// <returns>Istniejący katalog do użycia jako WorkingDirectory.</returns>
    internal static string ResolveWorkingDirectory(string targetFile)
    {
        try
        {
            var fullPath = Path.GetFullPath(targetFile);
            if (Directory.Exists(fullPath))
                return fullPath;
            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null && Directory.Exists(dir))
                return dir;
        }
        catch (ArgumentException) { }
        catch (PathTooLongException) { }
        catch (NotSupportedException) { }

        return Environment.CurrentDirectory;
    }

    private async Task<CliExecutionResult> RunCliAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo psi;
        try
        {
            psi = CreateStartInfo(_options.ResolvedCliPath, arguments, workingDirectory);
        }
        catch (Exception ex)
        {
            return new CliExecutionResult(-1, "", "", new Diagnostic(
                DiagnosticSeverity.Error,
                $"Failed to start compiler '{_options.ResolvedCliPath}': {ex.Message}",
                source: CompilerId));
        }

        using var timeoutCts = new CancellationTokenSource(_options.TimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            cancellationToken);
        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new CliExecutionResult(-1, "", "", new Diagnostic(
                DiagnosticSeverity.Error,
                $"Compiler CLI not found or failed to start: {ex.Message}",
                source: CompilerId));
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* best-effort */ }

            if (cancellationToken.IsCancellationRequested)
                throw;

            return new CliExecutionResult(-1, await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false), new Diagnostic(
                DiagnosticSeverity.Error,
                $"Compiler timed out after {_options.TimeoutMs} ms.",
                source: CompilerId));
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new CliExecutionResult(process.ExitCode, stdout, stderr);
    }

    internal static ProcessStartInfo CreateStartInfo(
        string cliPath,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cliPath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var isDll = cliPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo(isDll ? "dotnet" : cliPath)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (isDll)
            startInfo.ArgumentList.Add(Path.GetFullPath(cliPath));
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    internal static string? ResolveBuildInput(BuildContext context)
    {
        if (Directory.Exists(context.WorkspaceRoot) &&
            File.Exists(Path.Combine(context.WorkspaceRoot, "junexis.project.toml")))
            return Path.GetFullPath(context.WorkspaceRoot);

        if (!string.IsNullOrWhiteSpace(context.ActiveDocumentPath))
        {
            var manifestRoot = FindManifestRoot(context.ActiveDocumentPath, context.WorkspaceRoot);
            if (manifestRoot is not null)
                return manifestRoot;
            return context.ActiveDocumentPath;
        }

        return FindFirstJnxFile(context.WorkspaceRoot);
    }

    private static string? FindManifestRoot(string path, string workspaceRoot)
    {
        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path))!);
        }
        catch
        {
            return null;
        }

        var workspace = string.IsNullOrWhiteSpace(workspaceRoot) ? null : Path.GetFullPath(workspaceRoot);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "junexis.project.toml")))
                return directory.FullName;
            if (workspace is not null && directory.FullName.Equals(workspace, StringComparison.OrdinalIgnoreCase))
                break;
            directory = directory.Parent;
        }
        return null;
    }

    private static string? FindFirstJnxFile(string workspaceRoot)
    {
        if (!Directory.Exists(workspaceRoot))
            return null;

        return Directory
            .EnumerateFiles(workspaceRoot, "*.jnx", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
    }

    private sealed record CliExecutionResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        Diagnostic? StartDiagnostic = null);
}
