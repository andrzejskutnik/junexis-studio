using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

internal sealed class FakeWhyTraceQueryService : IWhyTraceQueryService
{
    private readonly WhyTraceResult _result;
    private readonly bool _throwOnCancellation;

    public FakeWhyTraceQueryService(WhyTraceResult result)
        : this(result, throwOnCancellation: false)
    {
    }

    public FakeWhyTraceQueryService(WhyTraceResult result, bool throwOnCancellation)
    {
        _result = result;
        _throwOnCancellation = throwOnCancellation;
    }

    public Task<WhyTraceResult> GetWhyAsync(string baseUrl, ulong eventId, CancellationToken cancellationToken = default)
    {
        if (_throwOnCancellation)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Task.FromResult(_result);
    }
}

internal sealed class ThrowingWhyTraceQueryService : IWhyTraceQueryService
{
    private readonly Exception _exception;

    public ThrowingWhyTraceQueryService(Exception exception)
    {
        _exception = exception;
    }

    public Task<WhyTraceResult> GetWhyAsync(string baseUrl, ulong eventId, CancellationToken cancellationToken = default)
        => Task.FromException<WhyTraceResult>(_exception);
}

internal sealed class BlockingWhyTraceQueryService : IWhyTraceQueryService
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<WhyTraceResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    public Task Started => _started.Task;

    public Task<WhyTraceResult> GetWhyAsync(string baseUrl, ulong eventId, CancellationToken cancellationToken = default)
    {
        CallCount++;
        _started.TrySetResult();
        return _completion.Task;
    }

    public void Complete(WhyTraceResult result)
    {
        _completion.TrySetResult(result);
    }
}

internal sealed class MutableWhyTraceQueryService : IWhyTraceQueryService
{
    public WhyTraceResult Result { get; set; } = new(0, [], 0);

    public Task<WhyTraceResult> GetWhyAsync(string baseUrl, ulong eventId, CancellationToken cancellationToken = default)
        => Task.FromResult(Result);
}

internal sealed class MutableOutcomeWhyTraceQueryService : IWhyTraceQueryService
{
    public WhyTraceResult Result { get; set; } = new(0, [], 0);

    public Exception? Exception { get; set; }

    public Task<WhyTraceResult> GetWhyAsync(string baseUrl, ulong eventId, CancellationToken cancellationToken = default)
        => Exception is null
            ? Task.FromResult(Result)
            : Task.FromException<WhyTraceResult>(Exception);
}

internal sealed class CorrelatingWhyTraceQueryService : IWhyTraceQueryService
{
    public WhyDebugCorrelationResult Correlation { get; set; } = new(
        "1.0", true, 42, 7, "task:Worker", "symbol-and-source", "MainNarrative", ["stable symbol", "source"]);
    public WhyDebugCorrelationRequest? LastRequest { get; private set; }
    public int WhyCalls { get; private set; }

    public Task<WhyDebugCorrelationResult> CorrelateAsync(
        string baseUrl,
        WhyDebugCorrelationRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(Correlation);
    }

    public Task<WhyTraceResult> GetWhyAsync(
        string baseUrl,
        ulong eventId,
        CancellationToken cancellationToken = default)
    {
        WhyCalls++;
        return Task.FromResult(new WhyTraceResult(
            eventId,
            [new WhyTraceChainEntry(eventId, 7, "SCHED_DEQ", 9, "2026-07-22T10:00:00Z", "MainNarrative")],
            1));
    }
}

internal sealed class DiagnosticStoryQueryService : IWhyTraceQueryService
{
    public int StoryCallCount { get; private set; }
    public int LatestCallCount { get; private set; }
    public int AdjacentCallCount { get; private set; }
    public ulong? LastRequestedEventId { get; private set; }
    public bool? LastAdjacentPrevious { get; private set; }
    public string? LastAdjacentBuildId { get; private set; }
    public string? LastAdjacentRuntimeBundleId { get; private set; }
    public string? LastAdjacentNarrativeId { get; private set; }
    public string? LastAdjacentEventKind { get; private set; }
    public NarrativeEventSelection? LatestSelection { get; set; } =
        new(42, "SCHED_DEQ", "MainNarrative", "build-1");
    public NarrativeEventSelection? AdjacentSelection { get; set; }

    public DiagnosticStoryResult Story { get; set; } = new(
        "1.1", 42, "task:Worker", "Worker", true, "MainNarrative",
        [new DiagnosticStoryEvent(41, 7, "SCHED_ENQ", 8, "2026-07-23T10:00:00Z", "main.jnx", 16, "Worker", "MainNarrative")],
        [new DiagnosticStoryEvent(42, 7, "SCHED_DEQ", 9, "2026-07-23T10:00:01Z", "main.jnx", 17, "Worker", "MainNarrative")],
        [new DiagnosticStoryEvent(50, 8, "COMM_SEND", 9, "2026-07-23T10:00:01Z", "peer.jnx", 4, "Peer", null)],
        [new WhyCommunicationResolution(42, "communication:Main:Worker->Sink", "chan", "pipeline:Main", "task:Worker", "task:Sink", null)],
        ["Vector clocks prove event #50 concurrent with the target."])
    {
        BuildId = "build-1",
        RuntimeBundleId = "bundle-1"
    };

    public Task<WhyTraceResult> GetWhyAsync(string baseUrl, ulong eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WhyTraceResult(eventId, [], 0));

    public Task<NarrativeEventSelection?> GetLatestEventAsync(
        string baseUrl,
        string? buildId,
        string? runtimeBundleId,
        string? narrativeId,
        string? eventKind,
        CancellationToken cancellationToken = default)
    {
        LatestCallCount++;
        return Task.FromResult(LatestSelection);
    }

    public Task<NarrativeEventSelection?> GetAdjacentEventAsync(
        string baseUrl,
        ulong eventId,
        bool previous,
        string? buildId,
        string? runtimeBundleId,
        string? narrativeId,
        string? eventKind,
        CancellationToken cancellationToken = default)
    {
        AdjacentCallCount++;
        LastAdjacentPrevious = previous;
        LastAdjacentBuildId = buildId;
        LastAdjacentRuntimeBundleId = runtimeBundleId;
        LastAdjacentNarrativeId = narrativeId;
        LastAdjacentEventKind = eventKind;
        return Task.FromResult(AdjacentSelection);
    }

    public Task<DiagnosticStoryResult> GetDiagnosticStoryAsync(
        string baseUrl,
        ulong eventId,
        CancellationToken cancellationToken = default)
    {
        StoryCallCount++;
        LastRequestedEventId = eventId;
        return Task.FromResult(Story with { TargetEventId = eventId });
    }
}

internal sealed class NarrativeDebugSessionStub : IDebugSessionService
{
    public DebugSessionSnapshot Snapshot { get; private set; } =
        new(DebugSessionState.Running, "Running", 1, [], []);

    public IReadOnlyList<DebugBreakpointStatus> BreakpointStatuses => [];
    public event Action<DebugSessionSnapshot>? SnapshotChanged;
    public event Action<DebugOutputEvent>? OutputReceived { add { } remove { } }
    public event Action<IReadOnlyList<DebugBreakpointStatus>>? BreakpointStatusesChanged { add { } remove { } }

    public void Publish(DebugSessionSnapshot snapshot)
    {
        Snapshot = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }

    public Task StartAsync(DebugSessionConfiguration configuration, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public Task SetBreakpointsAsync(string path, IReadOnlyList<DebugSourceBreakpoint> breakpoints, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StepInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StepOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<DebugScope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DebugScope>>([]);
    public Task<IReadOnlyList<DebugVariable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DebugVariable>>([]);
    public Task<DebugEvaluation> EvaluateAsync(string expression, int? frameId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DebugEvaluation(expression, string.Empty, null, 0));
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class NarrativeTemporaryDirectory : IDisposable
{
    public NarrativeTemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "junexis-narrative-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

internal sealed class DiagnosticStoryMentorService : IMentorAnalysisService
{
    public int CallCount { get; private set; }

    public Task<MentorAnalysisResult> AnalyzeAsync(string snapshotPath, CancellationToken cancellationToken = default) =>
        AnalyzeCore(snapshotPath);

    private Task<MentorAnalysisResult> AnalyzeCore(string snapshotPath)
    {
        CallCount++;
        return Task.FromResult(new MentorAnalysisResult(
            "build-1",
            snapshotPath,
            [
                new MentorFinding("JNX001", "warning", "Worker can block the pipeline.", "task:Worker", null),
                new MentorFinding("JNX999", "info", "Unrelated finding.", "task:Other", null)
            ]));
    }
}

internal sealed class FakeNarrativeSessionService : INarrativeSessionService
{
    public NarrativeSessionSnapshot Snapshot { get; private set; } = new(
        NarrativeSessionState.Ready,
        "Narrative is ready; latest event #42.",
        "http://127.0.0.1:43125",
        "build-1",
        "debug.toml",
        42,
        5,
        NarrativeRuntimeActivity.Live);

    public event Action<NarrativeSessionSnapshot>? SnapshotChanged;

    public Task StartAsync(LatestBuildReceipt receipt, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Publish(NarrativeSessionSnapshot snapshot)
    {
        Snapshot = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }
}

public sealed class WhyPanelViewModelTests
{
    [Fact]
    [Trait("Issue", "25")]
    public void ManagedNarrativeSession_SelectsLatestEventAndCollectorUrl()
    {
        var session = new FakeNarrativeSessionService();
        using var sut = new WhyPanelViewModel(
            new DiagnosticStoryQueryService(),
            new OutputService(),
            narrativeSession: session);

        Assert.Equal("http://127.0.0.1:43125", sut.BaseUrl);
        Assert.Equal("42", sut.EventIdText);
        Assert.Contains("latest event #42", sut.NarrativeSessionText, StringComparison.Ordinal);

        session.Publish(session.Snapshot with
        {
            LatestEventId = 51,
            Status = "Narrative is ready; latest event #51."
        });

        Assert.Equal("51", sut.EventIdText);
        Assert.Contains("#51", sut.NarrativeSessionText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "28")]
    public void ManagedNarrativeSession_NewRuntimeEvent_AutomaticallyLoadsDiagnosticStory()
    {
        var session = new FakeNarrativeSessionService();
        var service = new DiagnosticStoryQueryService();
        using var sut = new WhyPanelViewModel(
            service,
            new OutputService(),
            narrativeSession: session);

        session.Publish(session.Snapshot with
        {
            LatestEventId = 51,
            TotalEvents = 6,
            EventDelta = 1,
            LastEventObservedAt = DateTimeOffset.UtcNow,
            Status = "Narrative is ready; latest event #51."
        });

        Assert.Equal((ulong)51, service.LastRequestedEventId);
        Assert.Equal((ulong)51, sut.DiagnosticStory?.TargetEventId);
        Assert.Contains("loaded automatically", sut.NarrativeActivityText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "28")]
    public void ManagedNarrativeSession_AutoFollowDisabled_SelectsButDoesNotLoadEvent()
    {
        var session = new FakeNarrativeSessionService();
        var service = new DiagnosticStoryQueryService();
        using var sut = new WhyPanelViewModel(
            service,
            new OutputService(),
            narrativeSession: session)
        {
            AutoFollowRuntime = false
        };

        session.Publish(session.Snapshot with
        {
            LatestEventId = 51,
            TotalEvents = 6,
            EventDelta = 1,
            Status = "Narrative is ready; latest event #51."
        });

        Assert.Equal("51", sut.EventIdText);
        Assert.Equal(0, service.StoryCallCount);
        Assert.Contains("latest #51", sut.NarrativeActivityText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "29")]
    public async Task RuntimeFollow_PausePreservesCaptureAndResumeLoadsNewestFilteredEvent()
    {
        var session = new FakeNarrativeSessionService();
        var service = new DiagnosticStoryQueryService
        {
            LatestSelection = new NarrativeEventSelection(
                49, "COMM_RECV", "MainNarrative", "build-1")
        };
        using var sut = new WhyPanelViewModel(
            service,
            new OutputService(),
            narrativeSession: session)
        {
            NarrativeFilterText = "MainNarrative",
            EventKindFilterText = "COMM_RECV"
        };

        sut.PauseRuntimeFollow();
        session.Publish(session.Snapshot with
        {
            LatestEventId = 51,
            TotalEvents = 7,
            EventDelta = 2
        });

        Assert.True(sut.RuntimeFollowPaused);
        Assert.Equal(0, service.StoryCallCount);
        Assert.Contains("capture continues", sut.NarrativeActivityText, StringComparison.Ordinal);

        await sut.ResumeRuntimeFollowAsync();
        await WaitUntilAsync(() => sut.DiagnosticStory?.TargetEventId == 49);

        Assert.False(sut.RuntimeFollowPaused);
        Assert.Equal(1, service.LatestCallCount);
        Assert.Equal((ulong)49, service.LastRequestedEventId);
        Assert.Equal("49", sut.EventIdText);
    }

    [Fact]
    [Trait("Issue", "29")]
    public async Task DebuggerStop_AutomaticallyLoadsBuildFilteredDiagnosticStory_AndResumeCancelsContext()
    {
        var session = new FakeNarrativeSessionService();
        var debug = new NarrativeDebugSessionStub();
        var service = new CorrelatingWhyTraceQueryService();
        using var sut = new WhyPanelViewModel(
            service,
            new OutputService(),
            debugSession: debug,
            narrativeSession: session)
        {
            NarrativeFilterText = "MainNarrative",
            EventKindFilterText = "SCHED_DEQ"
        };
        var frame = new DebugStackFrame(
            3, "Worker", Path.GetFullPath("main.jnx"), 17, 1,
            JunexisSymbolId: "task:Worker");

        debug.Publish(new DebugSessionSnapshot(
            DebugSessionState.Stopped, "Stopped", 1, [new DebugThread(1, "main")], [frame]));
        await WaitUntilAsync(() => sut.DiagnosticStory?.TargetEventId == 42);

        Assert.Equal("build-1", service.LastRequest?.BuildId);
        Assert.Equal("MainNarrative", service.LastRequest?.NarrativeId);
        Assert.Equal("SCHED_DEQ", service.LastRequest?.EventKind);
        Assert.Contains("symbol-and-source", sut.DebugCorrelationText, StringComparison.Ordinal);

        debug.Publish(new DebugSessionSnapshot(DebugSessionState.Running, "Running", 1, [], []));

        Assert.Null(sut.DebugCorrelation);
        Assert.Contains("Debugger running", sut.DebugCorrelationText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "30")]
    public async Task HistoryNavigation_PausesFollowPreservesPinAndBuildsEvidenceComparison()
    {
        var session = new FakeNarrativeSessionService();
        var service = new DiagnosticStoryQueryService
        {
            AdjacentSelection = new NarrativeEventSelection(
                41, "COMM_RECV", "MainNarrative", "build-1")
        };
        using var sut = new WhyPanelViewModel(
            service,
            new OutputService(),
            narrativeSession: session)
        {
            EventIdText = "42",
            NarrativeFilterText = "MainNarrative",
            EventKindFilterText = "COMM_RECV"
        };
        await sut.LoadAsync();
        sut.PinCurrentStory();
        var pinned = sut.PinnedDiagnosticStory;
        service.Story = service.Story with
        {
            Evidence = ["Current-only evidence."],
            Timeline =
            [
                .. service.Story.Timeline,
                new DiagnosticStoryEvent(
                    41, 7, "COMM_RECV", 10, "2026-07-23T10:00:02Z",
                    "main.jnx", 18, "Worker", "MainNarrative")
            ]
        };

        await sut.NavigateHistoryAsync(previous: true);
        sut.CompareWithPinnedStory();

        Assert.True(sut.RuntimeFollowPaused);
        Assert.Equal("41", sut.EventIdText);
        Assert.Same(pinned, sut.PinnedDiagnosticStory);
        Assert.Equal(true, service.LastAdjacentPrevious);
        Assert.Equal("build-1", service.LastAdjacentBuildId);
        Assert.Equal("bundle-1", service.LastAdjacentRuntimeBundleId);
        Assert.Equal("MainNarrative", service.LastAdjacentNarrativeId);
        Assert.Equal("COMM_RECV", service.LastAdjacentEventKind);
        Assert.Equal((ulong)42, sut.StoryComparison?.PinnedEventId);
        Assert.Equal((ulong)41, sut.StoryComparison?.CurrentEventId);
        Assert.True(sut.StoryComparison?.SameBuild);
        Assert.Equal(1, sut.StoryComparison?.TimelineDelta);
        Assert.Contains("Current-only evidence.", sut.StoryComparison?.AddedEvidence ?? []);
        Assert.Contains(
            "Vector clocks prove event #50 concurrent with the target.",
            sut.StoryComparison?.RemovedEvidence ?? []);
    }

    [Fact]
    [Trait("Issue", "30")]
    public async Task HistoryNavigation_AtBoundaryKeepsCurrentStoryAndDebuggerContext()
    {
        var service = new DiagnosticStoryQueryService { AdjacentSelection = null };
        using var sut = new WhyPanelViewModel(service, new OutputService())
        {
            EventIdText = "42"
        };
        await sut.LoadAsync();
        var current = sut.DiagnosticStory;

        await sut.NavigateHistoryAsync(previous: false);

        Assert.Same(current, sut.DiagnosticStory);
        Assert.Equal("42", sut.EventIdText);
        Assert.Contains("No next event", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "M7.8")]
    public async Task StoryComparison_RejectsSameBuildFromDifferentRuntimeBundle()
    {
        var service = new DiagnosticStoryQueryService();
        using var sut = new WhyPanelViewModel(service, new OutputService())
        {
            EventIdText = "42"
        };
        await sut.LoadAsync();
        sut.PinCurrentStory();
        service.Story = service.Story with
        {
            TargetEventId = 43,
            RuntimeBundleId = "bundle-2"
        };
        sut.EventIdText = "43";
        await sut.LoadAsync();

        sut.CompareWithPinnedStory();

        Assert.False(sut.StoryComparison?.SameBuild);
        Assert.Contains("different or incomplete runtime identity", sut.StoryComparison?.Summary, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "31")]
    public async Task NamedBookmark_PersistsPerWorkspaceAndReopensWithoutCollector()
    {
        using var root = new NarrativeTemporaryDirectory();
        var workspace = new WorkspaceService();
        workspace.OpenFolder(root.Path);
        var service = new DiagnosticStoryQueryService();
        using (var firstSession = new WhyPanelViewModel(
                   service,
                   new OutputService(),
                   workspace: workspace))
        {
            firstSession.EventIdText = "42";
            await firstSession.LoadAsync();
            firstSession.BookmarkNameText = "Before retry";
            firstSession.SaveBookmark();

            var storedPath = Path.Combine(
                root.Path,
                ".junexis",
                "narrative-session.json");
            Assert.True(File.Exists(storedPath));
            Assert.Single(firstSession.Bookmarks);
        }

        var offlineService = new ThrowingWhyTraceQueryService(
            new InvalidOperationException("Collector must not be called."));
        using var reopened = new WhyPanelViewModel(
            offlineService,
            new OutputService(),
            workspace: workspace);

        var bookmark = Assert.Single(reopened.Bookmarks);
        Assert.Equal("Before retry", bookmark.Name);
        reopened.SelectedBookmark = bookmark;
        reopened.OpenSelectedBookmark();

        Assert.Equal((ulong)42, reopened.DiagnosticStory?.TargetEventId);
        Assert.True(reopened.RuntimeFollowPaused);
        Assert.Contains("Opened diagnostic bookmark", reopened.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "31")]
    public async Task DiagnosticSessionBundle_ContainsChecksummedOfflineEvidence()
    {
        using var root = new NarrativeTemporaryDirectory();
        var workspace = new WorkspaceService();
        workspace.OpenFolder(root.Path);
        var service = new DiagnosticStoryQueryService();
        using var sut = new WhyPanelViewModel(
            service,
            new OutputService(),
            workspace: workspace)
        {
            EventIdText = "42",
            BookmarkNameText = "Baseline",
            NarrativeFilterText = "MainNarrative",
            EventKindFilterText = "SCHED_DEQ"
        };
        await sut.LoadAsync();
        sut.PinCurrentStory();
        sut.SaveBookmark();
        service.Story = service.Story with
        {
            BuildId = "build-1",
            Evidence = ["Changed runtime evidence."]
        };
        sut.EventIdText = "43";
        await sut.LoadAsync();
        sut.CompareWithPinnedStory();
        var bundlePath = Path.Combine(root.Path, "diagnostic-session.zip");

        await sut.ExportDiagnosticSessionAsync(bundlePath);

        using var archive = ZipFile.OpenRead(bundlePath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "session.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "stories/current.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "stories/pinned.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "comparison.json");
        Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("bookmarks/", StringComparison.Ordinal));
        var manifestEntry = Assert.Single(
            archive.Entries,
            entry => entry.FullName == "manifest.json");
        using var manifestStream = manifestEntry.Open();
        using var manifest = await JsonDocument.ParseAsync(manifestStream);
        foreach (var checksum in manifest.RootElement.GetProperty("Sha256").EnumerateObject())
        {
            var entry = Assert.Single(
                archive.Entries,
                item => item.FullName == checksum.Name);
            await using var stream = entry.Open();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var actual = Convert.ToHexString(SHA256.HashData(memory.ToArray())).ToLowerInvariant();
            Assert.Equal(checksum.Value.GetString(), actual);
            Assert.Equal(1980, entry.LastWriteTime.Year);
        }
    }

    [Fact]
    [Trait("Issue", "32")]
    public async Task DiagnosticSessionImport_VerifiesAndOpensBundleReadOnlyWithoutCollector()
    {
        using var root = new NarrativeTemporaryDirectory();
        var workspace = new WorkspaceService();
        workspace.OpenFolder(root.Path);
        var bundlePath = Path.Combine(root.Path, "diagnostic-session.zip");
        using (var exporter = new WhyPanelViewModel(
                   new DiagnosticStoryQueryService(),
                   new OutputService(),
                   workspace: workspace)
               {
                   EventIdText = "42",
                   BookmarkNameText = "Captured baseline",
                   NarrativeFilterText = "MainNarrative",
                   EventKindFilterText = "SCHED_DEQ"
               })
        {
            await exporter.LoadAsync();
            exporter.SaveBookmark();
            await exporter.ExportDiagnosticSessionAsync(bundlePath);
        }

        using var sut = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Import must not call Collector.")),
            new OutputService(),
            workspace: workspace);

        await sut.ImportDiagnosticSessionAsync(bundlePath);

        Assert.NotNull(sut.ImportedSession);
        Assert.False(sut.CanEditBookmarks);
        Assert.True(sut.CanCloseImportedSession);
        Assert.Equal((ulong)42, sut.DiagnosticStory?.TargetEventId);
        Assert.Equal("MainNarrative", sut.NarrativeFilterText);
        Assert.Contains("read-only", sut.DiagnosticSessionModeText, StringComparison.Ordinal);
        var importedBookmark = Assert.Single(sut.Bookmarks);
        sut.SelectedBookmark = importedBookmark;
        sut.RemoveSelectedBookmark();
        Assert.Single(sut.Bookmarks);
        Assert.Contains("read-only", sut.StatusText, StringComparison.Ordinal);

        sut.CloseImportedDiagnosticSession();

        Assert.Null(sut.ImportedSession);
        Assert.True(sut.CanEditBookmarks);
        Assert.Null(sut.DiagnosticStory);
        Assert.Single(sut.Bookmarks);
        Assert.Contains("workspace session restored", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "32")]
    public async Task DiagnosticSessionImport_WhenEvidenceWasTampered_RejectsBundleAtomically()
    {
        using var root = new NarrativeTemporaryDirectory();
        var bundlePath = Path.Combine(root.Path, "tampered-session.zip");
        var service = new DiagnosticStoryQueryService();
        using var sut = new WhyPanelViewModel(service, new OutputService())
        {
            EventIdText = "42"
        };
        await sut.LoadAsync();
        var originalStory = sut.DiagnosticStory;
        await sut.ExportDiagnosticSessionAsync(bundlePath);
        using (var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Update))
        {
            archive.GetEntry("stories/current.json")!.Delete();
            var replacement = archive.CreateEntry("stories/current.json");
            await using var stream = replacement.Open();
            await stream.WriteAsync("{}"u8.ToArray());
        }

        await sut.ImportDiagnosticSessionAsync(bundlePath);

        Assert.Null(sut.ImportedSession);
        Assert.Same(originalStory, sut.DiagnosticStory);
        Assert.Contains("integrity check failed", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "32")]
    public async Task DiagnosticSessionImport_WhenChecksummedMetadataContradictsStory_RejectsBundle()
    {
        using var root = new NarrativeTemporaryDirectory();
        var bundlePath = Path.Combine(root.Path, "inconsistent-session.zip");
        using var sut = new WhyPanelViewModel(
            new DiagnosticStoryQueryService(),
            new OutputService())
        {
            EventIdText = "42"
        };
        await sut.LoadAsync();
        await sut.ExportDiagnosticSessionAsync(bundlePath);
        using (var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Update))
        {
            var sessionEntry = archive.GetEntry("session.json")!;
            JsonNode session;
            await using (var stream = sessionEntry.Open())
                session = await JsonNode.ParseAsync(stream) ?? throw new InvalidDataException();
            session["CurrentEventId"] = 999UL;
            var sessionBytes = JsonSerializer.SerializeToUtf8Bytes(
                session,
                new JsonSerializerOptions { WriteIndented = true });

            var manifestEntry = archive.GetEntry("manifest.json")!;
            JsonNode manifest;
            await using (var stream = manifestEntry.Open())
                manifest = await JsonNode.ParseAsync(stream) ?? throw new InvalidDataException();
            manifest["Sha256"]!["session.json"] =
                Convert.ToHexString(SHA256.HashData(sessionBytes)).ToLowerInvariant();
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                new JsonSerializerOptions { WriteIndented = true });

            sessionEntry.Delete();
            manifestEntry.Delete();
            await WriteZipEntryAsync(archive, "session.json", sessionBytes);
            await WriteZipEntryAsync(archive, "manifest.json", manifestBytes);
        }

        await sut.ImportDiagnosticSessionAsync(bundlePath);

        Assert.Null(sut.ImportedSession);
        Assert.Contains("identities do not match", sut.StatusText, StringComparison.Ordinal);

        static async Task WriteZipEntryAsync(
            ZipArchive archive,
            string name,
            byte[] content)
        {
            var entry = archive.CreateEntry(name);
            await using var stream = entry.Open();
            await stream.WriteAsync(content);
        }
    }

    [Fact]
    [Trait("Issue", "33")]
    public async Task DiagnosticSessionImport_ExposesVerifiedProvenanceAndEvidenceInventory()
    {
        using var root = new NarrativeTemporaryDirectory();
        var workspace = new WorkspaceService();
        workspace.OpenFolder(root.Path);
        var bundlePath = Path.Combine(root.Path, "provenance-session.zip");
        using (var exporter = new WhyPanelViewModel(
                   new DiagnosticStoryQueryService(),
                   new OutputService(),
                   workspace: workspace)
               {
                   EventIdText = "42",
                   BookmarkNameText = "Provenance baseline"
               })
        {
            await exporter.LoadAsync();
            exporter.SaveBookmark();
            await exporter.ExportDiagnosticSessionAsync(bundlePath);
        }
        using var sut = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Provenance inspection must remain offline.")),
            new OutputService());

        await sut.ImportDiagnosticSessionAsync(bundlePath);

        var imported = Assert.IsType<ImportedNarrativeDiagnosticSession>(sut.ImportedSession);
        Assert.Equal(
            NarrativeDiagnosticAuthenticityStatus.Unsigned,
            imported.Signature.Status);
        Assert.Equal(imported.VerifiedFileCount, sut.ImportedEvidenceFiles.Count);
        Assert.Contains("Integrity · verified SHA-256", sut.ImportedSessionSummaryText, StringComparison.Ordinal);
        Assert.Contains("Build · build-1", sut.ImportedSessionSummaryText, StringComparison.Ordinal);
        Assert.Contains(sut.ImportedEvidenceFiles, file => file.Path == "session.json");
        Assert.Contains(sut.ImportedEvidenceFiles, file => file.Path.StartsWith("bookmarks/", StringComparison.Ordinal));
        Assert.All(sut.ImportedEvidenceFiles, file =>
        {
            Assert.True(file.SizeBytes > 0);
            Assert.Equal(64, file.Sha256.Length);
        });
        Assert.Equal(
            sut.ImportedEvidenceFiles.OrderBy(file => file.Path, StringComparer.Ordinal),
            sut.ImportedEvidenceFiles);
    }

    [Fact]
    [Trait("Issue", "33")]
    public async Task CompareImportedWithLive_KeepsProvenanceAndReportsDirectionalEvidenceDelta()
    {
        using var root = new NarrativeTemporaryDirectory();
        var bundlePath = Path.Combine(root.Path, "comparison-session.zip");
        var importedService = new DiagnosticStoryQueryService
        {
            Story = new DiagnosticStoryQueryService().Story with
            {
                TargetEventId = 77,
                Evidence = ["Imported evidence."]
            }
        };
        using (var exporter = new WhyPanelViewModel(importedService, new OutputService())
               {
                   EventIdText = "77"
               })
        {
            await exporter.LoadAsync();
            await exporter.ExportDiagnosticSessionAsync(bundlePath);
        }
        var liveService = new DiagnosticStoryQueryService
        {
            Story = new DiagnosticStoryQueryService().Story with
            {
                TargetEventId = 42,
                Evidence = ["Live evidence."]
            }
        };
        using var sut = new WhyPanelViewModel(liveService, new OutputService())
        {
            EventIdText = "42"
        };
        await sut.LoadAsync();

        await sut.ImportDiagnosticSessionAsync(bundlePath);
        sut.CompareImportedWithLive();

        Assert.True(sut.CanCompareImportedWithLive);
        Assert.Contains("event #42", sut.LiveComparisonCandidateText, StringComparison.Ordinal);
        var comparison = Assert.IsType<DiagnosticSessionCrossComparison>(sut.CrossSessionComparison);
        Assert.Equal((ulong)77, comparison.ImportedEventId);
        Assert.Equal((ulong)42, comparison.LiveEventId);
        Assert.True(comparison.SameBuild);
        Assert.Equal(["Live evidence."], comparison.AddedInLive);
        Assert.Equal(["Imported evidence."], comparison.MissingFromLive);
        Assert.Contains("live +1/-1", comparison.Summary, StringComparison.Ordinal);

        sut.CloseImportedDiagnosticSession();

        Assert.Empty(sut.ImportedEvidenceFiles);
        Assert.Null(sut.CrossSessionComparison);
        Assert.False(sut.CanCompareImportedWithLive);
    }

    [Fact]
    [Trait("Issue", "34")]
    public async Task SignedDiagnosticSession_VerifiesAndPersistsExactSignerTrustLocally()
    {
        using var root = new NarrativeTemporaryDirectory();
        var keyPath = Path.Combine(root.Path, "research-team.pem");
        var trustPath = Path.Combine(root.Path, "local-trust.json");
        var bundlePath = Path.Combine(root.Path, "signed-session.zip");
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        using (var exporter = new WhyPanelViewModel(
                   new DiagnosticStoryQueryService(),
                   new OutputService())
               {
                   EventIdText = "42"
               })
        {
            await exporter.LoadAsync();
            await exporter.ExportSignedDiagnosticSessionAsync(
                bundlePath,
                keyPath,
                "Distributed Systems Lab");
        }
        using (var archive = ZipFile.OpenRead(bundlePath))
        {
            Assert.Contains(archive.Entries, entry => entry.FullName == "signature.json");
            Assert.DoesNotContain(archive.Entries, entry =>
                ReadEntry(entry).Contains("PRIVATE KEY", StringComparison.Ordinal));
        }

        using (var firstImport = new WhyPanelViewModel(
                   new ThrowingWhyTraceQueryService(
                       new InvalidOperationException("Signed import must remain offline.")),
                   new OutputService(),
                   diagnosticTrustStorePath: trustPath))
        {
            await firstImport.ImportDiagnosticSessionAsync(bundlePath);

            var imported = Assert.IsType<ImportedNarrativeDiagnosticSession>(
                firstImport.ImportedSession);
            Assert.Equal(
                NarrativeDiagnosticAuthenticityStatus.SignatureVerified,
                imported.Signature.Status);
            Assert.True(firstImport.CanTrustImportedSigner);
            Assert.Equal(64, imported.BundleId.Length);
            Assert.Equal(64, imported.Signature.SignerFingerprint?.Length);

            firstImport.TrustImportedSigner();

            Assert.Equal(
                NarrativeDiagnosticAuthenticityStatus.TrustedSigner,
                firstImport.ImportedSession?.Signature.Status);
            Assert.True(firstImport.CanRemoveImportedSignerTrust);
            Assert.True(File.Exists(trustPath));
            Assert.DoesNotContain(
                "PRIVATE KEY",
                File.ReadAllText(trustPath),
                StringComparison.Ordinal);
        }

        using var trustedImport = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Trusted import must remain offline.")),
            new OutputService(),
            diagnosticTrustStorePath: trustPath);
        await trustedImport.ImportDiagnosticSessionAsync(bundlePath);
        Assert.Equal(
            NarrativeDiagnosticAuthenticityStatus.TrustedSigner,
            trustedImport.ImportedSession?.Signature.Status);

        var rotatedKeyPath = Path.Combine(root.Path, "rotated.pem");
        var rotatedBundlePath = Path.Combine(root.Path, "rotated-session.zip");
        using (var rotatedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            File.WriteAllText(rotatedKeyPath, rotatedKey.ExportPkcs8PrivateKeyPem());
        using (var rotatedExporter = new WhyPanelViewModel(
                   new DiagnosticStoryQueryService(),
                   new OutputService())
               {
                   EventIdText = "42"
               })
        {
            await rotatedExporter.LoadAsync();
            await rotatedExporter.ExportSignedDiagnosticSessionAsync(
                rotatedBundlePath,
                rotatedKeyPath,
                "Distributed Systems Lab");
        }
        using (var rotatedImport = new WhyPanelViewModel(
                   new ThrowingWhyTraceQueryService(
                       new InvalidOperationException("Rotated signer import must remain offline.")),
                   new OutputService(),
                   diagnosticTrustStorePath: trustPath))
        {
            await rotatedImport.ImportDiagnosticSessionAsync(rotatedBundlePath);
            Assert.Equal(
                NarrativeDiagnosticAuthenticityStatus.SignatureVerified,
                rotatedImport.ImportedSession?.Signature.Status);
        }

        trustedImport.RemoveImportedSignerTrust();

        Assert.Equal(
            NarrativeDiagnosticAuthenticityStatus.SignatureVerified,
            trustedImport.ImportedSession?.Signature.Status);

        static string ReadEntry(ZipArchiveEntry entry)
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
    }

    [Fact]
    [Trait("Issue", "34")]
    public async Task SignedDiagnosticSession_WhenSignatureIsAltered_OpensEvidenceAsInvalidClaim()
    {
        using var root = new NarrativeTemporaryDirectory();
        var keyPath = Path.Combine(root.Path, "signer.pem");
        var bundlePath = Path.Combine(root.Path, "invalid-signature.zip");
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        using (var exporter = new WhyPanelViewModel(
                   new DiagnosticStoryQueryService(),
                   new OutputService())
               {
                   EventIdText = "42"
               })
        {
            await exporter.LoadAsync();
            await exporter.ExportSignedDiagnosticSessionAsync(
                bundlePath,
                keyPath,
                "Claimed signer");
        }
        using (var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Update))
        {
            var signatureEntry = archive.GetEntry("signature.json")!;
            JsonNode signature;
            await using (var stream = signatureEntry.Open())
                signature = await JsonNode.ParseAsync(stream) ?? throw new InvalidDataException();
            signature["Signature"] = Convert.ToBase64String([1, 2, 3, 4]);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                signature,
                new JsonSerializerOptions { WriteIndented = true });
            signatureEntry.Delete();
            var replacement = archive.CreateEntry("signature.json");
            await using var replacementStream = replacement.Open();
            await replacementStream.WriteAsync(bytes);
        }
        using var sut = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Invalid signature inspection must remain offline.")),
            new OutputService(),
            diagnosticTrustStorePath: Path.Combine(root.Path, "trust.json"));

        await sut.ImportDiagnosticSessionAsync(bundlePath);

        Assert.NotNull(sut.DiagnosticStory);
        Assert.Equal(
            NarrativeDiagnosticAuthenticityStatus.InvalidSignature,
            sut.ImportedSession?.Signature.Status);
        Assert.False(sut.CanTrustImportedSigner);
        Assert.Contains("InvalidSignature", sut.DiagnosticAuthenticityText, StringComparison.Ordinal);
        Assert.Contains("Verified and opened", sut.StatusText, StringComparison.Ordinal);

        using (var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Update))
            archive.GetEntry("signature.json")!.Delete();
        await sut.ImportDiagnosticSessionAsync(bundlePath);

        Assert.Equal(
            NarrativeDiagnosticAuthenticityStatus.InvalidSignature,
            sut.ImportedSession?.Signature.Status);
        Assert.Contains("signature.json is missing", sut.DiagnosticAuthenticityText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "35")]
    public async Task DiagnosticReview_KeepsEvidenceImmutableAndPersistsVersionedSemanticStatements()
    {
        using var root = new NarrativeTemporaryDirectory();
        var bundlePath = Path.Combine(root.Path, "reviewed-session.zip");
        using (var exporter = new WhyPanelViewModel(
                   new DiagnosticStoryQueryService(),
                   new OutputService())
               {
                   EventIdText = "42"
               })
        {
            await exporter.LoadAsync();
            await exporter.ExportDiagnosticSessionAsync(bundlePath);
        }
        var originalBundleHash = SHA256.HashData(await File.ReadAllBytesAsync(bundlePath));
        using var sut = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Review workflow must remain offline.")),
            new OutputService());
        await sut.ImportDiagnosticSessionAsync(bundlePath);
        var bundleId = sut.ImportedSession?.BundleId;

        sut.ReviewAuthorText = "Distributed Systems Lab";
        sut.ReviewText = "The captured event is directly supported by the current Story.";
        sut.ReviewKind = NarrativeDiagnosticReviewKind.Observation;
        sut.AddReviewEntry();
        sut.SaveReviewRevision();
        sut.ReviewStatus = NarrativeDiagnosticReviewStatus.Investigating;
        sut.ReviewText = "A delayed communication may explain the observed ordering.";
        sut.ReviewKind = NarrativeDiagnosticReviewKind.Hypothesis;
        sut.AddReviewEntry();
        sut.SaveReviewRevision();
        sut.CompareLatestReviewRevisions();

        Assert.Equal(2, sut.ReviewEntries.Count);
        Assert.True(
            sut.DiagnosticReview?.Revisions.Count == 2,
            $"Expected two saved revisions. Studio status: {sut.StatusText}");
        Assert.Equal((ulong)42, sut.ReviewEntries[0].EventId);
        Assert.Equal("task:Worker", sut.ReviewEntries[0].SymbolId);
        Assert.NotNull(sut.ReviewEntries[0].EvidencePath);
        Assert.Equal(1, sut.ReviewComparison?.AddedEntries.Count);
        Assert.True(sut.ReviewComparison?.StatusChanged);
        Assert.Equal(bundleId, sut.DiagnosticReview?.BundleId);
        Assert.Equal(
            originalBundleHash,
            SHA256.HashData(await File.ReadAllBytesAsync(bundlePath)));
        Assert.True(File.Exists(bundlePath + ".review.json"));

        using var reopened = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Review reopen must remain offline.")),
            new OutputService());
        await reopened.ImportDiagnosticSessionAsync(bundlePath);
        Assert.Equal(bundleId, reopened.DiagnosticReview?.BundleId);
        Assert.Equal(2, reopened.DiagnosticReview?.Revisions.Count);
        Assert.Equal(NarrativeDiagnosticReviewStatus.Investigating, reopened.ReviewStatus);
    }

    [Fact]
    [Trait("Issue", "35")]
    public async Task DiagnosticReview_SignatureIsVerifiedAndWorkingEditInvalidatesIt()
    {
        using var root = new NarrativeTemporaryDirectory();
        var bundlePath = Path.Combine(root.Path, "signed-review-session.zip");
        var keyPath = Path.Combine(root.Path, "reviewer.pem");
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        using (var exporter = new WhyPanelViewModel(
                   new DiagnosticStoryQueryService(),
                   new OutputService())
               {
                   EventIdText = "42"
               })
        {
            await exporter.LoadAsync();
            await exporter.ExportDiagnosticSessionAsync(bundlePath);
        }
        using var sut = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Review signing must remain offline.")),
            new OutputService());
        await sut.ImportDiagnosticSessionAsync(bundlePath);
        sut.ReviewAuthorText = "Reviewer";
        sut.ReviewText = "Evidence supports this observation.";
        sut.AddReviewEntry();
        sut.SaveReviewRevision();

        await sut.SignReviewAsync(keyPath, "Research reviewer");

        Assert.Contains("SignatureVerified", sut.ReviewSignatureText, StringComparison.Ordinal);
        Assert.True(sut.CanExportDiagnosticCase);
        var signedJson = await File.ReadAllTextAsync(bundlePath + ".review.json");
        Assert.DoesNotContain("\"Source\"", signedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SourceReference\"", signedJson, StringComparison.Ordinal);
        sut.ReviewText = "A later unsaved hypothesis.";
        sut.ReviewKind = NarrativeDiagnosticReviewKind.Hypothesis;
        sut.AddReviewEntry();
        Assert.Contains("unsigned working changes", sut.ReviewSignatureText, StringComparison.Ordinal);
        Assert.False(sut.CanExportDiagnosticCase);
        Assert.Null(sut.DiagnosticReview?.Signature);
    }

    [Fact]
    [Trait("Issue", "35")]
    public async Task DiagnosticCase_RoundTripsOfflineAndRejectsTamperedReviewAtomically()
    {
        using var root = new NarrativeTemporaryDirectory();
        var bundlePath = Path.Combine(root.Path, "case-source.zip");
        var casePath = Path.Combine(root.Path, "diagnostic-case.zip");
        using (var exporter = new WhyPanelViewModel(
                   new DiagnosticStoryQueryService(),
                   new OutputService())
               {
                   EventIdText = "42"
               })
        {
            await exporter.LoadAsync();
            await exporter.ExportDiagnosticSessionAsync(bundlePath);
        }
        string? bundleId;
        using (var reviewer = new WhyPanelViewModel(
                   new ThrowingWhyTraceQueryService(
                       new InvalidOperationException("Case export must remain offline.")),
                   new OutputService()))
        {
            await reviewer.ImportDiagnosticSessionAsync(bundlePath);
            bundleId = reviewer.ImportedSession?.BundleId;
            reviewer.ReviewAuthorText = "Reviewer";
            reviewer.ReviewText = "Captured evidence establishes the observed outcome.";
            reviewer.AddReviewEntry();
            reviewer.SaveReviewRevision();
            await reviewer.ExportDiagnosticCaseAsync(casePath);
        }
        using var sut = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Case import must remain offline.")),
            new OutputService());
        await sut.ImportDiagnosticCaseAsync(casePath);

        Assert.Equal(bundleId, sut.ImportedSession?.BundleId);
        Assert.Equal(bundleId, sut.DiagnosticReview?.BundleId);
        Assert.Single(sut.DiagnosticReview?.Revisions ?? []);
        Assert.Contains("Verified and opened diagnostic case", sut.StatusText, StringComparison.Ordinal);

        using (var archive = ZipFile.Open(casePath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("review.json")!;
            entry.Delete();
            var replacement = archive.CreateEntry("review.json");
            await using var output = replacement.Open();
            await output.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(
                NarrativeDiagnosticReviewStore.Create("tampered")));
        }
        await sut.ImportDiagnosticCaseAsync(casePath);

        Assert.Equal(bundleId, sut.ImportedSession?.BundleId);
        Assert.Equal(bundleId, sut.DiagnosticReview?.BundleId);
        Assert.Contains("integrity check failed", sut.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "36")]
    public async Task MentorReviewProposals_AreGroundedOfflineAndNeverProposeDecision()
    {
        using var root = new NarrativeTemporaryDirectory();
        var bundlePath = Path.Combine(root.Path, "mentor-proposals.zip");
        var service = new DiagnosticStoryQueryService
        {
            Story = new DiagnosticStoryQueryService().Story with
            {
                MentorFindings =
                [
                    new MentorFinding(
                        "M1601",
                        "warning",
                        "Worker can block the pipeline.",
                        "task:Worker",
                        null,
                        "The task performs a potentially blocking operation.",
                        "Inspect the Worker scheduling boundary.")
                ]
            }
        };
        using (var exporter = new WhyPanelViewModel(service, new OutputService())
               {
                   EventIdText = "42"
               })
        {
            await exporter.LoadAsync();
            await exporter.ExportDiagnosticSessionAsync(bundlePath);
        }
        using var sut = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Mentor review proposals must remain offline.")),
            new OutputService());
        await sut.ImportDiagnosticSessionAsync(bundlePath);

        sut.GenerateMentorReviewProposals();

        Assert.Equal(3, sut.MentorReviewProposals.Count);
        Assert.Contains(
            sut.MentorReviewProposals,
            item => item.Kind == NarrativeDiagnosticReviewKind.Observation);
        Assert.Contains(
            sut.MentorReviewProposals,
            item => item.Kind == NarrativeDiagnosticReviewKind.Hypothesis);
        Assert.Contains(
            sut.MentorReviewProposals,
            item => item.Kind == NarrativeDiagnosticReviewKind.Action);
        Assert.DoesNotContain(
            sut.MentorReviewProposals,
            item => item.Kind == NarrativeDiagnosticReviewKind.Decision);
        Assert.All(sut.MentorReviewProposals, item =>
        {
            Assert.Equal("M1601", item.FindingCode);
            Assert.Equal("task:Worker", item.RelatedSymbolId);
            Assert.Equal("build-1", item.BuildId);
            Assert.Equal((ulong)42, item.EventId);
            Assert.Equal("stories/current.json", item.EvidencePath);
        });
        Assert.Empty(sut.ReviewEntries);
        Assert.Empty(sut.DiagnosticReview?.Revisions ?? []);
    }

    [Fact]
    [Trait("Issue", "36")]
    public async Task MentorProposal_RequiresExplicitAcceptanceAndInvalidatesSignedWorkingReview()
    {
        using var root = new NarrativeTemporaryDirectory();
        var bundlePath = Path.Combine(root.Path, "mentor-acceptance.zip");
        var keyPath = Path.Combine(root.Path, "review-key.pem");
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        var service = new DiagnosticStoryQueryService
        {
            Story = new DiagnosticStoryQueryService().Story with
            {
                MentorFindings =
                [
                    new MentorFinding(
                        "M1602",
                        "info",
                        "Worker has a deterministic review candidate.",
                        "task:Worker",
                        null,
                        RecommendedAction: "Review the Worker boundary.")
                ]
            }
        };
        using (var exporter = new WhyPanelViewModel(service, new OutputService())
               {
                   EventIdText = "42"
               })
        {
            await exporter.LoadAsync();
            await exporter.ExportDiagnosticSessionAsync(bundlePath);
        }
        using var sut = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Mentor acceptance must remain offline.")),
            new OutputService());
        await sut.ImportDiagnosticSessionAsync(bundlePath);
        sut.ReviewAuthorText = "Human reviewer";
        sut.ReviewText = "Human baseline.";
        sut.AddReviewEntry();
        sut.SaveReviewRevision();
        await sut.SignReviewAsync(keyPath, "Human reviewer");
        var statusBefore = sut.ReviewStatus;
        sut.GenerateMentorReviewProposals();
        var dismissed = Assert.IsType<NarrativeDiagnosticMentorProposal>(
            sut.SelectedMentorReviewProposal);
        sut.DismissSelectedMentorReviewProposal();

        Assert.Single(sut.ReviewEntries);
        Assert.NotNull(sut.DiagnosticReview?.Signature);
        Assert.DoesNotContain(
            sut.MentorReviewProposals,
            item => item.Id == dismissed.Id);

        sut.SelectedMentorReviewProposal = Assert.Single(
            sut.MentorReviewProposals,
            item => item.Kind == NarrativeDiagnosticReviewKind.Action);
        sut.AcceptSelectedMentorReviewProposal();

        var accepted = Assert.Single(
            sut.ReviewEntries,
            item => item.Source == NarrativeDiagnosticReviewSource.Mentor);
        Assert.Equal("Junexis Mentor", accepted.Author);
        Assert.Contains("mentor:M1602:Action:build:build-1", accepted.SourceReference);
        Assert.Equal(statusBefore, sut.ReviewStatus);
        Assert.Null(sut.DiagnosticReview?.Signature);
        Assert.False(sut.CanExportDiagnosticCase);
        Assert.Single(sut.DiagnosticReview?.Revisions ?? []);
        Assert.Contains("save a human-authored revision", sut.StatusText, StringComparison.Ordinal);

        sut.GenerateMentorReviewProposals();
        Assert.DoesNotContain(
            sut.MentorReviewProposals,
            item => item.SourceReference == accepted.SourceReference);
    }

    [Fact]
    [Trait("Issue", "36")]
    public async Task MentorReviewProposals_RejectUngroundedFindingWithoutChangingReview()
    {
        using var root = new NarrativeTemporaryDirectory();
        var bundlePath = Path.Combine(root.Path, "ungrounded-mentor.zip");
        var service = new DiagnosticStoryQueryService
        {
            Story = new DiagnosticStoryQueryService().Story with
            {
                MentorFindings =
                [
                    new MentorFinding(
                        "M1603",
                        "warning",
                        "Finding belongs to another symbol.",
                        "task:Other",
                        null)
                ]
            }
        };
        using (var exporter = new WhyPanelViewModel(service, new OutputService())
               {
                   EventIdText = "42"
               })
        {
            await exporter.LoadAsync();
            await exporter.ExportDiagnosticSessionAsync(bundlePath);
        }
        using var sut = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Ungrounded proposal check must remain offline.")),
            new OutputService());
        await sut.ImportDiagnosticSessionAsync(bundlePath);

        sut.GenerateMentorReviewProposals();

        Assert.Empty(sut.MentorReviewProposals);
        Assert.Empty(sut.ReviewEntries);
        Assert.Equal(NarrativeDiagnosticReviewStatus.Open, sut.ReviewStatus);
        Assert.Contains("no new grounded proposals", sut.MentorReviewProposalText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "25")]
    public async Task LoadAsync_PresentsTimelineConcurrencyEvidenceAndGroundedMentorFindings()
    {
        var bus = new EventBus();
        using var sut = new WhyPanelViewModel(
            new DiagnosticStoryQueryService(), new OutputService(), bus, mentor: new DiagnosticStoryMentorService())
        {
            EventIdText = "42"
        };
        bus.Publish(new AnalysisSnapshotUpdatedEvent("junexis", "build-1", "analysis.json"));

        await sut.LoadAsync();

        Assert.Single(sut.Timeline);
        Assert.Single(sut.ConcurrentEvents);
        Assert.Single(sut.StoryEvidence);
        var finding = Assert.Single(sut.MentorFindings);
        Assert.Equal("task:Worker", finding.RelatedSymbolId);
        Assert.Equal("MainNarrative", sut.DiagnosticStory?.NarrativeId);
        Assert.Contains("Mentor · Current", sut.MentorContextText, StringComparison.Ordinal);
        Assert.Contains("build-1", sut.MentorContextText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "25")]
    public async Task LoadAsync_DoesNotJoinMentorWhenRuntimeBuildDiffersFromActiveAnalysis()
    {
        var bus = new EventBus();
        var mentor = new DiagnosticStoryMentorService();
        var service = new DiagnosticStoryQueryService
        {
            Story = new DiagnosticStoryQueryService().Story with { BuildId = "older-build" }
        };
        using var sut = new WhyPanelViewModel(service, new OutputService(), bus, mentor: mentor)
        {
            EventIdText = "42"
        };
        bus.Publish(new AnalysisSnapshotUpdatedEvent("junexis", "build-1", "analysis.json"));

        await sut.LoadAsync();

        Assert.Equal(0, mentor.CallCount);
        Assert.Empty(sut.MentorFindings);
        Assert.Contains("not joined", sut.MentorContextText, StringComparison.Ordinal);
        Assert.Contains("older-build", sut.MentorContextText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "25")]
    public async Task LoadAsync_DoesNotJoinMentorWithoutRuntimeBuildIdentity()
    {
        var bus = new EventBus();
        var mentor = new DiagnosticStoryMentorService();
        var service = new DiagnosticStoryQueryService
        {
            Story = new DiagnosticStoryQueryService().Story with { BuildId = null }
        };
        using var sut = new WhyPanelViewModel(service, new OutputService(), bus, mentor: mentor)
        {
            EventIdText = "42"
        };
        bus.Publish(new AnalysisSnapshotUpdatedEvent("junexis", "build-1", "analysis.json"));

        await sut.LoadAsync();

        Assert.Equal(0, mentor.CallCount);
        Assert.Empty(sut.MentorFindings);
        Assert.Contains("runtime build identity was not captured", sut.MentorContextText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Issue", "25")]
    public async Task ExportDiagnosticStory_WritesVersionedJsonAndEvidenceLinkedMarkdown()
    {
        var sut = new WhyPanelViewModel(new DiagnosticStoryQueryService(), new OutputService())
        {
            EventIdText = "42"
        };
        await sut.LoadAsync();
        var directory = Path.Combine(Path.GetTempPath(), "junexis-story-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var jsonPath = Path.Combine(directory, "story.json");
            var markdownPath = Path.Combine(directory, "story.md");

            await sut.ExportJsonAsync(jsonPath);
            await sut.ExportMarkdownAsync(markdownPath);

            var json = await File.ReadAllTextAsync(jsonPath);
            var markdown = await File.ReadAllTextAsync(markdownPath);
            Assert.Contains("\"SchemaVersion\": \"1.1\"", json, StringComparison.Ordinal);
            Assert.Contains("\"BuildId\": \"build-1\"", json, StringComparison.Ordinal);
            Assert.Contains("# Diagnostic Story — event #42", markdown, StringComparison.Ordinal);
            Assert.Contains("- Build: `build-1`", markdown, StringComparison.Ordinal);
            Assert.Contains("Vector clocks prove event #50", markdown, StringComparison.Ordinal);
            Assert.Contains("| 42 | 7 | SCHED_DEQ | 9 |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CorrelateStoppedFrame_MatchedEvidenceLoadsWhyOnExplicitRequest()
    {
        var service = new CorrelatingWhyTraceQueryService();
        var sut = new WhyPanelViewModel(service, new OutputService());
        var frame = new DebugStackFrame(
            1, "Worker", Path.GetFullPath("main.jnx"), 17, 3,
            JunexisSymbolId: "task:Worker");

        await sut.CorrelateStoppedFrameAsync(frame, loadWhy: true);

        Assert.Equal("task:Worker", service.LastRequest?.SymbolId);
        Assert.Equal(frame.SourcePath, service.LastRequest?.SourcePath);
        Assert.Equal(17, service.LastRequest?.Line);
        Assert.Equal("42", sut.EventIdText);
        Assert.Single(sut.CausalChain);
        Assert.Equal(1, service.WhyCalls);
        Assert.Contains("symbol-and-source", sut.DebugCorrelationText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrelateStoppedFrame_NoMatchDoesNotGuessOrLoadWhy()
    {
        var service = new CorrelatingWhyTraceQueryService
        {
            Correlation = new WhyDebugCorrelationResult(
                "1.0", false, null, null, null, "none", null, ["no captured event"]),
        };
        var sut = new WhyPanelViewModel(service, new OutputService());

        await sut.CorrelateStoppedFrameAsync(new DebugStackFrame(1, "unknown", "main.jnx", 5, 1), loadWhy: true);

        Assert.Null(sut.DebugCorrelation?.EventId);
        Assert.Equal(0, service.WhyCalls);
        Assert.Contains("No Narrative event matched", sut.DebugCorrelationText, StringComparison.Ordinal);
    }

    [Fact]
    public void WhyForStoppedFrameEvent_CorrelatesAndLoadsCausalChain()
    {
        var bus = new EventBus();
        var service = new CorrelatingWhyTraceQueryService();
        using var sut = new WhyPanelViewModel(service, new OutputService(), bus);
        var frame = new DebugStackFrame(
            3, "Worker", Path.GetFullPath("main.jnx"), 17, 1,
            JunexisSymbolId: "task:Worker");

        bus.Publish(new WhyForStoppedFrameRequested(frame));

        Assert.Equal(frame.JunexisSymbolId, service.LastRequest?.SymbolId);
        Assert.Single(sut.CausalChain);
        Assert.Equal("42", sut.EventIdText);
    }

    [Fact]
    public async Task LoadAsync_WithValidEventId_UpdatesCausalChain()
    {
        var output = new OutputService();
        var service = new FakeWhyTraceQueryService(
            new WhyTraceResult(
                42,
                [
                    new WhyTraceChainEntry(1, 7, "COMM_SEND", 10, "2026-01-01T10:00:00Z", "Smart Home Automation"),
                    new WhyTraceChainEntry(2, 8, "COMM_RECV", 11, "2026-01-01T10:00:01Z", "Smart Home Automation")
                ],
                2));

        var sut = new WhyPanelViewModel(service, output)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        await sut.LoadAsync();

        Assert.Equal(2, sut.CausalChain.Count);
        Assert.Contains("Loaded 2 event(s)", sut.StatusText);
        Assert.False(sut.IsBusy);
        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Why" &&
            e.Severity == OutputSeverity.Info &&
            e.Message.Contains("Loaded 2 event(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_WithServiceTargetEventId_UsesResponseTargetInSuccessStatus()
    {
        var output = new OutputService();
        var service = new FakeWhyTraceQueryService(
            new WhyTraceResult(
                84,
                [
                    new WhyTraceChainEntry(1, 7, "COMM_SEND", 10, "2026-01-01T10:00:00Z", "Smart Home Automation")
                ],
                1));

        var sut = new WhyPanelViewModel(service, output)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        await sut.LoadAsync();

        Assert.Equal("Loaded 1 event(s) for target 84.", sut.StatusText);
        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Why" &&
            e.Severity == OutputSeverity.Info &&
            e.Message.Contains("target 84", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAndNavigate_ResolvedCommunicationOpensJunexisSource()
    {
        var output = new OutputService();
        var eventBus = new EventBus();
        NavigateToLocationRequested? navigation = null;
        eventBus.Subscribe<NavigateToLocationRequested>(request => navigation = request);
        var result = new WhyTraceResult(
            42,
            [new WhyTraceChainEntry(42, 7, "COMM_SEND", 10, "2026-01-01T10:00:00Z", null)],
            1)
        {
            Communications =
            [
                new WhyCommunicationResolution(
                    42,
                    "communication:Main:A->B",
                    "chan",
                    "pipeline:Main",
                    "task:A",
                    "task:B",
                    new AnalysisSourceLocation(Path.GetFullPath("main.jnx"), 12, 5))
            ]
        };
        var sut = new WhyPanelViewModel(new FakeWhyTraceQueryService(result), output, eventBus)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        await sut.LoadAsync();
        sut.NavigateTo(Assert.Single(sut.Communications));

        Assert.Contains("1 communication resolution", sut.StatusText, StringComparison.Ordinal);
        Assert.NotNull(navigation);
        Assert.Equal(Path.GetFullPath("main.jnx"), navigation!.Path);
        Assert.Equal(12, navigation.Line);
        Assert.Equal(5, navigation.Column);
    }

    [Fact]
    public async Task LoadAsync_WithEmptyResult_SetsNoChainStatusAndWritesInfo()
    {
        var output = new OutputService();
        var sut = new WhyPanelViewModel(
            new FakeWhyTraceQueryService(new WhyTraceResult(42, [], 0)),
            output)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        await sut.LoadAsync();

        Assert.Empty(sut.CausalChain);
        Assert.Equal("No causal chain returned for event 42.", sut.StatusText);
        Assert.False(sut.IsBusy);
        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Why" &&
            e.Severity == OutputSeverity.Info &&
            e.Message.Contains("No causal chain returned for event 42.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_WithEmptyResultAfterSuccess_ReplacesPreviousCausalChainAndWritesInfo()
    {
        var output = new OutputService();
        var service = new MutableWhyTraceQueryService
        {
            Result = new WhyTraceResult(
                42,
                [
                    new WhyTraceChainEntry(1, 7, "COMM_SEND", 10, "2026-01-01T10:00:00Z", "Smart Home Automation")
                ],
                1)
        };

        var sut = new WhyPanelViewModel(service, output)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        await sut.LoadAsync();

        Assert.Single(sut.CausalChain);

        service.Result = new WhyTraceResult(42, [], 0);

        await sut.LoadAsync();

        Assert.Empty(sut.CausalChain);
        Assert.Equal("No causal chain returned for event 42.", sut.StatusText);
        Assert.False(sut.IsBusy);
        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Why" &&
            e.Severity == OutputSeverity.Info &&
            e.Message.Contains("No causal chain returned for event 42.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_WithInvalidEventId_SetsValidationStatus()
    {
        var sut = new WhyPanelViewModel(
            new FakeWhyTraceQueryService(new WhyTraceResult(0, [], 0)),
            new OutputService())
        {
            EventIdText = "bad-id"
        };

        await sut.LoadAsync();

        Assert.Equal("Event ID must be an unsigned integer.", sut.StatusText);
        Assert.Empty(sut.CausalChain);
    }

    [Fact]
    public async Task LoadAsync_WithMissingBaseUrl_SetsValidationStatus()
    {
        var sut = new WhyPanelViewModel(
            new FakeWhyTraceQueryService(new WhyTraceResult(0, [], 0)),
            new OutputService())
        {
            BaseUrl = string.Empty,
            EventIdText = "42"
        };

        await sut.LoadAsync();

        Assert.Equal("TraceCollector URL is required.", sut.StatusText);
        Assert.Empty(sut.CausalChain);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task LoadAsync_WithInvalidEventIdAfterSuccess_ClearsPreviousCausalChain()
    {
        var output = new OutputService();
        var service = new FakeWhyTraceQueryService(
            new WhyTraceResult(
                42,
                [
                    new WhyTraceChainEntry(1, 7, "COMM_SEND", 10, "2026-01-01T10:00:00Z", "Smart Home Automation")
                ],
                1));

        var sut = new WhyPanelViewModel(service, output)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        await sut.LoadAsync();

        Assert.Single(sut.CausalChain);

        sut.EventIdText = "bad-id";

        await sut.LoadAsync();

        Assert.Equal("Event ID must be an unsigned integer.", sut.StatusText);
        Assert.Empty(sut.CausalChain);
    }

    [Fact]
    public async Task LoadAsync_WithMissingBaseUrlAfterSuccess_ClearsPreviousCausalChain()
    {
        var output = new OutputService();
        var service = new FakeWhyTraceQueryService(
            new WhyTraceResult(
                42,
                [
                    new WhyTraceChainEntry(1, 7, "COMM_SEND", 10, "2026-01-01T10:00:00Z", "Smart Home Automation")
                ],
                1));

        var sut = new WhyPanelViewModel(service, output)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        await sut.LoadAsync();

        Assert.Single(sut.CausalChain);

        sut.BaseUrl = string.Empty;

        await sut.LoadAsync();

        Assert.Equal("TraceCollector URL is required.", sut.StatusText);
        Assert.Empty(sut.CausalChain);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public async Task LoadAsync_WhenCancelled_SetsCancelledStatusWithoutError()
    {
        var output = new OutputService();
        var sut = new WhyPanelViewModel(
            new FakeWhyTraceQueryService(new WhyTraceResult(0, [], 0), throwOnCancellation: true),
            output)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await sut.LoadAsync(cts.Token);

        Assert.Equal("Why query cancelled.", sut.StatusText);
        Assert.Empty(sut.CausalChain);
        Assert.False(sut.IsBusy);
        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Why" &&
            e.Severity == OutputSeverity.Info &&
            e.Message.Contains("Why query cancelled.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_WhenServiceThrows_SetsFailureStatusAndWritesError()
    {
        var output = new OutputService();
        var sut = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(new InvalidOperationException("boom")),
            output)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        await sut.LoadAsync();

        Assert.Equal("Why query failed: boom", sut.StatusText);
        Assert.Empty(sut.CausalChain);
        Assert.False(sut.IsBusy);
        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Why" &&
            e.Severity == OutputSeverity.Error &&
            e.Message.Contains("Why query failed: boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_WhenServiceThrowsAfterSuccess_ClearsPreviousCausalChainAndWritesError()
    {
        var output = new OutputService();
        var service = new MutableOutcomeWhyTraceQueryService
        {
            Result = new WhyTraceResult(
                42,
                [
                    new WhyTraceChainEntry(1, 7, "COMM_SEND", 10, "2026-01-01T10:00:00Z", "Smart Home Automation")
                ],
                1)
        };

        var sut = new WhyPanelViewModel(service, output)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        await sut.LoadAsync();

        Assert.Single(sut.CausalChain);

        service.Exception = new InvalidOperationException("boom");

        await sut.LoadAsync();

        Assert.Equal("Why query failed: boom", sut.StatusText);
        Assert.Empty(sut.CausalChain);
        Assert.False(sut.IsBusy);
        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Why" &&
            e.Severity == OutputSeverity.Error &&
            e.Message.Contains("Why query failed: boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_WhenAlreadyBusy_DoesNotStartSecondQuery()
    {
        var output = new OutputService();
        var service = new BlockingWhyTraceQueryService();
        var sut = new WhyPanelViewModel(service, output)
        {
            BaseUrl = "http://localhost:8080",
            EventIdText = "42"
        };

        var firstLoad = sut.LoadAsync();
        await service.Started;

        Assert.True(sut.IsBusy);

        await sut.LoadAsync();

        Assert.Equal(1, service.CallCount);

        service.Complete(new WhyTraceResult(42, [], 0));
        await firstLoad;

        Assert.False(sut.IsBusy);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= timeout)
                throw new TimeoutException("Narrative workflow did not reach the expected state.");
            await Task.Delay(10);
        }
    }
}

public sealed class WhyPanelExtensionTests
{
    [Fact]
    public void Initialize_RegistersWhyToolPanel()
    {
        var capabilities = new CapabilityRegistry();
        capabilities.Register<IWhyTraceQueryService>(new FakeWhyTraceQueryService(new WhyTraceResult(0, [], 0)));
        var context = new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: new OutputService(),
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry(),
            capabilities: capabilities);

        var sut = new WhyPanelExtension();
        sut.Initialize(context);

        Assert.True(capabilities.TryGet<IWhyTraceQueryService>(out var service));
        Assert.NotNull(service);

        var panel = Assert.Single(context.Panels.GetRegisteredPanels(), p => p.Id == "junexis.narrative.why");
        Assert.Equal(PanelType.Tool, panel.Type);
        Assert.Equal("Why?", panel.Title);
        Assert.Equal(DockGroup.Bottom, panel.DockGroup);
        Assert.IsType<WhyPanelViewModel>(panel.Factory());
    }

    [Fact]
    public void Initialize_WithoutWhyTraceCapability_LogsInactiveAndSkipsPanelRegistration()
    {
        var output = new OutputService();
        var context = new HostContext(
            eventBus: new EventBus(),
            commands: new CommandRegistry(),
            panels: new PanelRegistry(),
            output: output,
            processes: new TestProcessService(),
            workspace: new WorkspaceService(),
            diagnostics: new DiagnosticService(),
            activeDocument: new ActiveDocumentService(),
            openDocuments: new TestOpenDocumentRepository(),
            versionControl: null,
            views: new TestViewRegistry(),
            capabilities: new CapabilityRegistry());

        var sut = new WhyPanelExtension();
        sut.Initialize(context);

        Assert.DoesNotContain(context.Panels.GetRegisteredPanels(), p => p.Id == "junexis.narrative.why");
        Assert.Contains(output.GetSnapshot(), e =>
            e.Channel == "Why" &&
            e.Message.Contains("inactive", StringComparison.OrdinalIgnoreCase));
    }
}
