using System.Diagnostics;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

/// <summary>Opt-in end-to-end attach coverage for live C++, Go, and Node processes.</summary>
public sealed class RealAttachDapSmokeTests
{
    /// <summary>Attaches the real profile adapter and verifies that disconnect leaves the observed process alive.</summary>
    /// <param name="profileId">Studio debugger profile.</param>
    /// <param name="rootVariable">Environment variable containing the corresponding Junexis build root.</param>
    /// <param name="pidVariable">Environment variable containing an already running process identifier.</param>
    [Theory]
    [InlineData("cpp", "JUNEXIS_REAL_CPP_ATTACH_ROOT", "JUNEXIS_REAL_CPP_ATTACH_PID")]
    [InlineData("go", "JUNEXIS_REAL_GO_ATTACH_ROOT", "JUNEXIS_REAL_GO_ATTACH_PID")]
    [InlineData("typescript", "JUNEXIS_REAL_JS_ATTACH_ROOT", "JUNEXIS_REAL_JS_ATTACH_PID")]
    [Trait("Category", "Integration")]
    public async Task RunningJunexisProcess_AttachesAndDisconnectsWithoutTermination(
        string profileId,
        string rootVariable,
        string pidVariable)
    {
        var root = Environment.GetEnvironmentVariable(rootVariable);
        var pidText = Environment.GetEnvironmentVariable(pidVariable);
        if (string.IsNullOrWhiteSpace(root) || !int.TryParse(pidText, out var pid))
            return;

        Assert.True(Directory.Exists(root), $"Attach build root from {rootVariable} does not exist: '{root}'.");
        using var process = Process.GetProcessById(pid);
        Assert.False(process.HasExited, $"Attach process {pid} is not running.");
        var workspace = new WorkspaceService();
        workspace.OpenFolder(root);
        var capabilities = new CapabilityRegistry();
        capabilities.Register<IBreakpointService>(new BreakpointService());
        var context = new HostContext(
            new EventBus(), new CommandRegistry(), new PanelRegistry(), new OutputService(),
            new TestProcessService(), workspace, new DiagnosticService(), new ActiveDocumentService(),
            new TestOpenDocumentRepository(), null, new TestViewRegistry(), capabilities: capabilities);
        var selected = new DebugLaunchConfiguration(
            $"real-attach-{profileId}", $"Real attach {profileId}", profileId,
            DebugConfigurationRequest.Attach, ProcessId: pid, WorkingDirectory: root);
        var configuration = DapDebuggerExtension.CreateConfiguration(context, selected, new DebugAdapterRegistry());
        await using var session = new DapDebugSessionService();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await session.StartAsync(configuration, timeout.Token);
        Assert.Contains(session.Snapshot.State, new[] { DebugSessionState.Running, DebugSessionState.Stopped });
        await session.StopAsync(timeout.Token);

        process.Refresh();
        Assert.False(process.HasExited, "Disconnect from an attach session must not terminate the observed process.");
    }
}
