using Junexis.Studio.Abstractions;

namespace JunexisLang.Studio.CompilerExtension;

/// <summary>
/// Rozszerzenie kompilatora JunexisLang (.jnx).
/// Rejestruje <see cref="JunexisLangCompilerService"/> w <see cref="CompilerSelector"/>
/// i udostępnia w Studio komendy budowania oraz sesję aplikacji <c>@report</c>.
/// </summary>
public sealed class JunexisLangCompilerExtension : IJunexisExtension
{
    private const string BuildCommandId = "build.jnx";
    private const string StopCommandId  = "build.jnx.stop";
    private const string RestoreCommandId = "restore.jnx";
    private const string RestoreOfflineCommandId = "restore.jnx.offline";
    private const string OutputChannel  = "Build";
    private const string RestoreOutputChannel = "Restore";
    private const string RestoreDiagnosticSource = "junexis-restore";
    private const string ReportPanelId = "junexis.report.application";

    private readonly JunexisCompilerOptions _options;
    private CancellationTokenSource? _buildCts;
    private CancellationTokenSource? _restoreCts;
    private IEventBus? _eventBus;
    private IHostContext? _context;
    private ILatestBuildReceiptService? _latestBuilds;
    private string? _restoredReceiptKey;
    private JunexisReportApplicationSession? _reportSession;
    private ReportApplicationViewModel? _reportViewModel;

    /// <inheritdoc/>
    public string Id => "junexis.compiler.jnx";

    /// <summary>
    /// Tworzy rozszerzenie z domyślną konfiguracją.
    /// </summary>
    public JunexisLangCompilerExtension() : this(new JunexisCompilerOptions()) { }

    /// <summary>
    /// Tworzy rozszerzenie z konfigurowalnymi opcjami CLI.
    /// </summary>
    /// <param name="options">Opcje: CliPath, TimeoutMs.</param>
    /// <exception cref="ArgumentNullException">Gdy <paramref name="options"/> jest <see langword="null"/>.</exception>
    public JunexisLangCompilerExtension(JunexisCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc/>
    public void Initialize(IHostContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_options.ToolResolver is null
            && context.Capabilities.TryGet<IPlatformToolResolver>(out var toolResolver))
        {
            _options.ToolResolver = toolResolver;
        }

        var service = new JunexisLangCompilerService(_options);
        context.Capabilities.TryGet<ILatestBuildReceiptService>(out _latestBuilds);
        _context = context;
        _eventBus = context.EventBus;
        _eventBus.Subscribe<WorkspaceOpenedEvent>(OnWorkspaceOpened);
        _eventBus.Subscribe<DocumentOpened>(OnDocumentOpened);
        context.Capabilities.Register<IDeploymentService>(
            new JunexisDeploymentService(_options, context.Processes));

        // Rejestruj w CompilerSelector jeśli dostępny
        if (context.Capabilities.TryGet<ICompilerRegistry>(out var registry) && registry is not null)
        {
            registry.Register(service);
        }

        // Rejestruj jako capability ICompilerService
        context.Capabilities.Register<ICompilerService>(service);

        _reportSession = new JunexisReportApplicationSession(
            _options,
            context.Processes,
            service,
            context);
        context.Capabilities.Register<IReportApplicationSession>(_reportSession);
        _reportViewModel = new ReportApplicationViewModel(
            _reportSession,
            context.Workspace,
            context.ActiveDocument);
        context.Panels.RegisterPanel(
            ReportPanelId,
            PanelType.Tool,
            () => _reportViewModel,
            title: "Aplikacja",
            dockGroup: DockGroup.Right);

        // Komenda build.jnx
        context.Commands.Register(
            id: BuildCommandId,
            execute: () => StartBuild(context, service),
            menuPath: "Build/Build (junexis)",
            displayName: "Build (junexis)",
            category: "Build");

        // Komenda stop
        context.Commands.Register(
            id: StopCommandId,
            execute: () => StopBuild(context),
            menuPath: "Build/Stop Build (junexis)",
            displayName: "Stop Build (junexis)",
            category: "Build");

        context.Commands.Register(
            id: RestoreCommandId,
            execute: () => StartRestore(context, offline: false),
            canExecute: () => CanRestore(context),
            menuPath: "Build/Odtwórz zależności Junexis",
            displayName: "Odtwórz zależności Junexis",
            category: "Build");

        context.Commands.Register(
            id: RestoreOfflineCommandId,
            execute: () => StartRestore(context, offline: true),
            canExecute: () => CanRestore(context),
            menuPath: "Build/Odtwórz zależności Junexis offline",
            displayName: "Odtwórz zależności Junexis offline",
            category: "Build");

        context.Commands.Register(
            id: "run.jnx",
            execute: () =>
            {
                context.EventBus.Publish(new ActivatePanelRequested(ReportPanelId));
                _ = _reportViewModel.StartAsync();
            },
            canExecute: () => _reportViewModel.CanStart,
            menuPath: "Build/Uruchom aplikację Junexis",
            displayName: "Uruchom aplikację Junexis",
            category: "Build");

        context.Commands.Register(
            id: "run.jnx.open",
            execute: () => _reportViewModel.Open(),
            canExecute: () => _reportViewModel.CanOpen,
            menuPath: "Build/Otwórz panel @report",
            displayName: "Otwórz panel @report",
            category: "Build");

        context.Commands.Register(
            id: "run.jnx.stop",
            execute: () => _reportViewModel.Stop(),
            canExecute: () => _reportViewModel.CanStop,
            menuPath: "Build/Zatrzymaj aplikację Junexis",
            displayName: "Zatrzymaj aplikację Junexis",
            category: "Build");

        context.Output.Write("Extensions", "JunexisLangCompilerExtension loaded");
        if (!string.IsNullOrWhiteSpace(context.Workspace.RootPath))
            _ = RestoreLatestBuildAsync(context.Workspace.RootPath);
    }

    /// <inheritdoc/>
    public void Shutdown()
    {
        _reportViewModel?.Dispose();
        _reportViewModel = null;
        _reportSession?.Dispose();
        _reportSession = null;
        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = null;
        var restoreCts = Interlocked.Exchange(ref _restoreCts, null);
        restoreCts?.Cancel();
        _eventBus?.Unsubscribe<WorkspaceOpenedEvent>(OnWorkspaceOpened);
        _eventBus?.Unsubscribe<DocumentOpened>(OnDocumentOpened);
        _eventBus = null;
        _context = null;
        _latestBuilds = null;
        _restoredReceiptKey = null;
    }

    private void StopBuild(IHostContext context)
    {
        var cts = _buildCts;
        if (cts is null)
        {
            context.Output.Write(OutputChannel, "No build in progress.");
            return;
        }
        cts.Cancel();
        context.Output.Write(OutputChannel, "Stop requested.");
    }

    private bool CanRestore(IHostContext context) =>
        !string.IsNullOrWhiteSpace(context.Workspace.RootPath) && _restoreCts is null;

    private void StartRestore(IHostContext context, bool offline)
    {
        var cts = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref _restoreCts, cts, null) is not null)
        {
            cts.Dispose();
            return;
        }

        _ = RestoreDependenciesAsync(context, offline, cts);
    }

    private async Task RestoreDependenciesAsync(
        IHostContext context,
        bool offline,
        CancellationTokenSource operationCts)
    {
        var workspace = context.Workspace.RootPath;
        if (string.IsNullOrWhiteSpace(workspace))
        {
            Interlocked.CompareExchange(ref _restoreCts, null, operationCts);
            operationCts.Dispose();
            return;
        }

        var arguments = new List<string> { "restore", workspace };
        var localPackages = _options.ResolvedLocalPackagesPath;
        var registry = _options.ResolvedRegistryBaseAddress;
        string sourceLabel;
        if (offline)
        {
            arguments.Add("--offline");
            sourceLabel = "lokalna pamięć podręczna";
        }
        else if (!string.IsNullOrWhiteSpace(registry))
        {
            if (!TryNormalizeRegistryAddress(registry, out var normalizedRegistry))
            {
                context.Output.Write(
                    RestoreOutputChannel,
                    "Konfiguracja źródła Registry jest nieprawidłowa. Wymagany jest bazowy adres HTTPS bez danych użytkownika, parametrów zapytania i fragmentu.",
                    OutputSeverity.Error);
                Interlocked.CompareExchange(ref _restoreCts, null, operationCts);
                operationCts.Dispose();
                return;
            }
            arguments.Add("--registry");
            arguments.Add(normalizedRegistry);
            sourceLabel = "Registry";
        }
        else if (!string.IsNullOrWhiteSpace(localPackages))
        {
            arguments.Add("--local-packages");
            arguments.Add(localPackages);
            sourceLabel = "lokalny zestaw wydania";
        }
        else
        {
            sourceLabel = "skonfigurowany profil";
        }

        context.Output.Write(
            RestoreOutputChannel,
            $"Etap 1/3 · przygotowanie · źródło: {sourceLabel}.",
            OutputSeverity.Info);
        context.Output.Write(
            RestoreOutputChannel,
            "Etap 2/3 · pobieranie i weryfikacja zależności.",
            OutputSeverity.Info);
        context.Diagnostics.ClearBySource(RestoreDiagnosticSource);
        var failureClassifier = new RestoreFailureClassifier();
        try
        {
            var result = await context.Processes.RunAsync(
                new ProcessRequest(_options.ResolvedPublicCliPath, arguments, workspace),
                line =>
                {
                    failureClassifier.Observe(line.Text);
                    context.Output.Write(
                        RestoreOutputChannel,
                        RestoreOutputSanitizer.Sanitize(line.Text),
                        line.Kind == ProcessOutputKind.StdErr ? OutputSeverity.Error : OutputSeverity.Info);
                },
                operationCts.Token).ConfigureAwait(false);
            var severity = result.ExitCode == 0 ? OutputSeverity.Info : OutputSeverity.Error;
            context.Output.Write(
                RestoreOutputChannel,
                result.ExitCode == 0
                    ? "Etap 3/3 · zależności są gotowe."
                    : $"Etap 3/3 · odtwarzanie zatrzymane kodem {result.ExitCode}.",
                severity);
            if (result.ExitCode != 0 && failureClassifier.GetAdvice() is { } advice)
            {
                context.Output.Write(RestoreOutputChannel, advice.Message, OutputSeverity.Error);
                context.Diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    advice.Message,
                    Path.Combine(workspace, "junexis.project.toml"),
                    source: RestoreDiagnosticSource,
                    code: advice.Code));
            }
        }
        catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
        {
            context.Output.Write(RestoreOutputChannel, "Odtwarzanie zależności anulowane.", OutputSeverity.Info);
        }
        catch (Exception exception)
        {
            context.Output.Write(
                RestoreOutputChannel,
                $"Nie można uruchomić publicznego CLI Junexis: {RestoreOutputSanitizer.Sanitize(exception.Message)}",
                OutputSeverity.Error);
        }
        finally
        {
            Interlocked.CompareExchange(ref _restoreCts, null, operationCts);
            operationCts.Dispose();
        }
    }

    private static bool TryNormalizeRegistryAddress(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) ||
            address.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(address.UserInfo) ||
            !string.IsNullOrEmpty(address.Query) ||
            !string.IsNullOrEmpty(address.Fragment))
            return false;
        normalized = address.AbsoluteUri.TrimEnd('/');
        return true;
    }

    private void StartBuild(IHostContext context, JunexisLangCompilerService service)
    {
        var prev = Interlocked.Exchange(ref _buildCts, new CancellationTokenSource());
        prev?.Cancel();
        prev?.Dispose();

        var cts = _buildCts!;
        var lifecycle = WorkbenchOperationHandle.Begin(
            context, "junexis", "Junexis", LaunchOperation.Build, "Starting Junexis build…");

        _ = Task.Run(async () =>
        {
            var active    = context.ActiveDocument.ActivePath;
            var workspace = context.Workspace.RootPath ?? string.Empty;
            var buildCtx  = new BuildContext(workspace, active);

            context.Output.Write(OutputChannel, "Build started [junexis-lang].", OutputSeverity.Info);

            CompilerResult result;
            try
            {
                lifecycle.Running("Checking Junexis system…");
                var preflight = await service.PreflightAsync(
                    buildCtx,
                    cts.Token).ConfigureAwait(false);
                if (!preflight.Success)
                {
                    context.Diagnostics.ClearBySource(preflight.CompilerId);
                    context.EventBus.Publish(new DiagnosticsUpdatedEvent(
                        preflight.CompilerId,
                        preflight.Diagnostics,
                        preflight.BuildId));
                    PublishDiagnostics(context, preflight.Diagnostics);
                    context.Output.Write(OutputChannel,
                        $"Build stopped before code generation: {preflight.Diagnostics.Count} diagnostic(s).",
                        OutputSeverity.Warning);
                    lifecycle.Fail(
                        "Junexis preflight failed",
                        diagnostics: preflight.Diagnostics.Count);
                    return;
                }

                if (context.Capabilities.TryGet<IMentorBuildPreflightService>(out var mentorPreflight)
                    && mentorPreflight is not null)
                {
                    lifecycle.Running("Mentor reviewing system design…");
                    await mentorPreflight.RunAsync(preflight, cts.Token).ConfigureAwait(false);
                }

                lifecycle.Running("Generating Junexis targets…");
                result = await service.BuildAfterPreflightAsync(
                    buildCtx,
                    preflight,
                    cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                context.Output.Write(OutputChannel, "Build cancelled.", OutputSeverity.Warning);
                lifecycle.Cancel("Junexis build cancelled");
                return;
            }
            catch (Exception ex)
            {
                context.Output.Write(OutputChannel, $"Build failed: {ex.Message}", OutputSeverity.Error);
                lifecycle.Fail($"Junexis build failed: {ex.Message}");
                return;
            }

            var status = result.Success ? "succeeded" : "failed";
            context.Output.Write(OutputChannel,
                $"Build {status} [{result.CompilerId}] {result.Diagnostics.Count} diagnostic(s).",
                result.Success ? OutputSeverity.Info : OutputSeverity.Warning);

            var stateSynchronized = !result.Success;
            if (result.Success)
            {
                stateSynchronized = await PublishLatestBuildAsync(
                    context,
                    result.LatestBuildReceiptPath,
                    result.BuildId,
                    restored: false,
                    cts.Token).ConfigureAwait(false);
            }

            context.Diagnostics.ClearBySource(result.CompilerId);
            context.EventBus.Publish(new DiagnosticsUpdatedEvent(
                result.CompilerId,
                result.Diagnostics,
                result.BuildId));
            PublishDiagnostics(context, result.Diagnostics);
            if (result.Success && stateSynchronized)
                lifecycle.Complete(0, result.Diagnostics.Count, "Junexis build succeeded");
            else if (result.Success)
                lifecycle.Fail("Junexis build succeeded but Studio rejected latest-build state");
            else
                lifecycle.Fail("Junexis build failed", diagnostics: result.Diagnostics.Count);
        }, cts.Token);
    }

    private void OnWorkspaceOpened(WorkspaceOpenedEvent opened)
    {
        Volatile.Write(ref _restoredReceiptKey, null);
        _ = RestoreLatestBuildAsync(opened.RootPath);
    }

    private void OnDocumentOpened(DocumentOpened opened)
    {
        var workspaceRoot = _context?.Workspace.RootPath;
        if (!string.IsNullOrWhiteSpace(workspaceRoot)
            && Path.GetExtension(opened.Path).Equals(".jnx", StringComparison.OrdinalIgnoreCase))
        {
            _ = RestoreLatestBuildAsync(workspaceRoot, opened.Path);
        }
    }

    private async Task RestoreLatestBuildAsync(
        string workspaceRoot,
        string? activeDocumentPath = null)
    {
        var context = _context;
        var latestBuilds = _latestBuilds;
        if (context is null || latestBuilds is null)
            return;

        try
        {
            var receipt = await latestBuilds.TryLoadForContextAsync(
                workspaceRoot,
                activeDocumentPath ?? context.ActiveDocument.ActivePath).ConfigureAwait(false);
            if (receipt is not null)
            {
                var key = $"{receipt.ReceiptPath}\u001f{receipt.BuildId}";
                if (string.Equals(
                        Volatile.Read(ref _restoredReceiptKey),
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                PublishReceipt(context, receipt, restored: true);
                Volatile.Write(ref _restoredReceiptKey, key);
            }
        }
        catch (Exception ex)
        {
            context.Output.Write(
                OutputChannel,
                $"Cannot restore latest Junexis build: {ex.Message}",
                OutputSeverity.Warning);
        }
    }

    private async Task<bool> PublishLatestBuildAsync(
        IHostContext context,
        string? receiptPath,
        string? expectedBuildId,
        bool restored,
        CancellationToken cancellationToken)
    {
        if (_latestBuilds is null)
        {
            context.Output.Write(
                OutputChannel,
                "ILatestBuildReceiptService unavailable — build state was not published to Studio.",
                OutputSeverity.Error);
            return false;
        }
        if (string.IsNullOrWhiteSpace(receiptPath))
        {
            context.Output.Write(
                OutputChannel,
                "Compiler did not publish latest-build 1.0; update the Junexis CLI.",
                OutputSeverity.Error);
            return false;
        }

        try
        {
            var receipt = await _latestBuilds.LoadAsync(receiptPath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(expectedBuildId)
                && !receipt.BuildId.Equals(expectedBuildId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Latest-build buildId does not match the completed CLI response.");
            }
            PublishReceipt(context, receipt, restored);
            Volatile.Write(
                ref _restoredReceiptKey,
                $"{receipt.ReceiptPath}\u001f{receipt.BuildId}");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Output.Write(
                OutputChannel,
                $"Studio rejected latest-build receipt: {ex.Message}",
                OutputSeverity.Error);
            return false;
        }
    }

    private static void PublishReceipt(
        IHostContext context,
        LatestBuildReceipt receipt,
        bool restored)
    {
        context.Output.Write(
            OutputChannel,
            $"{(restored ? "Restored" : "Published")} build {receipt.BuildId}; targets: " +
            $"{string.Join(", ", receipt.Targets)}.",
            OutputSeverity.Info);
        context.Output.Write(OutputChannel, $"Artifacts: {receipt.ArtifactManifestPath}");
        context.Output.Write(OutputChannel, $"Debug manifest: {receipt.DebugManifestPath}");
        context.Output.Write(OutputChannel, $"Analysis snapshot: {receipt.AnalysisSnapshotPath}");
        context.EventBus.Publish(new LatestBuildUpdatedEvent("junexis-lang", receipt, restored));
        context.EventBus.Publish(new ArtifactsUpdatedEvent(
            "junexis-lang",
            receipt.BuildId,
            receipt.ArtifactManifestPath));
        context.EventBus.Publish(new AnalysisSnapshotUpdatedEvent(
            "junexis-lang",
            receipt.BuildId,
            receipt.AnalysisSnapshotPath));
    }

    private static void PublishDiagnostics(
        IHostContext context,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach (var d in diagnostics)
        {
            var sev = d.Severity switch
            {
                DiagnosticSeverity.Error   => OutputSeverity.Error,
                DiagnosticSeverity.Warning => OutputSeverity.Warning,
                _                          => OutputSeverity.Info,
            };
            var location = d.FilePath is not null
                ? $"{d.FilePath}:{d.Line}:{d.Column} "
                : string.Empty;
            context.Output.Write(OutputChannel, $"{location}{d.Severity}: {d.Message}", sev);
            context.Diagnostics.Add(d);
        }
    }
}
