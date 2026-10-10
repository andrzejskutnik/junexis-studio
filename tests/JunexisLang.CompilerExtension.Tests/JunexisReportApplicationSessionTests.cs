using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace JunexisLang.Studio.CompilerExtension.Tests;

public sealed class JunexisReportApplicationSessionTests
{
    /// <summary>Verifies the session adapter against an explicitly supplied local CLI and laboratory.</summary>
    [PanelIntegrationFact]
    public async Task RealCli_PublishesLaboratoryPanelAndStopsOwnedProcesses()
    {
        var cli = Environment.GetEnvironmentVariable("JUNEXIS_TEST_CLI")!;
        var project = Environment.GetEnvironmentVariable("JUNEXIS_TEST_PROJECT")!;
        using var fixture = await TestFixture.CreateAsync();
        var processes = new ProcessService();
        var options = new JunexisCompilerOptions { CliPath = cli };
        var opened = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new JunexisReportApplicationSession(options, processes,
            new JunexisLangCompilerService(options), fixture.Context, url => opened.TrySetResult(url));
        var run = session.StartAsync(project, Path.Combine(project, "src", "main.jnx"));
        try
        {
            var url = await opened.Task.WaitAsync(TimeSpan.FromMinutes(3));
            Assert.Equal("/laboratory/", url.AbsolutePath);
            using var http = new HttpClient();
            using var response = await http.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode);
            var output = new System.Text.StringBuilder();
            var verification = await processes.RunAsync(new ProcessRequest("pwsh",
                ["-NoProfile", "-File", Path.Combine(project, "verification", "verify-running-laboratory.ps1"),
                 "-BaseUrl", url.GetLeftPart(UriPartial.Authority)], project),
                line => output.AppendLine(line.Text), CancellationToken.None);
            Assert.Equal(0, verification.ExitCode);
            session.Stop();
            await run.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Null(session.Current.Url);
            Assert.Equal(ReportApplicationState.Idle, session.Current.State);
            await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(url));
            var evidence = Environment.GetEnvironmentVariable("JUNEXIS_TEST_EVIDENCE");
            if (evidence is not null)
                await File.WriteAllTextAsync(evidence, System.Text.Json.JsonSerializer.Serialize(new
                {
                    date = "2026-09-25", adapter = "Studio application session", realCli = true,
                    realProcessStop = true, panelHttpStatus = 200, closedAfterStop = true, uiVerified = false,
                    laboratory = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(output.ToString())
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            session.Stop();
            await run.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private sealed class PanelIntegrationFactAttribute : FactAttribute
    {
        /// <summary>Enables the real-process test only when its explicit inputs are configured.</summary>
        public PanelIntegrationFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JUNEXIS_TEST_CLI"))
                || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JUNEXIS_TEST_PROJECT")))
                Skip = "Set JUNEXIS_TEST_CLI and JUNEXIS_TEST_PROJECT to verify real local processes.";
        }
    }

    /// <summary>Accepts both versioned panel kinds and excludes child log records.</summary>
    [Theory]
    [InlineData("server")]
    [InlineData("report")]
    public void PanelReady_AcceptsVersionedLocalPanel(string kind)
    {
        var line = "JUNEXIS_READY " + System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = "junexis.panel-ready/1", sessionId = Guid.NewGuid().ToString("D"), kind,
            url = "http://127.0.0.1:12345/custom-panel/"
        });
        Assert.Equal("/custom-panel/", JunexisReportApplicationSession.ParsePanelReady(line)!.AbsolutePath);
        Assert.Null(JunexisReportApplicationSession.ParsePanelReady("[server] " + line));
    }

    /// <summary>Rejects malformed control records and nonlocal URLs.</summary>
    [Theory]
    [InlineData("READY report · https://example.com/")]
    [InlineData("READY report · http://user@localhost/")]
    [InlineData("READY report · file:///tmp/panel")]
    [InlineData("JUNEXIS_READY {}")]
    [InlineData("JUNEXIS_READY null")]
    [InlineData("JUNEXIS_READY {broken")]
    public void PanelReady_RejectsInvalidOrExternalAddress(string line) =>
        Assert.Null(JunexisReportApplicationSession.ParsePanelReady(line));

    public static TheoryData<string, string, string> RestoreFailureCases => new()
    {
        { "Brakuje dostawcy 'postgresql@1.0.0/sdk:0.1' w profilu offline.", "JXR001", "Połącz się z siecią" },
        { "Operacja Registry przekroczyła limit czasu.", "JXR002", "Sprawdź połączenie" },
        { "Registry zwróciło HTTP 429 (rate-limited).", "JXR003", "Odczekaj czas" },
        { "Registry zwróciło HTTP 413 (payload-too-large).", "JXR004", "Sprawdź manifest" },
        { "Registry zwróciło HTTP 409 (artifact-conflict).", "JXR005", "Wybierz inną wersję" },
        { "Dostawca 'postgresql@1.0.0' nie obsługuje SDK '0.2.0'.", "JXR006", "Dopasuj providerVersion" }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Milestone", "M10.9")]
    public async Task StartAsync_ReadyReport_PublishesUrlOpensBrowserAndStopsOwnedProcess(bool versioned)
    {
        using var fixture = await TestFixture.CreateAsync();
        var process = new ReadyBlockingProcessService(versioned);
        var opened = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var openCount = 0;
        using var session = new JunexisReportApplicationSession(
            new JunexisCompilerOptions { CliPath = "junexis" },
            process,
            new StubPreflightService(success: true),
            fixture.Context,
            url => { openCount++; opened.TrySetResult(url); });

        var run = session.StartAsync(fixture.Root, fixture.Entry);
        var url = await opened.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("http://127.0.0.1:4173/?server=http%3A%2F%2F127.0.0.1%3A5080", url.AbsoluteUri);
        Assert.Equal(ReportApplicationState.Ready, session.Current.State);
        Assert.Contains("--no-open", process.Request!.ArgumentList!);
        Assert.Equal("junexis", process.Request.FileName);

        session.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ReportApplicationState.Idle, session.Current.State);
        Assert.True(process.WasCancelled);
        Assert.Null(session.Current.Url);
        Assert.Equal(1, openCount);
    }

    [Fact]
    [Trait("Milestone", "M10.9")]
    public async Task StartAsync_InvalidView_PublishesProblemAndDoesNotStartRuntime()
    {
        using var fixture = await TestFixture.CreateAsync();
        var diagnostic = new Diagnostic(
            DiagnosticSeverity.Error,
            "invalid user/report/view.json: unknown widget",
            Path.Combine(fixture.Root, "user", "report", "view.json"),
            source: "junexis-lang",
            code: "B0005");
        var process = new RecordingProcessService();
        using var session = new JunexisReportApplicationSession(
            new JunexisCompilerOptions { CliPath = "junexis" },
            process,
            new StubPreflightService(success: false, diagnostic),
            fixture.Context,
            _ => throw new InvalidOperationException("Browser must not open."));

        await session.StartAsync(fixture.Root, fixture.Entry);

        Assert.Equal(ReportApplicationState.Failed, session.Current.State);
        Assert.Equal(0, process.CallCount);
        var published = Assert.Single(fixture.Diagnostics.GetBySource("junexis-lang"));
        Assert.Equal("B0005", published.Code);
        Assert.Contains("view.json", published.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Milestone", "M10.9")]
    public async Task Extension_RegistersApplicationPanelCommandsAndCapability()
    {
        using var fixture = await TestFixture.CreateAsync();
        var extension = new JunexisLangCompilerExtension(
            new JunexisCompilerOptions { CliPath = "junexis" });
        try
        {
            extension.Initialize(fixture.Context);

            Assert.True(fixture.Commands.CanExecute("run.jnx"));
            Assert.False(fixture.Commands.CanExecute("run.jnx.open"));
            Assert.Contains(fixture.Panels.GetRegisteredPanels(),
                panel => panel.Id == "junexis.report.application" && panel.Title == "Aplikacja");
            Assert.True(fixture.Capabilities.TryGet<IReportApplicationSession>(out var session));
            Assert.NotNull(session);
        }
        finally
        {
            extension.Shutdown();
        }
    }

    [Fact]
    [Trait("Milestone", "M16.11")]
    public async Task Extension_RestoreCommands_InvokePublicJunexisWithProjectOptions()
    {
        var process = new CapturingRestoreProcessService();
        using var fixture = await TestFixture.CreateAsync(process);
        var extension = new JunexisLangCompilerExtension(new JunexisCompilerOptions
        {
            CliPath = "private-compiler",
            PublicCliPath = "public-junexis",
            LocalPackagesPath = "C:\\Junexis\\release"
        });
        try
        {
            extension.Initialize(fixture.Context);

            Assert.True(fixture.Commands.CanExecute("restore.jnx"));
            Assert.True(fixture.Commands.CanExecute("restore.jnx.offline"));
            fixture.Commands.Execute("restore.jnx.offline");
            var request = await process.Request.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("public-junexis", request.FileName);
            Assert.Equal(fixture.Root, request.WorkingDirectory);
            Assert.Equal(
                ["restore", fixture.Root, "--offline"],
                request.ArgumentList);
        }
        finally
        {
            extension.Shutdown();
        }
    }

    [Fact]
    [Trait("Milestone", "M17.11.1")]
    public async Task Extension_RestoreFromRegistry_ReportsThreeSafeStagesAndDelegatesToPublicCli()
    {
        var process = new ReportingRestoreProcessService();
        using var fixture = await TestFixture.CreateAsync(process);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Output.EntryWritten += entry =>
        {
            if (entry.Channel == "Restore" && entry.Message.StartsWith("Etap 3/3", StringComparison.Ordinal))
                completed.TrySetResult();
        };
        var extension = new JunexisLangCompilerExtension(new JunexisCompilerOptions
        {
            PublicCliPath = "public-junexis",
            RegistryBaseAddress = "https://registry.example.test/releases/",
            LocalPackagesPath = "C:\\Junexis\\release"
        });
        try
        {
            extension.Initialize(fixture.Context);
            fixture.Commands.Execute("restore.jnx");
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("public-junexis", process.Request!.FileName);
            Assert.Equal(fixture.Root, process.Request.WorkingDirectory);
            Assert.Equal(
                ["restore", fixture.Root, "--registry", "https://registry.example.test/releases"],
                process.Request.ArgumentList);

            var messages = fixture.Output.GetSnapshot()
                .Where(entry => entry.Channel == "Restore")
                .Select(entry => entry.Message)
                .ToArray();
            Assert.Contains("Etap 1/3 · przygotowanie · źródło: Registry.", messages);
            Assert.Contains("Etap 2/3 · pobieranie i weryfikacja zależności.", messages);
            Assert.Contains("Etap 3/3 · zależności są gotowe.", messages);
            Assert.DoesNotContain(messages, message => message.Contains("registry.example.test", StringComparison.Ordinal));
        }
        finally
        {
            extension.Shutdown();
        }
    }

    [Theory]
    [MemberData(nameof(RestoreFailureCases))]
    [Trait("Milestone", "M17.11.2")]
    public async Task Extension_RestoreFailure_PublishesSafeActionableDiagnostic(
        string processLine,
        string expectedCode,
        string expectedAction)
    {
        var process = new FailedRestoreProcessService(processLine);
        using var fixture = await TestFixture.CreateAsync(process);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Diagnostics.DiagnosticsChanged += () =>
        {
            if (fixture.Diagnostics.GetBySource("junexis-restore").Count > 0)
                published.TrySetResult();
        };
        var extension = new JunexisLangCompilerExtension(new JunexisCompilerOptions
        {
            PublicCliPath = "public-junexis",
            RegistryBaseAddress = "https://registry.example.test"
        });
        try
        {
            extension.Initialize(fixture.Context);
            fixture.Commands.Execute("restore.jnx");
            await published.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var diagnostic = Assert.Single(fixture.Diagnostics.GetBySource("junexis-restore"));
            Assert.Equal(expectedCode, diagnostic.Code);
            Assert.Contains(expectedAction, diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("junexis-compiler restore", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(fixture.Root, "junexis.project.toml"), diagnostic.FilePath);

            var advice = fixture.Output.GetSnapshot()
                .Last(entry => entry.Channel == "Restore" && entry.Message == diagnostic.Message);
            Assert.Equal(OutputSeverity.Error, advice.Severity);
        }
        finally
        {
            extension.Shutdown();
        }
    }

    [Fact]
    [Trait("Milestone", "M17.10.6")]
    public async Task Extension_RestoreOutput_RedactsCredentialsQueryAndUserPath()
    {
        const string token = "m17-token-super-secret-1234567890";
        const string querySecret = "m17-query-secret-0987654321";
        const string userName = "m17 private user";
        var line =
            $"Authorization: Bearer {token} request=https://registry.example/v1/snapshot?access_token={querySecret} path=C:\\Users\\{userName}\\project\\junexis.project.toml";
        var process = new FailedRestoreProcessService(line);
        using var fixture = await TestFixture.CreateAsync(process);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Output.EntryWritten += entry =>
        {
            if (entry.Channel == "Restore" && entry.Message.StartsWith("Etap 3/3", StringComparison.Ordinal))
                completed.TrySetResult();
        };
        var extension = new JunexisLangCompilerExtension(new JunexisCompilerOptions
        {
            PublicCliPath = "public-junexis",
            RegistryBaseAddress = "https://registry.example"
        });
        try
        {
            extension.Initialize(fixture.Context);
            fixture.Commands.Execute("restore.jnx");
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var output = string.Join(
                Environment.NewLine,
                fixture.Output.GetSnapshot()
                    .Where(entry => entry.Channel == "Restore")
                    .Select(entry => entry.Message));
            Assert.DoesNotContain(token, output, StringComparison.Ordinal);
            Assert.DoesNotContain(querySecret, output, StringComparison.Ordinal);
            Assert.DoesNotContain(userName, output, StringComparison.Ordinal);
            Assert.Contains("[USUNIĘTO_TOKEN]", output, StringComparison.Ordinal);
            Assert.Contains("[USUNIĘTO_PARAMETRY]", output, StringComparison.Ordinal);
            Assert.Contains("[USUNIĘTO_ŚCIEŻKĘ_UŻYTKOWNIKA]", output, StringComparison.Ordinal);
        }
        finally
        {
            extension.Shutdown();
        }
    }

    [Fact]
    [Trait("Milestone", "M16.11")]
    public async Task Extension_Restore_AllowsSingleProcessAndCancelsItOnShutdown()
    {
        var process = new BlockingRestoreProcessService();
        using var fixture = await TestFixture.CreateAsync(process);
        var extension = new JunexisLangCompilerExtension(
            new JunexisCompilerOptions { PublicCliPath = "public-junexis" });
        extension.Initialize(fixture.Context);

        fixture.Commands.Execute("restore.jnx");
        await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Commands.Execute("restore.jnx.offline");

        Assert.Equal(1, process.CallCount);
        Assert.False(fixture.Commands.CanExecute("restore.jnx"));

        extension.Shutdown();
        await process.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    [Trait("Milestone", "M17.11.3")]
    public async Task Extension_Restore_CancelThenRetryLeavesConsistentState()
    {
        var process = new CancelThenSuccessRestoreProcessService();
        using var fixture = await TestFixture.CreateAsync(process);
        var retrySucceeded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Output.EntryWritten += entry =>
        {
            if (entry.Channel == "Restore" && entry.Message == "Etap 3/3 · zależności są gotowe.")
                retrySucceeded.TrySetResult();
        };
        var extension = new JunexisLangCompilerExtension(
            new JunexisCompilerOptions { PublicCliPath = "public-junexis" });

        extension.Initialize(fixture.Context);
        fixture.Commands.Execute("restore.jnx");
        await process.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        extension.Shutdown();
        await process.FirstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        extension.Initialize(fixture.Context);
        fixture.Commands.Execute("restore.jnx");
        await retrySucceeded.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (var attempt = 0; attempt < 100 && !fixture.Commands.CanExecute("restore.jnx"); attempt++)
            await Task.Delay(10);

        Assert.Equal(2, process.CallCount);
        Assert.True(fixture.Commands.CanExecute("restore.jnx"));
        Assert.Empty(fixture.Diagnostics.GetBySource("junexis-restore"));

        extension.Shutdown();
    }

    [Fact]
    [Trait("Milestone", "M10.9")]
    public async Task StartAsync_AdapterBuildFails_PublishesRuntimeProblem()
    {
        using var fixture = await TestFixture.CreateAsync();
        using var session = new JunexisReportApplicationSession(
            new JunexisCompilerOptions { CliPath = "junexis" },
            new FailedAdapterProcessService(),
            new StubPreflightService(success: true),
            fixture.Context,
            _ => throw new InvalidOperationException("Browser must not open."));

        await session.StartAsync(fixture.Root, fixture.Entry);

        Assert.Equal(ReportApplicationState.Failed, session.Current.State);
        var diagnostic = Assert.Single(
            fixture.Diagnostics.GetBySource("junexis-report-runtime"));
        Assert.Equal("RPT001", diagnostic.Code);
        Assert.Contains("adapter.ts", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("TS2322", diagnostic.Message, StringComparison.Ordinal);
    }

    private sealed class StubPreflightService(
        bool success,
        Diagnostic? diagnostic = null) : ICompilerPreflightService
    {
        public Task<CompilerPreflightResult> PreflightAsync(
            BuildContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompilerPreflightResult(
                success,
                diagnostic is null ? [] : [diagnostic],
                "junexis-lang",
                success ? new string('a', 64) : null,
                ProjectRoot: context.WorkspaceRoot));

        public Task<CompilerResult> BuildAfterPreflightAsync(
            BuildContext context,
            CompilerPreflightResult preflight,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingProcessService : IProcessService
    {
        public int CallCount { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
        }
    }

    private sealed class CapturingRestoreProcessService : IProcessService
    {
        public TaskCompletionSource<ProcessRequest> Request { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            Request.TrySetResult(request);
            return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
        }
    }

    private sealed class ReportingRestoreProcessService : IProcessService
    {
        public ProcessRequest? Request { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            Request = request;
            listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "Zależności zweryfikowane."));
            return Task.FromResult(new ProcessResult(0, TimeSpan.FromMilliseconds(10)));
        }
    }

    private sealed class FailedRestoreProcessService(string line) : IProcessService
    {
        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdErr, line));
            return Task.FromResult(new ProcessResult(1, TimeSpan.FromMilliseconds(10)));
        }
    }

    private sealed class BlockingRestoreProcessService : IProcessService
    {
        public int CallCount { get; private set; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new ProcessResult(0, TimeSpan.Zero);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class CancelThenSuccessRestoreProcessService : IProcessService
    {
        public int CallCount { get; private set; }
        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                FirstStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    FirstCancelled.TrySetResult();
                    throw;
                }
            }

            listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "Ponowienie zakończone powodzeniem."));
            return new ProcessResult(0, TimeSpan.FromMilliseconds(10));
        }
    }

    private sealed class ReadyBlockingProcessService(bool versioned = false) : IProcessService
    {
        public ProcessRequest? Request { get; private set; }
        public bool WasCancelled { get; private set; }

        public async Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            Request = request;
            if (versioned) listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut,
                "JUNEXIS_READY " + System.Text.Json.JsonSerializer.Serialize(new {
                    schema = "junexis.panel-ready/1", sessionId = Guid.NewGuid().ToString("D"), kind = "server",
                    url = "http://127.0.0.1:4173/?server=http%3A%2F%2F127.0.0.1%3A5080"
                })));
            listener?.Invoke(new ProcessOutputLine(
                ProcessOutputKind.StdOut,
                "READY report · http://127.0.0.1:4173/?server=http%3A%2F%2F127.0.0.1%3A5080"));
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new ProcessResult(0, TimeSpan.Zero);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }
    }

    private sealed class FailedAdapterProcessService : IProcessService
    {
        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            listener?.Invoke(new ProcessOutputLine(
                ProcessOutputKind.StdErr,
                "user/report/adapter.ts(8,5): error TS2322: Type 'string' is not assignable to type 'number'."));
            return Task.FromResult(new ProcessResult(1, TimeSpan.FromMilliseconds(10)));
        }
    }

    private sealed class TestFixture : IDisposable
    {
        private readonly WorkspaceService _workspace;
        private readonly OpenDocumentRepository _documents;

        private TestFixture(
            string root,
            string entry,
            WorkspaceService workspace,
            OpenDocumentRepository documents,
            HostContext context,
            CommandRegistry commands,
            PanelRegistry panels,
            CapabilityRegistry capabilities,
            DiagnosticService diagnostics,
            OutputService output)
        {
            Root = root;
            Entry = entry;
            _workspace = workspace;
            _documents = documents;
            Context = context;
            Commands = commands;
            Panels = panels;
            Capabilities = capabilities;
            Diagnostics = diagnostics;
            Output = output;
        }

        public string Root { get; }
        public string Entry { get; }
        public HostContext Context { get; }
        public CommandRegistry Commands { get; }
        public PanelRegistry Panels { get; }
        public CapabilityRegistry Capabilities { get; }
        public DiagnosticService Diagnostics { get; }
        public OutputService Output { get; }

        public static async Task<TestFixture> CreateAsync(IProcessService? processes = null)
        {
            var root = Path.Combine(Path.GetTempPath(), $"junexis-report-studio-{Guid.NewGuid():N}");
            var entry = Path.Combine(root, "src", "main.jnx");
            Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
            await File.WriteAllTextAsync(Path.Combine(root, "junexis.project.toml"),
                "entry = \"src/main.jnx\"\ntargets = [\"server\", \"report\"]\n");
            await File.WriteAllTextAsync(entry, "task Main() @server {}\n");

            var eventBus = new EventBus();
            var workspace = new WorkspaceService(eventBus);
            workspace.OpenFolder(root);
            var documents = new OpenDocumentRepository(eventBus, workspace);
            var active = new ActiveDocumentService();
            active.SetActivePath(entry);
            var commands = new CommandRegistry();
            var panels = new PanelRegistry();
            var capabilities = new CapabilityRegistry();
            var diagnostics = new DiagnosticService();
            var output = new OutputService();
            var context = new HostContext(
                eventBus,
                commands,
                panels,
                output,
                processes ?? new RecordingProcessService(),
                workspace,
                diagnostics,
                active,
                documents,
                versionControl: null,
                views: new NullViewRegistry(),
                capabilities: capabilities);
            return new TestFixture(
                root, entry, workspace, documents, context,
                commands, panels, capabilities, diagnostics, output);
        }

        public void Dispose()
        {
            _documents.Dispose();
            _workspace.Dispose();
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class NullViewRegistry : IViewRegistry
    {
        public void Register<TViewModel, TView>()
            where TViewModel : class
            where TView : class, new()
        {
        }

        public object? Resolve(object viewModel) => null;
    }
}
