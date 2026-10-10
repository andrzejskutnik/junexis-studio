using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class ToolchainDoctorServiceTests
{
    [Fact]
    public async Task CheckAsync_ReportsSuccessfulAndMissingToolsIndependently()
    {
        var process = new ScriptedProcessService(request => request.FileName switch
        {
            "dotnet" => (0, "10.0.100"),
            _ => throw new System.ComponentModel.Win32Exception("program not found"),
        });
        var probes = new[]
        {
            new ToolchainDoctorService.ToolProbe("dotnet", ".NET SDK", "C#", "dotnet", "--version"),
            new ToolchainDoctorService.ToolProbe("cpp", "GNU C++", "C++", "g++", "--version"),
        };
        var service = new ToolchainDoctorService(process, probes, TimeSpan.FromSeconds(1));

        var snapshot = await service.CheckAsync();

        Assert.Equal(2, snapshot.Capabilities.Count);
        Assert.True(snapshot.Capabilities[0].IsAvailable);
        Assert.Equal("10.0.100", snapshot.Capabilities[0].Version);
        Assert.False(snapshot.Capabilities[1].IsAvailable);
        Assert.Contains("program not found", snapshot.Capabilities[1].Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_ReportsProbeTimeout()
    {
        var process = new BlockingProcessService();
        var probe = new ToolchainDoctorService.ToolProbe("go", "Go", "Go", "go", "version");
        var service = new ToolchainDoctorService(process, [probe], TimeSpan.FromMilliseconds(20));

        var snapshot = await service.CheckAsync();

        var capability = Assert.Single(snapshot.Capabilities);
        Assert.False(capability.IsAvailable);
        Assert.Contains("timed out", capability.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAsync_UpdatesPanelAndPublishesSnapshot()
    {
        var expected = new ToolchainDoctorSnapshot(
            DateTimeOffset.UtcNow,
            [new ToolchainCapability("go", "Go", "Go", "go version", true, "go1.25", "Ready.")]);
        var eventBus = new EventBus();
        ToolchainCapabilitiesUpdatedEvent? published = null;
        eventBus.Subscribe<ToolchainCapabilitiesUpdatedEvent>(value => published = value);
        var viewModel = new ToolchainDoctorViewModel(
            new StubDoctorService(expected),
            eventBus,
            new OutputService());

        await viewModel.RefreshAsync();

        Assert.Same(expected, published?.Snapshot);
        Assert.Single(viewModel.Capabilities);
        Assert.Contains("1/1", viewModel.StatusText, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    private sealed class ScriptedProcessService(
        Func<ProcessRequest, (int ExitCode, string Output)> handler) : IProcessService
    {
        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            var response = handler(request);
            listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, response.Output));
            return Task.FromResult(new ProcessResult(response.ExitCode, TimeSpan.Zero));
        }
    }

    private sealed class BlockingProcessService : IProcessService
    {
        public async Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ProcessResult(0, TimeSpan.Zero);
        }
    }

    private sealed class StubDoctorService(ToolchainDoctorSnapshot snapshot) : IToolchainDoctorService
    {
        public ToolchainDoctorSnapshot? LatestSnapshot => snapshot;

        public Task<ToolchainDoctorSnapshot> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }
}
