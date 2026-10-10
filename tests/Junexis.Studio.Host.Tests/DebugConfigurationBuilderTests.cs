using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class DebugConfigurationBuilderTests
{
    [Theory]
    [InlineData("cpp", "cppdbg")]
    [InlineData("go", "go")]
    [InlineData("typescript", "pwa-node")]
    public void CreateConfiguration_BuildsAttachArgumentsForProfile(string profileId, string adapterType)
    {
        var root = Directory.CreateTempSubdirectory("junexis-attach-config-");
        try
        {
            var workspace = new WorkspaceService();
            workspace.OpenFolder(root.FullName);
            var capabilities = new CapabilityRegistry();
            capabilities.Register<IBreakpointService>(new BreakpointService());
            capabilities.Register<INarrativeRuntimeEnvironmentService>(
                new FixedNarrativeRuntimeEnvironmentService());
            var context = new HostContext(
                new EventBus(), new CommandRegistry(), new PanelRegistry(), new OutputService(),
                new TestProcessService(), workspace, new DiagnosticService(), new ActiveDocumentService(),
                new TestOpenDocumentRepository(), null, new TestViewRegistry(), capabilities: capabilities);
            var selected = new DebugLaunchConfiguration(
                $"attach-{profileId}", $"Attach {profileId}", profileId,
                DebugConfigurationRequest.Attach, ProcessId: 4242, WorkingDirectory: ".");

            var configuration = DapDebuggerExtension.CreateConfiguration(
                context, selected, new StubAdapterRegistry(profileId, adapterType));

            Assert.Equal("attach", configuration.LaunchArguments["request"]);
            Assert.Equal(4242, configuration.LaunchArguments["processId"]);
            Assert.Equal(adapterType, configuration.LaunchArguments["type"]);
            Assert.False(configuration.LaunchArguments.ContainsKey("program"));
            Assert.False(configuration.LaunchArguments.ContainsKey("env"));
            if (profileId == "go")
                Assert.Equal("local", configuration.LaunchArguments["mode"]);
            if (profileId == "cpp")
                Assert.Equal("gdb", configuration.LaunchArguments["MIMode"]);
            if (profileId == "typescript")
                Assert.True((bool)configuration.LaunchArguments["sourceMaps"]!);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [Trait("Issue", "26")]
    [InlineData("cpp", "cppdbg")]
    [InlineData("go", "go")]
    [InlineData("typescript", "pwa-node")]
    public void CreateConfiguration_Launch_AttachesManagedNarrativeEnvironment(
        string profileId,
        string adapterType)
    {
        var root = Directory.CreateTempSubdirectory("junexis-launch-config-");
        try
        {
            var workspace = new WorkspaceService();
            workspace.OpenFolder(root.FullName);
            var capabilities = new CapabilityRegistry();
            capabilities.Register<IBreakpointService>(new BreakpointService());
            capabilities.Register<INarrativeRuntimeEnvironmentService>(
                new FixedNarrativeRuntimeEnvironmentService());
            var context = new HostContext(
                new EventBus(), new CommandRegistry(), new PanelRegistry(), new OutputService(),
                new TestProcessService(), workspace, new DiagnosticService(), new ActiveDocumentService(),
                new TestOpenDocumentRepository(), null, new TestViewRegistry(), capabilities: capabilities);

            var configuration = DapDebuggerExtension.CreateConfiguration(
                context,
                new DebugLaunchConfiguration(
                    $"launch-{profileId}",
                    $"Launch {profileId}",
                    profileId,
                    DebugConfigurationRequest.Launch),
                new StubAdapterRegistry(profileId, adapterType));

            var environment = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
                configuration.LaunchArguments["env"]);
            Assert.Equal("http://127.0.0.1:43125",
                environment[NarrativeRuntimeEnvironment.TraceUrlVariable]);
            Assert.Equal("build-42",
                environment[NarrativeRuntimeEnvironment.BuildIdVariable]);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Issue", "27")]
    public async Task PrepareDebugAsync_LaunchWaitsButAttachDoesNot()
    {
        var root = Directory.CreateTempSubdirectory("junexis-debug-prepare-");
        try
        {
            var workspace = new WorkspaceService();
            workspace.OpenFolder(root.FullName);
            var narrative = new PreparingNarrativeRuntimeEnvironmentService();
            var capabilities = new CapabilityRegistry();
            capabilities.Register<INarrativeRuntimeEnvironmentService>(narrative);
            var context = new HostContext(
                new EventBus(), new CommandRegistry(), new PanelRegistry(), new OutputService(),
                new TestProcessService(), workspace, new DiagnosticService(), new ActiveDocumentService(),
                new TestOpenDocumentRepository(), null, new TestViewRegistry(), capabilities: capabilities);

            await NarrativeRuntimeLaunch.PrepareDebugAsync(
                context,
                DebugConfigurationRequest.Attach);
            Assert.False(narrative.WaitStarted.Task.IsCompleted);

            var launch = NarrativeRuntimeLaunch.PrepareDebugAsync(
                context,
                DebugConfigurationRequest.Launch);
            await narrative.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(launch.IsCompleted);
            narrative.Complete();
            await launch;
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class FixedNarrativeRuntimeEnvironmentService : INarrativeRuntimeEnvironmentService
    {
        public bool IsPreparationPending => false;
        public NarrativeRuntimeEnvironment? GetCurrent() =>
            new("http://127.0.0.1:43125", "build-42");
        public Task<NarrativeRuntimeEnvironment?> WaitForCurrentAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GetCurrent());
    }

    private sealed class PreparingNarrativeRuntimeEnvironmentService
        : INarrativeRuntimeEnvironmentService
    {
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _isReady;

        public TaskCompletionSource WaitStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsPreparationPending => !_isReady;
        public NarrativeRuntimeEnvironment? GetCurrent() =>
            _isReady ? new("http://127.0.0.1:43125", "build-42") : null;

        public async Task<NarrativeRuntimeEnvironment?> WaitForCurrentAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            WaitStarted.TrySetResult();
            await _ready.Task.WaitAsync(timeout, cancellationToken);
            return GetCurrent();
        }

        public void Complete()
        {
            _isReady = true;
            _ready.TrySetResult();
        }
    }

    private sealed class StubAdapterRegistry(string profileId, string adapterType) : IDebugAdapterRegistry
    {
        public DebugAdapterDescriptor? Resolve(string id) => id == profileId
            ? new(profileId, "Test adapter", "adapter", string.Empty,
                DebugAdapterTransport.Stdio, adapterType, "test")
            : null;

        public IReadOnlyList<DebugAdapterDescriptor> GetAvailableAdapters() => [];
    }
}
