using System.Net;
using System.Text;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class NarrativeSessionServiceTests
{
    [Fact]
    [Trait("Issue", "25")]
    public async Task StartAsync_BindsCollectorToVerifiedBuildAndSelectsLatestEvent()
    {
        using var fixture = new ReceiptFixture();
        var process = new FakeManagedNarrativeProcess();
        var handler = new NarrativeHttpHandler(latestEventId: 42, totalEvents: 5);
        await using var sut = CreateService(process, handler, port: 43125);

        await sut.StartAsync(fixture.Receipt);

        Assert.Equal(NarrativeSessionState.Ready, sut.Snapshot.State);
        Assert.Equal(fixture.Receipt.BuildId, sut.Snapshot.BuildId);
        Assert.Equal(fixture.Receipt.DebugManifestPath, sut.Snapshot.DebugManifestPath);
        Assert.Equal("http://127.0.0.1:43125", sut.Snapshot.BaseUrl);
        Assert.Equal((ulong)42, sut.Snapshot.LatestEventId);
        Assert.Equal(5, sut.Snapshot.TotalEvents);
        Assert.Equal(NarrativeRuntimeActivity.Live, sut.Snapshot.Activity);
        Assert.Equal(5, sut.Snapshot.EventDelta);
        Assert.NotNull(sut.Snapshot.LastEventObservedAt);
        Assert.NotNull(process.StartInfo);
        Assert.Contains("--manifest", process.StartInfo!.Arguments);
        Assert.Contains(fixture.Receipt.DebugManifestPath, process.StartInfo.Arguments);
        Assert.Contains("43125", process.StartInfo.Arguments);
        Assert.Contains(handler.Requests, uri =>
            uri.AbsolutePath == "/events/latest"
            && uri.Query.Contains(Uri.EscapeDataString(fixture.Receipt.BuildId), StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Issue", "25")]
    public async Task LatestBuildEvent_AutomaticallyStartsManagedSession()
    {
        using var fixture = new ReceiptFixture();
        var bus = new EventBus();
        var process = new FakeManagedNarrativeProcess();
        var ready = new TaskCompletionSource<NarrativeSessionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sut = CreateService(
            process,
            new NarrativeHttpHandler(latestEventId: null, totalEvents: 0),
            bus: bus);
        sut.SnapshotChanged += snapshot =>
        {
            if (snapshot.State == NarrativeSessionState.Ready)
                ready.TrySetResult(snapshot);
        };

        bus.Publish(new LatestBuildUpdatedEvent("junexis-lang", fixture.Receipt, Restored: false));
        var snapshot = await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(fixture.Receipt.BuildId, snapshot.BuildId);
        Assert.Null(snapshot.LatestEventId);
        Assert.Equal(NarrativeRuntimeActivity.WaitingForEvents, snapshot.Activity);
        Assert.Contains("waiting for runtime events", snapshot.Status, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "25")]
    public async Task StopAsync_TerminatesOnlyManagedProcessAndClearsSession()
    {
        using var fixture = new ReceiptFixture();
        var process = new FakeManagedNarrativeProcess();
        await using var sut = CreateService(
            process,
            new NarrativeHttpHandler(latestEventId: 7, totalEvents: 1));
        await sut.StartAsync(fixture.Receipt);

        await sut.StopAsync();

        Assert.Equal(1, process.StopCount);
        Assert.Equal(NarrativeSessionState.Inactive, sut.Snapshot.State);
        Assert.Null(sut.Snapshot.BaseUrl);
        Assert.Null(sut.Snapshot.BuildId);
    }

    [Fact]
    public void ResolveDevelopmentCollector_FindsInstalledPackageFromEnvironment()
    {
        var original = Environment.GetEnvironmentVariable("JUNEXIS_TRACE_COLLECTOR");
        var root = Path.Combine(Path.GetTempPath(), $"junexis-narrative-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "junexis-trace-collector.exe");
        File.WriteAllText(executable, "stub");
        try
        {
            Environment.SetEnvironmentVariable("JUNEXIS_TRACE_COLLECTOR", executable);

            Assert.Equal(executable, NarrativeSessionService.ResolveDevelopmentCollector());
        }
        finally
        {
            Environment.SetEnvironmentVariable("JUNEXIS_TRACE_COLLECTOR", original);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Host_RegistersManagedNarrativeSessionCapability()
    {
        var host = new JunexisStudioHost();

        Assert.True(host.Context.Capabilities.TryGet<INarrativeSessionService>(out var session));
        Assert.NotNull(session);
        Assert.True(host.Context.Capabilities.TryGet<INarrativeRuntimeEnvironmentService>(out var environment));
        Assert.NotNull(environment);
    }

    private static NarrativeSessionService CreateService(
        FakeManagedNarrativeProcess process,
        NarrativeHttpHandler handler,
        int port = 43125,
        EventBus? bus = null)
    {
        var output = new OutputService();
        return new NarrativeSessionService(
            bus ?? new EventBus(),
            output,
            "collector.dll",
            startInfo =>
            {
                process.StartInfo = startInfo;
                return process;
            },
            () => new HttpClient(handler, disposeHandler: false),
            () => port);
    }

    private sealed class FakeManagedNarrativeProcess : IManagedNarrativeProcess
    {
        public NarrativeCollectorStartInfo? StartInfo { get; set; }
        public bool HasExited { get; private set; }
        public int? ExitCode => HasExited ? 0 : null;
        public int StopCount { get; private set; }
        public event Action<int?>? Exited;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            HasExited = true;
            Exited?.Invoke(0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class NarrativeHttpHandler(ulong? latestEventId, long totalEvents) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath == "/health")
                return Json(HttpStatusCode.OK, $$"""{"status":"healthy","totalEvents":{{totalEvents}}}""");
            if (request.RequestUri.AbsolutePath == "/events/latest" && latestEventId is { } eventId)
                return Json(HttpStatusCode.OK, $$"""{"eventId":{{eventId}}}""");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ReceiptFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "junexis-narrative-session-" + Guid.NewGuid().ToString("N"));

        public ReceiptFixture()
        {
            Directory.CreateDirectory(_root);
            var manifest = Path.Combine(_root, "junexis.debug.toml");
            File.WriteAllText(manifest, "schemaVersion = \"0.2\"");
            Receipt = new LatestBuildReceipt(
                Path.Combine(_root, "latest-build.json"),
                new string('a', 64),
                "project",
                _root,
                Path.Combine(_root, "main.jnx"),
                Path.Combine(_root, "out"),
                ["server"],
                Path.Combine(_root, "bundle.json"),
                Path.Combine(_root, "artifacts.json"),
                manifest,
                Path.Combine(_root, "analysis.json"));
        }

        public LatestBuildReceipt Receipt { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
