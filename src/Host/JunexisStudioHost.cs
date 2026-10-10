using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Mcp.Studio;
using Junexis.Mcp.Studio.Integration;
using JunexisLang.Studio.CompilerExtension;
using System.IO;
using System.Text.Json;

namespace Junexis.Studio.Host;

/// <summary>
/// Minimalny host v0.1: tworzy Core services, składa <see cref="IHostContext"/>
/// i inicjalizuje rozszerzenia przez <see cref="ExtensionLoader"/>.
/// </summary>
public sealed class JunexisStudioHost
{
    private readonly ExtensionLoader _loader = new();
    private HostState _state = HostState.Created;
    private readonly PreferencesService _preferences;
    private FileSystemWatcher? _agentSessionWatcher;
    private Timer? _agentRefreshTimer;
    private JsonlAgentEventTailer? _agentEventTailer;
    private Timer? _agentEventTimer;
    private AgentReportPanelViewModel? _agentReportPanel;

    private enum HostState
    {
        Created = 0,
        Started = 1,
        Shutdown = 2,
    }

    /// <summary>
    /// Tworzy hosta z domyślnymi usługami Core i rejestruje minimalne rozszerzenia.
    /// </summary>
    public JunexisStudioHost()
        : this(viewRegistry: null)
    {
    }

    /// <summary>
    /// Tworzy hosta z domyślnymi usługami Core i rejestruje minimalne rozszerzenia.
    /// </summary>
    /// <param name="viewRegistry">
    /// Opcjonalny rejestr mapowania ViewModel → View. Jeśli nie podano, host użyje pustej implementacji.
    /// </param>
    public JunexisStudioHost(IViewRegistry? viewRegistry)
    {
        var output = new OutputService();

        var eventBus = new EventBus(ex =>
            output.Write("EventBus", $"Handler exception: {ex.Message}", OutputSeverity.Error, "Host", ex.ToString()));

        var commands = new CommandRegistry((id, ex) =>
            output.Write("Commands", $"Command '{id}' failed: {ex.Message}", OutputSeverity.Error, "Host", ex.ToString()));

        var panels = new PanelRegistry();
        var processes = new ProcessService();
        var diagnostics = new DiagnosticService();
        var activeDocument = new ActiveDocumentService();
        var workspace = new WorkspaceService(eventBus);
        var openDocuments = new OpenDocumentRepository(eventBus, workspace);

        var versionControl = new GitVersionControlService(workspace, output);

        var audit = new AuditService(output);

        var views = viewRegistry ?? new NullViewRegistry();

        var mcpConnectors = new McpConnectorRegistry(output);

        var assistant = new AssistantService(
            backend: (history, _) =>
            {
                var last = history.LastOrDefault(m => m.Role == AssistantRole.User);
                var content = last?.Content ?? string.Empty;
                var preview = content.Length > 100 ? content[..100] : content;
                var reply = last is null
                    ? "Cześć! OMWiA jest w trybie offline — brak podłączonego backendu LLM."
                    : $"[offline] Otrzymano: \u201E{preview}\u201D";
                return Task.FromResult(new AssistantResponse(AssistantMessage.FromAssistant(reply)));
            },
            eventBus: eventBus,
            output: output);

        var whyTraceQueryService = new HttpWhyTraceQueryService();

        var capabilities = new CapabilityRegistry();
        var platformTools = new PlatformToolResolver();
        capabilities.Register(new StudioMcpBridge(workspace, activeDocument));
        capabilities.Register<IPlatformToolResolver>(platformTools);
        capabilities.Register<IProcessService>(processes);
        capabilities.Register<IDiagnosticService>(diagnostics);
        capabilities.Register<IOutput>(output);
        capabilities.Register<IMcpConnectorRegistry>(mcpConnectors);
        capabilities.Register<IAssistantService>(assistant);
        capabilities.Register<IWhyTraceQueryService>(whyTraceQueryService);

        var compilerSelector = new CompilerSelector(output);
        capabilities.Register<CompilerSelector>(compilerSelector);

        var workspaceItems = new WorkspaceItemService(eventBus, output);
        capabilities.Register<IWorkspaceItemService>(workspaceItems);

        var latestBuildReceipts = new LatestBuildReceiptService();
        capabilities.Register<ILatestBuildReceiptService>(latestBuildReceipts);

        var narrativeSession = new NarrativeSessionService(
            eventBus,
            output,
            platformTools.ResolveCommand("junexis-trace-collector", "JUNEXIS_TRACE_COLLECTOR"));
        capabilities.Register<INarrativeSessionService>(narrativeSession);
        capabilities.Register<INarrativeRuntimeEnvironmentService>(new NarrativeRuntimeEnvironmentService(narrativeSession));

        var debugSession = new DapDebugSessionService();
        capabilities.Register<IDebugSessionService>(debugSession);
        capabilities.Register<IDebugHoverService>(new DebugHoverService(debugSession));
        capabilities.Register<IBreakpointService>(new BreakpointService());
        capabilities.Register<IDebugAdapterRegistry>(new DebugAdapterRegistry());
        capabilities.Register<IDebugConfigurationService>(new DebugConfigurationService(workspace, output));

        var languageService = new LanguageService(eventBus, workspace, diagnostics, output);
        capabilities.Register<ILanguageService>(languageService);

        var toolchainDoctor = new ToolchainDoctorService(processes, new DebugAdapterRegistry(), platformTools);
        capabilities.Register<IToolchainDoctorService>(toolchainDoctor);
        capabilities.Register<IWorkbenchOperationService>(new WorkbenchOperationService());
        capabilities.Register<ILaunchProfileRegistry>(new LaunchProfileRegistry());

        var analysisSnapshots = new AnalysisSnapshotService();
        capabilities.Register<IAnalysisSnapshotService>(analysisSnapshots);
        var artifacts = new ArtifactService();
        capabilities.Register<IArtifactService>(artifacts);
        capabilities.Register<IDebugTargetAssessmentService>(
            new DebugTargetAssessmentService(eventBus, artifacts, output));

        _preferences = new PreferencesService(workspace, output);

        var keybindings = new KeybindingRegistry(output);

        Context = new HostContext(eventBus, commands, panels, output, processes, workspace, diagnostics, activeDocument, openDocuments, versionControl, views, audit, capabilities, _preferences, keybindings);
        Context.Workspace.FolderOpened += OnWorkspaceFolderOpened;
        _agentReportPanel = new AgentReportPanelViewModel(output, capabilities.Get<StudioMcpBridge>(), workspace);
        Context.Panels.RegisterPanel("agent.report", PanelType.Tool, () => _agentReportPanel, "Agent Report", DockGroup.Right);

        RegisterBuiltInPanels(Context);

        RegisterBuiltInCommands(Context);
        Context.Commands.Register(
            "agent.refresh-session",
            RefreshLatestAgentSession,
            menuPath: "Agent/Refresh Latest Session",
            displayName: "Refresh Latest Agent Session",
            category: "Agent");

        RegisterBuiltInKeybindings(keybindings);

        _loader.Add(new HelloExtension());
        _loader.Add(new TerminalExtension());
        _loader.Add(new ExplorerExtension());
        _loader.Add(new EditorExtension());
        _loader.Add(new ProblemsExtension());
        _loader.Add(new GppCompilerExtension());
        _loader.Add(new CsharpCompilerExtension());
        _loader.Add(new GoCompilerExtension());
        _loader.Add(new GitExtension());
        _loader.Add(new GitProviderExtension());
        _loader.Add(new VcsExtension());
        _loader.Add(new VcsDecorationExtension());
        _loader.Add(new McpStatusExtension());
        _loader.Add(new WhyPanelExtension());
        _loader.Add(new DapDebuggerExtension());
        _loader.Add(new OmwiaExtension());
        _loader.Add(new OmwiaChatExtension());
        _loader.Add(new AutoBuildExtension());
        _loader.Add(new JunexisLangCompilerExtension());
        _loader.Add(new WorkspaceSearchExtension());
        _loader.Add(new LanguageServicesExtension());
        _loader.Add(new ToolchainDoctorExtension());
        _loader.Add(new PlatformLifecycleExtension());
        _loader.Add(new WorkbenchRunExtension());
        _loader.Add(new TypeScriptCompilerExtension());
        _loader.Add(new HelpCenterExtension());
        _loader.Add(new OutputFilterExtension());
        _loader.Add(new AnalysisExplorerExtension());
        _loader.Add(new ArtifactExplorerExtension());
    }

    private sealed class NullViewRegistry : IViewRegistry
    {
        public void Register<TViewModel, TView>()
            where TViewModel : class
            where TView : class, new()
        {
            // no-op
        }

        public object? Resolve(object viewModel) => null;
    }

    /// <summary>
    /// Kontekst hosta udostępniany powłoce i rozszerzeniom.
    /// </summary>
    public IHostContext Context { get; }

    /// <summary>
    /// Uruchamia hosta i inicjalizuje rozszerzenia.
    /// </summary>
    public void Start()
    {
        if (_state == HostState.Started)
        {
            Context.Output.Write("Host", "Start skipped (already started)");
            return;
        }

        if (_state == HostState.Shutdown)
        {
            Context.Output.Write("Host", "Start skipped (already shutdown)");
            return;
        }

        Context.Output.Write("Host", "Starting...");
        _loader.InitializeAll(Context);
        ConfigureAgentSessionWatcher(Context.Workspace.RootPath);
        ConfigureAgentEventTailer(Context.Workspace.RootPath);
        RefreshLatestAgentSession();
        _state = HostState.Started;
        Context.Output.Write("Host", "Started");
    }

    /// <summary>
    /// Publishes the latest durable Agent session to the Studio Output channel.
    /// </summary>
    public void RefreshLatestAgentSession()
    {
        try
        {
            var sessionJson = new AgentSessionResourceReader(Context.Workspace).ReadLatest();
            using var document = JsonDocument.Parse(sessionJson);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusValue)
                ? statusValue.GetString() ?? "Unknown"
                : "Unknown";
            var goal = root.TryGetProperty("goal", out var goalValue)
                ? goalValue.GetString() ?? string.Empty
                : string.Empty;
            var changedFiles = root.TryGetProperty("changedFiles", out var filesValue)
                ? filesValue.GetArrayLength()
                : 0;
            var suffix = string.IsNullOrWhiteSpace(goal) ? string.Empty : $" — {goal}";
            Context.Output.Write(
                "Agent",
                $"Latest session: {status}{suffix}; changed files: {changedFiles}",
                status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
                    ? OutputSeverity.Info
                    : OutputSeverity.Warning,
                "Junexis.Mcp.Studio");
        }
        catch (FileNotFoundException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (JsonException exception)
        {
            Context.Output.Write(
                "Agent",
                "Latest session report is invalid JSON.",
                OutputSeverity.Warning,
                "Junexis.Mcp.Studio",
                exception.ToString());
        }
    }

    /// <summary>
    /// Zatrzymuje hosta i zamyka rozszerzenia.
    /// </summary>
    public void Shutdown()
    {
        if (_state == HostState.Created)
        {
            Context.Output.Write("Host", "Shutdown skipped (not started)");
            _state = HostState.Shutdown;
            return;
        }

        if (_state == HostState.Shutdown)
        {
            Context.Output.Write("Host", "Shutdown skipped (already shutdown)");
            return;
        }

        Context.Output.Write("Host", "Shutting down...");
        _loader.ShutdownAll(Context);
        _agentSessionWatcher?.Dispose();
        _agentRefreshTimer?.Dispose();
        _agentEventTailer?.Dispose();
        _agentEventTimer?.Dispose();
        _agentReportPanel?.Dispose();

        if (Context.Workspace is IDisposable disposableWorkspace)
        {
            disposableWorkspace.Dispose();
        }

        if (Context.OpenDocuments is IDisposable disposableOpenDocuments)
        {
            disposableOpenDocuments.Dispose();
        }

        _state = HostState.Shutdown;
        Context.Output.Write("Host", "Shutdown complete");
    }

    private void OnWorkspaceFolderOpened(string path)
    {
        if (_state == HostState.Shutdown)
        {
            return;
        }

        ConfigureAgentSessionWatcher(path);
        RefreshLatestAgentSession();
    }

    private void ConfigureAgentSessionWatcher(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return;
        }

        _agentSessionWatcher?.Dispose();
        _agentSessionWatcher = new FileSystemWatcher(Path.GetFullPath(workspaceRoot), "*.json")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        _agentSessionWatcher.Created += OnAgentSessionFileChanged;
        _agentSessionWatcher.Changed += OnAgentSessionFileChanged;
        _agentSessionWatcher.Renamed += OnAgentSessionFileRenamed;
    }

    private void ConfigureAgentEventTailer(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return;
        _agentEventTailer?.Dispose();
        _agentEventTailer = new JsonlAgentEventTailer(
            Path.Combine(Path.GetFullPath(workspaceRoot), ".junexis", "agent-events.jsonl"),
            [new AgentEventOutputConsumer(Context.Output)]);
        _agentEventTimer?.Dispose();
        _agentEventTimer = new Timer(
            _ => _ = _agentEventTailer.ReadNewAsync(CancellationToken.None),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(250));
    }

    private void OnAgentSessionFileChanged(object sender, FileSystemEventArgs args)
    {
        if (args.FullPath.Contains(
                Path.Combine(".junexis", "sessions"),
                StringComparison.OrdinalIgnoreCase))
        {
            ScheduleAgentRefresh();
        }
    }

    private void OnAgentSessionFileRenamed(object sender, RenamedEventArgs args) =>
        OnAgentSessionFileChanged(sender, args);

    private void ScheduleAgentRefresh()
    {
        _agentRefreshTimer ??= new Timer(
            _ => RefreshLatestAgentSession(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _agentRefreshTimer.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
    }

    private static void RegisterBuiltInCommands(IHostContext context)
    {
        context.Commands.Register("app.ping", () => context.Output.Write("Core", "pong"), "Help/Ping");

        // --- Build stop ---

        context.Commands.Register(
            "build.stop",
            () =>
            {
                // Anuluje buildy we wszystkich extensions które go obsługują
                foreach (var stopId in new[] { "build.gpp.stop", "build.csharp.stop", "build.go.stop", "build.jnx.stop", "run.jnx.stop", "debug.session.stop" })
                {
                    if (context.Commands.CanExecute(stopId))
                        context.Commands.Execute(stopId);
                }
            },
            menuPath: "Build/Stop Build",
            displayName: "Stop Build",
            category: "Build");

        // --- Project build ---

        context.Commands.Register(
            "project.build",
            () =>
            {
                var active = context.ActiveDocument.ActivePath;
                var ext = string.IsNullOrWhiteSpace(active)
                    ? null
                    : Path.GetExtension(active).ToLowerInvariant();

                var commandId = ext switch
                {
                    ".cpp" or ".cc" or ".cxx" or ".c" => "build.gpp",
                    ".cs" or ".csproj" or ".sln" => "build.csharp",
                    ".go" => "build.go",
                    ".jnx" => "build.jnx",
                    ".ts" => "build.typescript",
                    _ => null
                };

                if (commandId is null)
                {
                    context.Output.Write("Build",
                        "project.build: brak aktywnego pliku lub nieobsługiwany język. Użyj build.gpp / build.csharp / build.go / build.jnx.");
                    return;
                }

                if (!context.Commands.CanExecute(commandId))
                {
                    context.Output.Write("Build", $"project.build: komenda '{commandId}' niedostępna.");
                    return;
                }

                context.Commands.Execute(commandId);
            },
            menuPath: "Build/Build",
            displayName: "Build",
            category: "Build");

        // --- Project run ---

        context.Commands.Register(
            "project.run",
            () =>
            {
                var active = context.ActiveDocument.ActivePath;
                var ext = string.IsNullOrWhiteSpace(active)
                    ? null
                    : Path.GetExtension(active).ToLowerInvariant();

                var commandId = ext switch
                {
                    ".cpp" or ".cc" or ".cxx" or ".c" => "run.gpp",
                    ".cs" or ".csproj" or ".sln" => "run.csharp",
                    ".go" => "run.go",
                    ".jnx" => "run.jnx",
                    ".ts" => "run.typescript",
                    _ => null
                };

                if (commandId is null)
                {
                    context.Output.Write("Build",
                        "project.run: brak aktywnego pliku lub nieobsługiwany język. Użyj run.gpp / run.csharp / run.go.");
                    return;
                }

                if (!context.Commands.CanExecute(commandId))
                {
                    context.Output.Write("Build", $"project.run: komenda '{commandId}' niedostępna.");
                    return;
                }

                context.Commands.Execute(commandId);
            },
            menuPath: "Build/Run",
            displayName: "Run",
            category: "Build");

        // --- Preferences commands ---

        context.Commands.Register(
            "preferences.open",
            () => context.EventBus.Publish(new OpenPreferencesRequested(IsWorkspace: false)),
            menuPath: "File/Preferences",
            displayName: "Preferences",
            category: "File");

        context.Commands.Register(
            "preferences.openWorkspace",
            () =>
            {
                if (context.Preferences.WorkspaceFilePath is null)
                {
                    context.Output.Write("Preferences", "Brak otwartego workspace — nie można otworzyć preferencji workspace.",
                        OutputSeverity.Warning);
                    return;
                }

                context.EventBus.Publish(new OpenPreferencesRequested(IsWorkspace: true));
            },
            menuPath: "File/Workspace Preferences",
            displayName: "Workspace Preferences",
            category: "File");
    }

    private static void RegisterBuiltInKeybindings(KeybindingRegistry keybindings)
    {
        keybindings.Bind(new KeyGesture("P", KeyModifiers.Ctrl | KeyModifiers.Shift), "app.ping");
        keybindings.Bind(new KeyGesture("S", KeyModifiers.Ctrl), "editor.save");
        keybindings.Bind(new KeyGesture("W", KeyModifiers.Ctrl), "editor.closeActiveTab");
        keybindings.Bind(new KeyGesture("F4", KeyModifiers.Ctrl), "editor.closeActiveTab");
        keybindings.Bind(new KeyGesture("Tab", KeyModifiers.Ctrl), "editor.nextTab");
        keybindings.Bind(new KeyGesture("Tab", KeyModifiers.Ctrl | KeyModifiers.Shift), "editor.previousTab");
        keybindings.Bind(new KeyGesture("PageDown", KeyModifiers.Ctrl), "editor.nextTab");
        keybindings.Bind(new KeyGesture("PageUp", KeyModifiers.Ctrl), "editor.previousTab");
        keybindings.Bind(new KeyGesture("Z", KeyModifiers.Ctrl), "editor.undo");
        keybindings.Bind(new KeyGesture("Y", KeyModifiers.Ctrl), "editor.redo");
        keybindings.Bind(new KeyGesture("Z", KeyModifiers.Ctrl | KeyModifiers.Shift), "editor.redo");
        keybindings.Bind(new KeyGesture("X", KeyModifiers.Ctrl), "editor.cut");
        keybindings.Bind(new KeyGesture("C", KeyModifiers.Ctrl), "editor.copy");
        keybindings.Bind(new KeyGesture("V", KeyModifiers.Ctrl), "editor.paste");
        keybindings.Bind(new KeyGesture("A", KeyModifiers.Ctrl), "editor.selectAll");
        keybindings.Bind(new KeyGesture("L", KeyModifiers.Ctrl), "editor.selectLine");
        keybindings.Bind(new KeyGesture("G", KeyModifiers.Ctrl), "editor.goToLine");
        keybindings.Bind(new KeyGesture("F", KeyModifiers.Ctrl), "editor.toggleSearch");
        keybindings.Bind(new KeyGesture("F", KeyModifiers.Ctrl | KeyModifiers.Shift), "view.search");
        keybindings.Bind(new KeyGesture("H", KeyModifiers.Ctrl), "editor.openReplace");
        keybindings.Bind(new KeyGesture("F3"), "editor.findNext");
        keybindings.Bind(new KeyGesture("F3", KeyModifiers.Shift), "editor.findPrev");
        keybindings.Bind(new KeyGesture("F5"), "project.run");
        keybindings.Bind(new KeyGesture("F6"), "project.build");
        keybindings.Bind(new KeyGesture("F5", KeyModifiers.Shift), "build.stop");
    }

    private static void RegisterBuiltInPanels(IHostContext context)
    {
        context.Panels.RegisterToolPanel("explorer", () => "Explorer (placeholder)");
        context.Panels.RegisterDocumentPanel("editor", () => "Editor (placeholder)");
    }
}
