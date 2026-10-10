using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class RealNarrativeSessionSmokeTests
{
    [Fact]
    [Trait("Category", "RealNarrative")]
    [Trait("Issue", "25")]
    [Trait("Issue", "26")]
    [Trait("Issue", "27")]
    [Trait("Issue", "28")]
    [Trait("Issue", "29")]
    [Trait("Issue", "30")]
    [Trait("Issue", "31")]
    [Trait("Issue", "32")]
    [Trait("Issue", "33")]
    [Trait("Issue", "34")]
    [Trait("Issue", "35")]
    [Trait("Issue", "36")]
    public async Task ManagedSession_StartsRealCollectorSelectsLatestEventAndStopsIt()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("JUNEXIS_REAL_NARRATIVE_SMOKE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var collectorPath = RequireEnvironmentPath("JUNEXIS_TRACE_COLLECTOR");
        var debugManifest = RequireEnvironmentPath("JUNEXIS_NARRATIVE_DEBUG_MANIFEST");
        var buildId = Environment.GetEnvironmentVariable("JUNEXIS_NARRATIVE_BUILD_ID")
            ?? throw new InvalidOperationException("JUNEXIS_NARRATIVE_BUILD_ID is required.");
        var runtimeBundleId = new string('e', 64);
        var projectRoot = Path.GetDirectoryName(debugManifest)!;
        var receipt = new LatestBuildReceipt(
            Path.Combine(projectRoot, "latest-build.json"),
            buildId,
            "project",
            projectRoot,
            Path.Combine(projectRoot, "main.jnx"),
            projectRoot,
            ["server"],
            Path.Combine(projectRoot, "bundle.json"),
            Path.Combine(projectRoot, "artifacts.json"),
            debugManifest,
            Path.Combine(projectRoot, "analysis.json"));
        var eventBus = new EventBus();
        await using var session = new NarrativeSessionService(
            eventBus,
            new OutputService(),
            collectorPath);

        var start = session.StartAsync(receipt);
        var runtimeEnvironmentTask = new NarrativeRuntimeEnvironmentService(session)
            .WaitForCurrentAsync(TimeSpan.FromSeconds(15));
        await start;

        Assert.Equal(NarrativeSessionState.Ready, session.Snapshot.State);
        Assert.NotNull(session.Snapshot.BaseUrl);
        var runtimeEnvironment = await runtimeEnvironmentTask;
        Assert.NotNull(runtimeEnvironment);
        Assert.Equal(session.Snapshot.BaseUrl, runtimeEnvironment.TraceUrl);
        Assert.Equal(buildId, runtimeEnvironment.BuildId);
        Assert.Equal(buildId,
            runtimeEnvironment.ToDebugAdapterVariables()[NarrativeRuntimeEnvironment.BuildIdVariable]);
        var debugSession = new NarrativeDebugSessionStub();
        var workspace = new WorkspaceService();
        workspace.OpenFolder(projectRoot);
        using var http = new HttpClient { BaseAddress = new Uri(session.Snapshot.BaseUrl!) };
        var taskJson = await http.GetFromJsonAsync<JsonElement>("/manifest/tasks/1");
        var taskSymbolId = taskJson.GetProperty("symbolId").GetString()
            ?? throw new InvalidDataException("Real Narrative task must expose a stable symbol.");
        using var whyPanel = new WhyPanelViewModel(
            new HttpWhyTraceQueryService(),
            new OutputService(),
            eventBus: eventBus,
            debugSession: debugSession,
            mentor: new RealSmokeMentorService(buildId, taskSymbolId),
            narrativeSession: session,
            workspace: workspace);
        eventBus.Publish(new AnalysisSnapshotUpdatedEvent(
            "junexis",
            buildId,
            receipt.AnalysisSnapshotPath));
        using var samplingResponse = await http.PostAsJsonAsync("/sampling/mode", new
        {
            mode = "Disabled"
        });
        samplingResponse.EnsureSuccessStatusCode();
        using var response = await http.PostAsJsonAsync("/events", new
        {
            taskId = 1,
            kind = "SCHED_ENQ",
            lamportClock = 1,
            attributes = new Dictionary<string, string>
            {
                ["build_id"] = buildId,
                ["bundle_id"] = runtimeBundleId,
                ["narrative_id"] = "ManagedSession"
            }
        });
        response.EnsureSuccessStatusCode();

        await session.RefreshAsync();
        await WaitUntilAsync(
            () => whyPanel.DiagnosticStory?.TargetEventId == 1,
            TimeSpan.FromSeconds(5));

        Assert.Equal((ulong)1, session.Snapshot.LatestEventId);
        Assert.Equal(1, session.Snapshot.TotalEvents);
        Assert.Equal(runtimeBundleId, whyPanel.DiagnosticStory?.RuntimeBundleId);
        Assert.Equal(NarrativeRuntimeActivity.Live, session.Snapshot.Activity);
        Assert.Equal(1, session.Snapshot.EventDelta);
        Assert.NotNull(session.Snapshot.LastEventObservedAt);
        Assert.Equal("1", whyPanel.EventIdText);
        Assert.Contains("loaded automatically", whyPanel.NarrativeActivityText, StringComparison.Ordinal);

        whyPanel.NarrativeFilterText = "ManagedSession";
        whyPanel.EventKindFilterText = "SCHED_ENQ";
        whyPanel.PauseRuntimeFollow();
        using var filteredOutResponse = await http.PostAsJsonAsync("/events", new
        {
            taskId = 1,
            kind = "SCHED_DEQ",
            lamportClock = 2,
            attributes = new Dictionary<string, string>
            {
                ["build_id"] = buildId,
                ["bundle_id"] = runtimeBundleId,
                ["narrative_id"] = "OtherSession"
            }
        });
        filteredOutResponse.EnsureSuccessStatusCode();
        await session.RefreshAsync();
        Assert.Equal("2", whyPanel.EventIdText);
        Assert.True(whyPanel.RuntimeFollowPaused);

        await whyPanel.ResumeRuntimeFollowAsync();
        await WaitUntilAsync(
            () => whyPanel.EventIdText == "1"
                  && whyPanel.NarrativeActivityText.Contains("loaded automatically", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        var source = taskJson.GetProperty("source");
        var frame = new DebugStackFrame(
            1,
            taskJson.GetProperty("displayName").GetString() ?? "Task 1",
            source.GetProperty("file").GetString(),
            source.GetProperty("line").GetInt32(),
            source.GetProperty("column").GetInt32(),
            JunexisSymbolId: taskJson.GetProperty("symbolId").GetString());
        debugSession.Publish(new DebugSessionSnapshot(
            DebugSessionState.Stopped,
            "Stopped",
            1,
            [new DebugThread(1, "main")],
            [frame]));
        await WaitUntilAsync(
            () => whyPanel.DebugCorrelation is { Matched: true, EventId: 1 }
                  && whyPanel.DiagnosticStory?.TargetEventId == 1,
            TimeSpan.FromSeconds(5));

        Assert.Equal("symbol-and-source", whyPanel.DebugCorrelation?.Confidence);

        using var historyResponse = await http.PostAsJsonAsync("/events", new
        {
            taskId = 1,
            kind = "SCHED_ENQ",
            lamportClock = 3,
            attributes = new Dictionary<string, string>
            {
                ["build_id"] = buildId,
                ["bundle_id"] = runtimeBundleId,
                ["narrative_id"] = "ManagedSession"
            }
        });
        historyResponse.EnsureSuccessStatusCode();
        await session.RefreshAsync();
        await WaitUntilAsync(
            () => whyPanel.DiagnosticStory?.TargetEventId == 3,
            TimeSpan.FromSeconds(5));
        whyPanel.PinCurrentStory();

        await whyPanel.NavigateHistoryAsync(previous: true);
        whyPanel.CompareWithPinnedStory();

        Assert.Equal((ulong)3, whyPanel.PinnedDiagnosticStory?.TargetEventId);
        Assert.Equal((ulong)1, whyPanel.DiagnosticStory?.TargetEventId);
        Assert.Equal((ulong)3, whyPanel.StoryComparison?.PinnedEventId);
        Assert.Equal((ulong)1, whyPanel.StoryComparison?.CurrentEventId);
        Assert.True(whyPanel.StoryComparison?.SameBuild);
        Assert.Equal((ulong)1, whyPanel.DebugCorrelation?.EventId);

        whyPanel.BookmarkNameText = "M6.11 runtime regression";
        whyPanel.SaveBookmark();
        Assert.True(File.Exists(Path.Combine(
            projectRoot,
            ".junexis",
            "narrative-session.json")));
        using var reopened = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Offline bookmark must not call Collector.")),
            new OutputService(),
            workspace: workspace);
        reopened.SelectedBookmark = Assert.Single(reopened.Bookmarks);
        reopened.OpenSelectedBookmark();
        Assert.Equal((ulong)1, reopened.DiagnosticStory?.TargetEventId);

        var bundlePath = Path.Combine(projectRoot, "m6.11-diagnostic-session.zip");
        await whyPanel.ExportDiagnosticSessionAsync(bundlePath);
        using (var bundle = ZipFile.OpenRead(bundlePath))
        {
            Assert.Contains(bundle.Entries, entry => entry.FullName == "manifest.json");
            Assert.Contains(bundle.Entries, entry => entry.FullName == "session.json");
            Assert.Contains(bundle.Entries, entry =>
                entry.FullName.StartsWith("bookmarks/", StringComparison.Ordinal));
        }
        var signingKeyPath = Path.Combine(projectRoot, "m6.14-signing-key.pem");
        var signedBundlePath = Path.Combine(projectRoot, "m6.14-diagnostic-session.zip");
        var trustStorePath = Path.Combine(projectRoot, "m6.14-local-trust.json");
        using (var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            await File.WriteAllTextAsync(
                signingKeyPath,
                signingKey.ExportPkcs8PrivateKeyPem());
        await whyPanel.ExportSignedDiagnosticSessionAsync(
            signedBundlePath,
            signingKeyPath,
            "Junexis E2E signer");
        using (var signedImport = new WhyPanelViewModel(
                   new ThrowingWhyTraceQueryService(
                       new InvalidOperationException("Signed bundle import must remain offline.")),
                   new OutputService(),
                   workspace: workspace,
                   diagnosticTrustStorePath: trustStorePath))
        {
            await signedImport.ImportDiagnosticSessionAsync(signedBundlePath);
            Assert.Equal(
                NarrativeDiagnosticAuthenticityStatus.SignatureVerified,
                signedImport.ImportedSession?.Signature.Status);
            signedImport.TrustImportedSigner();
            Assert.Equal(
                NarrativeDiagnosticAuthenticityStatus.TrustedSigner,
                signedImport.ImportedSession?.Signature.Status);
        }
        using (var trustedImport = new WhyPanelViewModel(
                   new ThrowingWhyTraceQueryService(
                       new InvalidOperationException("Trusted bundle import must remain offline.")),
                   new OutputService(),
                   workspace: workspace,
                   diagnosticTrustStorePath: trustStorePath))
        {
            await trustedImport.ImportDiagnosticSessionAsync(signedBundlePath);
            Assert.Equal(
                NarrativeDiagnosticAuthenticityStatus.TrustedSigner,
                trustedImport.ImportedSession?.Signature.Status);
            trustedImport.ReviewAuthorText = "Junexis E2E reviewer";
            trustedImport.ReviewText =
                "Runtime ordering is supported by the verified Narrative evidence.";
            trustedImport.ReviewKind = NarrativeDiagnosticReviewKind.Observation;
            trustedImport.AddReviewEntry();
            trustedImport.GenerateMentorReviewProposals();
            Assert.NotEmpty(trustedImport.MentorReviewProposals);
            trustedImport.SelectedMentorReviewProposal =
                trustedImport.MentorReviewProposals[0];
            trustedImport.AcceptSelectedMentorReviewProposal();
            Assert.Contains(
                trustedImport.ReviewEntries,
                entry => entry.Source == NarrativeDiagnosticReviewSource.Mentor);
            Assert.DoesNotContain(
                trustedImport.ReviewEntries,
                entry => entry.Source == NarrativeDiagnosticReviewSource.Mentor
                    && entry.Kind == NarrativeDiagnosticReviewKind.Decision);
            trustedImport.ReviewStatus = NarrativeDiagnosticReviewStatus.Resolved;
            trustedImport.SaveReviewRevision();
            await trustedImport.SignReviewAsync(signingKeyPath, "Junexis E2E signer");
            Assert.Contains(
                "TrustedSigner",
                trustedImport.ReviewSignatureText,
                StringComparison.Ordinal);
            var casePath = Path.Combine(projectRoot, "m6.15-diagnostic-case.zip");
            await trustedImport.ExportDiagnosticCaseAsync(casePath);
            using var caseImport = new WhyPanelViewModel(
                new ThrowingWhyTraceQueryService(
                    new InvalidOperationException("Diagnostic case import must remain offline.")),
                new OutputService(),
                workspace: workspace,
                diagnosticTrustStorePath: trustStorePath);
            await caseImport.ImportDiagnosticCaseAsync(casePath);
            Assert.Equal(
                trustedImport.ImportedSession?.BundleId,
                caseImport.ImportedSession?.BundleId);
            Assert.Single(caseImport.DiagnosticReview?.Revisions ?? []);
            Assert.Equal(
                NarrativeDiagnosticReviewStatus.Resolved,
                caseImport.DiagnosticReview?.Status);
            Assert.Contains(
                "TrustedSigner",
                caseImport.ReviewSignatureText,
                StringComparison.Ordinal);
        }
        using var imported = new WhyPanelViewModel(
            new ThrowingWhyTraceQueryService(
                new InvalidOperationException("Verified bundle import must not call Collector.")),
            new OutputService(),
            workspace: workspace);
        await imported.ImportDiagnosticSessionAsync(bundlePath);
        Assert.NotNull(imported.ImportedSession);
        Assert.False(imported.CanEditBookmarks);
        Assert.Equal((ulong)1, imported.DiagnosticStory?.TargetEventId);
        Assert.Equal((ulong)3, imported.PinnedDiagnosticStory?.TargetEventId);
        Assert.Equal((ulong)1, imported.StoryComparison?.CurrentEventId);
        Assert.Contains("read-only", imported.DiagnosticSessionModeText, StringComparison.Ordinal);
        Assert.Equal(
            imported.ImportedSession?.VerifiedFileCount,
            imported.ImportedEvidenceFiles.Count);
        Assert.Contains(
            "Integrity · verified SHA-256",
            imported.ImportedSessionSummaryText,
            StringComparison.Ordinal);

        await whyPanel.ImportDiagnosticSessionAsync(bundlePath);
        whyPanel.CompareImportedWithLive();
        Assert.True(whyPanel.CanCompareImportedWithLive);
        Assert.True(whyPanel.CrossSessionComparison?.SameBuild);
        Assert.Equal((ulong)1, whyPanel.CrossSessionComparison?.ImportedEventId);
        Assert.Equal((ulong)1, whyPanel.CrossSessionComparison?.LiveEventId);
        await session.StopAsync();
        Assert.Equal(NarrativeSessionState.Inactive, session.Snapshot.State);
    }

    private static string RequireEnvironmentPath(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value) || !File.Exists(value))
            throw new InvalidOperationException($"{variable} must reference an existing file.");
        return Path.GetFullPath(value);
    }

    private sealed class RealSmokeMentorService(
        string buildId,
        string relatedSymbolId) : IMentorAnalysisService
    {
        public Task<MentorAnalysisResult> AnalyzeAsync(
            string snapshotPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MentorAnalysisResult(
                buildId,
                snapshotPath,
                [
                    new MentorFinding(
                        "M1600",
                        "info",
                        "The managed runtime task has evidence available for review.",
                        relatedSymbolId,
                        null,
                        "The exact compiler build and runtime symbol are present in the Story.",
                        "Inspect the task boundary before resolving the case.")
                ],
                SchemaVersion: "1.1"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Managed runtime event was not followed automatically.");
            await Task.Delay(25);
        }
    }
}
