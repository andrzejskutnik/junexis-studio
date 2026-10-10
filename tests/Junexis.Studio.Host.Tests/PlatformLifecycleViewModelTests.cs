using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class PlatformLifecycleViewModelTests
{
    [Fact]
    [Trait("Milestone", "M20.11.2")]
    public async Task PlanUpdate_DelegatesStructuredArgumentsWithoutWritingProfile()
    {
        using var fixture = new ProfileFixture();
        var process = new RecordingProcessService();
        using var sut = Create(process);
        sut.ProfilePath = fixture.Root;
        sut.CatalogPath = "catalog.json";
        sut.TrustStorePath = "trust.json";
        sut.CurrentManifestPath = "current.json";
        sut.TargetBundlePath = "target.zip";

        await sut.PlanUpdateAsync();

        var request = Assert.Single(process.Requests);
        Assert.Equal("junexis-test", request.FileName);
        Assert.Equal(["update", "plan", "--catalog", "catalog.json", "--trust-store", "trust.json",
            "--current-manifest", "current.json", "--target-bundle", "target.zip", "--json",
            "--profile", fixture.Root], request.ArgumentList);
        Assert.Equal("wartość użytkownika", File.ReadAllText(fixture.Marker));
    }

    [Fact]
    [Trait("Milestone", "M20.11.2")]
    public async Task History_UsesOnlyPublicReadCommandAndPresentsProgress()
    {
        var process = new RecordingProcessService
        {
            Lines = [new ProcessOutputLine(ProcessOutputKind.StdOut,
                "{\"schemaVersion\":\"junexis.update-history/0.1\",\"operations\":[]}")]
        };
        using var sut = Create(process);

        await sut.RefreshHistoryAsync();

        var request = Assert.Single(process.Requests);
        Assert.Equal(["update", "history", "--json"], request.ArgumentList);
        Assert.Contains("junexis.update-history/0.1", sut.LogText, StringComparison.Ordinal);
        Assert.Contains("odświeżone", sut.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Milestone", "M20.11.2")]
    public async Task Repair_RequiresSuccessfulCheckForUnchangedProfileAndSource()
    {
        var process = new RecordingProcessService();
        using var sut = Create(process);
        sut.ProfilePath = "profile-a";
        sut.RepairOfflineSource = "mirror-a";

        await sut.ApplyRepairAsync();
        Assert.Empty(process.Requests);

        await sut.CheckRepairAsync();
        Assert.True(sut.CanApplyRepair);
        sut.RepairOfflineSource = "mirror-b";
        Assert.False(sut.CanApplyRepair);
        await sut.CheckRepairAsync();
        await sut.ApplyRepairAsync();

        Assert.Collection(process.Requests,
            request => Assert.Equal(["repair", "check", "--json", "--profile", "profile-a"], request.ArgumentList),
            request => Assert.Equal(["repair", "check", "--json", "--profile", "profile-a"], request.ArgumentList),
            request => Assert.Equal(["repair", "apply", "--profile", "profile-a",
                "--offline-source", "mirror-b"], request.ArgumentList));
        Assert.False(sut.CanApplyRepair);
    }

    [Fact]
    [Trait("Milestone", "M20.11.2")]
    public async Task Rollback_RequiresSuccessfulPlanAndDelegatesApply()
    {
        var process = new RecordingProcessService();
        using var sut = Create(process);
        sut.CatalogPath = "catalog.json";
        sut.TrustStorePath = "trust.json";

        await sut.PlanRollbackAsync();
        Assert.True(sut.CanApplyRollback);
        await sut.ApplyRollbackAsync();

        Assert.Collection(process.Requests,
            request => Assert.Equal(["update", "rollback", "--catalog", "catalog.json",
                "--trust-store", "trust.json", "--json"], request.ArgumentList),
            request => Assert.Equal(["update", "rollback", "--catalog", "catalog.json",
                "--trust-store", "trust.json", "--apply", "--json"], request.ArgumentList));
        Assert.False(sut.CanApplyRollback);
    }

    [Fact]
    [Trait("Milestone", "M20.11.2")]
    public async Task Cancel_StopsDelegatedProcessAndReturnsPanelToIdle()
    {
        var process = new RecordingProcessService { BlockUntilCancelled = true };
        using var sut = Create(process);

        var operation = sut.RefreshHistoryAsync();
        await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(sut.IsBusy);

        sut.Cancel();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(sut.IsBusy);
        Assert.False(sut.CanCancel);
        Assert.Contains("anulowane", sut.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Milestone", "M20.11.2")]
    [Trait("Milestone", "M20.11.3")]
    public async Task Output_HidesSecretsAndHomePathsBeforeShowingThem()
    {
        var output = new OutputService();
        var process = new RecordingProcessService
        {
            Lines = [new ProcessOutputLine(ProcessOutputKind.StdErr,
                "token=very-secret Authorization: Bearer bearer-secret "
                + "https://user:password@example.test/a?api_key=query-secret "
                + "C:\\Users\\alicja\\private.txt /var/lib/junexis/private.txt")]
        };
        using var sut = new PlatformLifecycleViewModel(process, new FixedToolResolver(), output);

        await sut.RefreshHistoryAsync();

        Assert.DoesNotContain("very-secret", sut.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-secret", sut.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("user:password", sut.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("query-secret", sut.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("alicja", sut.LogText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/var/lib/junexis", sut.LogText, StringComparison.Ordinal);
        var sharedOutput = string.Join('\n', output.GetSnapshot().Select(item => item.Message));
        Assert.DoesNotContain("very-secret", sharedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-secret", sharedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("query-secret", sharedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("/var/lib/junexis", sharedOutput, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Milestone", "M20.11.3")]
    public async Task ProcessFailure_IsRedactedBeforePanelAndSharedOutput()
    {
        var output = new OutputService();
        var process = new RecordingProcessService
        {
            Failure = new IOException("token=failure-secret /tmp/private/profile.json")
        };
        using var sut = new PlatformLifecycleViewModel(process, new FixedToolResolver(), output);

        await sut.RefreshHistoryAsync();

        var visible = sut.LogText + "\n" + string.Join('\n',
            output.GetSnapshot().Select(item => item.Message));
        Assert.DoesNotContain("failure-secret", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/private", visible, StringComparison.Ordinal);
        Assert.Contains("[ukryto]", visible, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Milestone", "M20.11.3")]
    public async Task NewSession_TakesOverSupervisionThroughHistoryWithoutResumingMutation()
    {
        var process = new RecordingProcessService
        {
            Lines = [new ProcessOutputLine(ProcessOutputKind.StdOut,
                "{\"schemaVersion\":\"junexis.update-history/0.1\",\"operations\":[{"
                + "\"operationId\":\"update-interrupted-1\",\"result\":\"recovery-required\","
                + "\"lastPhase\":\"health-checking\"}]}")]
        };
        using var sut = Create(process);

        await sut.TakeOverInterruptedAsync();

        Assert.Equal("update-interrupted-1", sut.ObservedOperationId);
        Assert.Contains("wymaga naprawy", sut.RecoveryText, StringComparison.OrdinalIgnoreCase);
        var request = Assert.Single(process.Requests);
        Assert.Equal(["update", "history", "--json"], request.ArgumentList);
        Assert.DoesNotContain("apply", request.ArgumentList!);
    }

    [Fact]
    [Trait("Milestone", "M20.11.3")]
    public async Task TakeOver_RejectsAmbiguousHistoryWithoutExposingOperation()
    {
        var process = new RecordingProcessService
        {
            Lines = [new ProcessOutputLine(ProcessOutputKind.StdOut,
                "{\"schemaVersion\":\"junexis.update-history/0.1\","
                + "\"schemaVersion\":\"foreign\",\"operations\":[]}")]
        };
        using var sut = Create(process);

        await sut.TakeOverInterruptedAsync();

        Assert.Null(sut.ObservedOperationId);
        Assert.Contains("bezpiecznego formatu", sut.RecoveryText, StringComparison.OrdinalIgnoreCase);
    }

    private static PlatformLifecycleViewModel Create(IProcessService processes) =>
        new(processes, new FixedToolResolver(), new OutputService());

    private sealed class FixedToolResolver : IPlatformToolResolver
    {
        public string ResolveCommand(string commandName, string? overrideEnvironmentVariable = null) =>
            "junexis-test";
    }

    private sealed class RecordingProcessService : IProcessService
    {
        internal List<ProcessRequest> Requests { get; } = [];
        internal IReadOnlyList<ProcessOutputLine> Lines { get; init; } = [];
        internal bool BlockUntilCancelled { get; init; }
        internal Exception? Failure { get; init; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProcessResult> RunAsync(ProcessRequest request,
            Action<ProcessOutputLine>? listener, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Started.TrySetResult();
            if (Failure is not null) throw Failure;
            if (BlockUntilCancelled)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            foreach (var line in Lines) listener?.Invoke(line);
            return new ProcessResult(0, TimeSpan.FromMilliseconds(1));
        }
    }

    private sealed class ProfileFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(),
            "junexis-studio-lifecycle-" + Guid.NewGuid().ToString("N"));
        internal string Marker => Path.Combine(Root, "user-project.txt");

        internal ProfileFixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Marker, "wartość użytkownika");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
