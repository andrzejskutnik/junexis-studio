using System.Collections.Concurrent;
using System.Text.Json;
using Junexis.Observability.Contracts;
using Junexis.Observability.Ingress;
using Junexis.Studio.Abstractions;

namespace JunexisLang.Studio.CompilerExtension.Tests;

public sealed class JunexisDeploymentServiceTests
{
    [Fact]
    public void Evaluate_ProjectBuildWithServerAndEmbedded_ReportsDeployableAndBlockedTargets()
    {
        using var fixture = new DeploymentFixture();
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions(), new FakeProcessService());

        var readiness = sut.Evaluate(fixture.Receipt with { Targets = ["server", "embedded"] });

        Assert.True(readiness.HasVerifiedBuild);
        Assert.True(readiness.CanPrepare);
        Assert.Equal(["server"], readiness.DeployableTargets);
        Assert.Equal(["embedded"], readiness.BlockedTargets);
    }

    [Fact]
    public async Task PrepareAndRunLocalAsync_UsesPackagePreflightAndLocalCommands()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root);
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, process);
        var lines = new ConcurrentQueue<string>();

        var preparation = await sut.PrepareAsync(
            fixture.Receipt,
            line => lines.Enqueue(line.Text));
        var local = await sut.RunLocalAsync(
            preparation,
            durationSeconds: 2,
            line => lines.Enqueue(line.Text));

        Assert.Equal(fixture.BuildId, preparation.BuildId);
        Assert.Equal("ready", preparation.TechnicalStatus);
        Assert.Equal(["report", "server"], preparation.Targets);
        Assert.True(File.Exists(preparation.PreflightReportPath));
        Assert.True(local.Success);
        Assert.Contains("completed successfully", local.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("deployment-receipt.json", local.DeploymentReceiptPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, local.TargetStates.Count);
        Assert.Contains(local.TargetStates, target => target.Target == "server" && target.Status == "ready");
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("package") == true);
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("preflight") == true);
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("local") == true);
        Assert.DoesNotContain(process.Requests, request => request.ArgumentList?.Contains("--receipt-out") == true);
        Assert.Contains(lines, line => line.Contains("[package]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("[preflight]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("[local]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrepareAsync_RejectsArtifactsProducedForDifferentBuild()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root)
        {
            ProducedBuildId = new string('d', 64)
        };
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, process);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.PrepareAsync(fixture.Receipt));

        Assert.Contains("different build", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Issue", "M7.9")]
    public async Task PrepareAsync_PropagatesSigningAndTrustedAdmissionPolicy()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root);
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, process);
        var fingerprint = new string('a', 64);

        var preparation = await sut.PrepareAsync(
            fixture.Receipt,
            new DeploymentAuthenticityPolicy(
                "trusted",
                Path.Combine(fixture.Root, "external-key.pem"),
                "Release signer",
                [fingerprint]));

        Assert.Equal("trusted", preparation.AuthenticityStatus);
        Assert.Equal("trusted", preparation.AuthenticityPolicy);
        Assert.True(preparation.AuthenticityPolicySatisfied);
        Assert.Equal(fingerprint, preparation.SignerFingerprint);
        Assert.Contains(process.Requests, request =>
            request.ArgumentList?.Contains("--sign-key") == true
            && request.ArgumentList.Contains("--signer"));
        Assert.Contains(process.Requests, request =>
            request.ArgumentList?.Contains("--auth-policy") == true
            && request.ArgumentList.Contains("trusted")
            && request.ArgumentList.Contains("--trusted-signer")
            && request.ArgumentList.Contains(fingerprint));
    }

    [Fact]
    public async Task EmbeddedFlow_UsesProbeExplicitSelectionAndVerifiedPackage()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root);
        var sut = new JunexisDeploymentService(
            new JunexisCompilerOptions { CliPath = "junexis", PlatformIoExecutable = "pio-test" },
            process);
        var receipt = fixture.Receipt with { Targets = ["embedded"] };

        var probe = await sut.ProbeEmbeddedAsync();
        var device = Assert.Single(probe.Devices);
        var selection = await sut.SelectEmbeddedAsync(receipt, device);
        var preparation = await sut.PrepareEmbeddedAsync(receipt);
        var flash = await sut.FlashEmbeddedAsync(preparation, selection);

        Assert.True(probe.ToolAvailable);
        Assert.True(probe.ProbeSucceeded);
        Assert.Equal("COM7", selection.Port);
        Assert.Equal(fixture.BuildId, preparation.BuildId);
        Assert.True(flash.Success);
        Assert.Equal(fixture.BuildId, flash.BuildId);
        Assert.Equal(preparation.BundleId, flash.BundleId);
        Assert.Equal(selection.Port, flash.Port);
        Assert.True(File.Exists(flash.ReceiptPath));
        Assert.Contains(process.Requests, request =>
            request.ArgumentList?.Contains("probe") == true
            && request.ArgumentList.Contains("pio-test"));
        Assert.Contains(process.Requests, request =>
            request.ArgumentList?.Contains("select") == true
            && request.ArgumentList.Contains("--port")
            && request.ArgumentList.Contains("COM7"));
        Assert.Contains(process.Requests, request =>
            request.ArgumentList?.Contains("package") == true
            && request.ArgumentList.Contains("embedded"));
        Assert.Contains(process.Requests, request =>
            request.ArgumentList?.Contains("flash") == true
            && request.ArgumentList.Contains(selection.SelectionPath)
            && request.ArgumentList.Contains("--auth-policy")
            && request.ArgumentList.Contains("permissive"));
    }

    [Fact]
    public async Task PrepareEmbeddedAsync_RejectsPackageFromDifferentBuild()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root)
        {
            ProducedBuildId = new string('d', 64)
        };
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, process);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.PrepareEmbeddedAsync(fixture.Receipt with { Targets = ["embedded"] }));

        Assert.Contains("different verified build", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FlashEmbeddedAsync_RejectsSelectionFromDifferentDeploymentState()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root);
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, process);
        var receipt = fixture.Receipt with { Targets = ["embedded"] };
        var preparation = await sut.PrepareEmbeddedAsync(receipt);
        var foreignRoot = Path.Combine(fixture.Root, ".junexis", "state", "deploy", new string('d', 64), "embedded");
        Directory.CreateDirectory(foreignRoot);
        var foreignSelectionPath = Path.Combine(foreignRoot, "embedded-device-selection.json");
        File.WriteAllText(foreignSelectionPath, "{}");
        var selection = new EmbeddedDeviceSelection(foreignSelectionPath, "COM7", null, null, null, null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.FlashEmbeddedAsync(preparation, selection));

        Assert.Contains("same verified deployment state", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(process.Requests, request => request.ArgumentList?.Contains("flash") == true);
    }

    [Fact]
    public async Task TryLoadLastLocalAsync_ReadsDeterministicReceipt()
    {
        using var fixture = new DeploymentFixture();
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, new FakeProcessService(fixture.Root));
        var deploymentRoot = Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId);
        Directory.CreateDirectory(deploymentRoot);
        var receiptPath = Path.Combine(deploymentRoot, "deployment-receipt.json");
        WriteDeploymentReceipt(receiptPath, fixture.BuildId);

        var restored = await sut.TryLoadLastLocalAsync(fixture.Receipt);

        Assert.NotNull(restored);
        Assert.Equal(receiptPath, restored!.DeploymentReceiptPath);
        Assert.Equal("succeeded", restored.LifecycleStatus);
        Assert.Equal(["report", "server"], restored.Targets);
        Assert.Contains(restored.TargetStates, target => target.Target == "report" && target.Status == "ready");
    }

    [Fact]
    public async Task TryRestorePreparationAsync_ReusesDeterministicPreparedState()
    {
        using var fixture = new DeploymentFixture();
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, new FakeProcessService(fixture.Root));
        var deploymentRoot = Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId);
        Directory.CreateDirectory(deploymentRoot);
        var receiptPath = Path.Combine(deploymentRoot, "deployment-receipt.json");
        File.WriteAllText(Path.Combine(deploymentRoot, "system-bundle.zip"), "bundle");
        WriteDeploymentReceipt(receiptPath, fixture.BuildId);

        var restored = await sut.TryRestorePreparationAsync(fixture.Receipt);

        Assert.NotNull(restored);
        Assert.Equal(fixture.BuildId, restored!.BuildId);
        Assert.Equal(Path.Combine(deploymentRoot, "system-bundle.zip"), restored.BundlePath);
        Assert.Equal("ready", restored.TechnicalStatus);
    }

    [Fact]
    public async Task TryLoadLiveLocalAsync_RenewsLease_ForCurrentStudioOwner()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root);
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, process);
        var deploymentRoot = Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId);
        Directory.CreateDirectory(deploymentRoot);
        File.WriteAllText(Path.Combine(deploymentRoot, "deployment-session.json"), "{}");
        var ownerPath = Path.Combine(fixture.Root, ".junexis", "state", "deploy", "studio-owner-instance-id.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ownerPath)!);
        File.WriteAllText(ownerPath, "studio-instance");

        var live = await sut.TryLoadLiveLocalAsync(fixture.Receipt);

        Assert.NotNull(live);
        Assert.True(live!.OwnedByCurrentStudio);
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("status") == true);
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("adopt") == true);
    }

    [Fact]
    public async Task AdoptLiveLocalAsync_UsesExplicitStudioIdentity()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root);
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, process);
        var deploymentRoot = Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId);
        Directory.CreateDirectory(deploymentRoot);
        File.WriteAllText(Path.Combine(deploymentRoot, "deployment-session.json"), "{}");
        var ownerPath = Path.Combine(fixture.Root, ".junexis", "state", "deploy", "studio-owner-instance-id.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ownerPath)!);
        File.WriteAllText(ownerPath, "studio-instance");

        var live = await sut.AdoptLiveLocalAsync(fixture.Receipt);

        Assert.Equal("junexis-studio", live.OwnerTool);
        Assert.Equal("studio-instance", live.OwnerInstanceId);
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("--owner-tool") == true);
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("--owner-instance-id") == true);
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("--owner-process-id") == true);
    }

    [Fact]
    public async Task StopLiveLocalAsync_UsesExplicitStudioIdentity()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root);
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, process);
        var deploymentRoot = Path.Combine(fixture.Root, ".junexis", "state", "deploy", fixture.BuildId);
        Directory.CreateDirectory(deploymentRoot);
        File.WriteAllText(Path.Combine(deploymentRoot, "deployment-session.json"), "{}");
        var ownerPath = Path.Combine(fixture.Root, ".junexis", "state", "deploy", "studio-owner-instance-id.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ownerPath)!);
        File.WriteAllText(ownerPath, "studio-instance");

        var live = await sut.StopLiveLocalAsync(fixture.Receipt);

        Assert.Equal("stopped", live.State);
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("stop") == true);
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("--owner-tool") == true);
        Assert.Contains(process.Requests, request => request.ArgumentList?.Contains("--owner-instance-id") == true);
    }

    [Fact]
    public async Task RunLocalAsync_PublishesDeploymentReceipt_WhenIngressGatewayIsProvided()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root);
        var sink = new RecordingIngressSink();
        var gateway = new EventIngressGateway(new FixedIngressClock(new DateTimeOffset(2026, 7, 31, 19, 0, 0, TimeSpan.Zero)), sink);
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, process, gateway);

        var preparation = await sut.PrepareAsync(fixture.Receipt);
        var result = await sut.RunLocalAsync(preparation, durationSeconds: 2);

        Assert.True(result.Success);
        Assert.Contains(sink.AuditRecords, record => record.Action == "deploy.local.completed");
        Assert.Contains(sink.HealthSignals, signal => signal.Scope == "deploy.target");
    }

    [Fact]
    public async Task RunLocalAsync_PublishesFailedDeploymentReceipt_WhenCliFails()
    {
        using var fixture = new DeploymentFixture();
        var process = new FakeProcessService(fixture.Root) { LocalExitCode = 7 };
        var sink = new RecordingIngressSink();
        var gateway = new EventIngressGateway(new FixedIngressClock(DateTimeOffset.UtcNow), sink);
        var sut = new JunexisDeploymentService(new JunexisCompilerOptions { CliPath = "junexis" }, process, gateway);
        var preparation = await sut.PrepareAsync(fixture.Receipt);

        var result = await sut.RunLocalAsync(preparation, durationSeconds: 2);

        Assert.False(result.Success);
        Assert.Contains(sink.AuditRecords, record => record.Action == "deploy.local.failed");
    }

    private static void WriteDeploymentReceipt(string receiptPath, string buildId, int exitCode = 0)
    {
        var directory = Path.GetDirectoryName(receiptPath)!;
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "junexis.deployment-receipt/0.1",
            bundleId = new string('c', 64),
            buildId,
            packageReceiptPath = Path.Combine(directory, "system-bundle.zip.package-receipt.json"),
            preflightReportPath = Path.Combine(directory, "deployment-preflight.json"),
            workDirectory = Path.Combine(directory, "work"),
            targets = new[]
            {
                new { target = "report", status = "ready", details = "Reported runtime readiness." },
                new { target = "server", status = "ready", details = "HTTP http://127.0.0.1:7100" }
            },
            lifecycle = new
            {
                status = exitCode == 0 ? "succeeded" : "failed",
                exitCode,
                summary = exitCode == 0
                    ? "Bounded local deployment completed successfully."
                    : "Bounded local deployment failed.",
                durationSeconds = 2
            }
        }));
        File.WriteAllText(Path.Combine(directory, "system-bundle.zip.package-receipt.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "deployment-preflight.json"), JsonSerializer.Serialize(new
        {
            technicalStatus = "ready",
            bundleId = new string('c', 64),
            buildId,
            targets = new[] { "report", "server" },
            checks = new[]
            {
                new { code = "DP0001", status = "passed", target = (string?)null, message = "ok" }
            },
            mentor = new { status = "reviewed", summary = "non-blocking" }
        }));
    }

    private sealed class FakeProcessService : IProcessService
    {
        private readonly string? _root;

        public FakeProcessService(string? root = null)
        {
            _root = root;
        }

        public List<ProcessRequest> Requests { get; } = [];

        public string ProducedBuildId { get; init; } = new string('b', 64);

        public int LocalExitCode { get; init; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var args = request.ArgumentList ?? [];
            if (args.Contains("embedded", StringComparer.Ordinal)
                && args.Contains("probe", StringComparer.Ordinal))
            {
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, JsonSerializer.Serialize(new
                {
                    schemaVersion = "junexis.embedded-probe/0.1",
                    toolPath = "pio-test",
                    toolAvailable = true,
                    probeSucceeded = true,
                    message = "Detected 1 embedded device.",
                    devices = new[]
                    {
                        new
                        {
                            port = "COM7",
                            description = "Junexis board",
                            hardwareId = "USB VID:PID",
                            serialNumber = "SERIAL-7",
                            manufacturer = "Junexis"
                        }
                    }
                })));
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }

            if (args.Contains("embedded", StringComparer.Ordinal)
                && args.Contains("select", StringComparer.Ordinal))
            {
                var portIndex = Array.IndexOf(args.ToArray(), "--port");
                var selectionIndex = Array.IndexOf(args.ToArray(), "--selection-out");
                var port = args[portIndex + 1];
                var selectionPath = args[selectionIndex + 1];
                Directory.CreateDirectory(Path.GetDirectoryName(selectionPath)!);
                var json = JsonSerializer.Serialize(new
                {
                    schemaVersion = "junexis.embedded-device-selection/0.1",
                    port,
                    description = "Junexis board",
                    hardwareId = "USB VID:PID",
                    serialNumber = "SERIAL-7",
                    manufacturer = "Junexis"
                });
                File.WriteAllText(selectionPath, json);
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, json));
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }

            if (args.Contains("embedded", StringComparer.Ordinal)
                && args.Contains("flash", StringComparer.Ordinal))
            {
                var receiptIndex = Array.IndexOf(args.ToArray(), "--receipt-out");
                var selectionIndex = Array.IndexOf(args.ToArray(), "--selection");
                var receiptPath = args[receiptIndex + 1];
                var selectionPath = args[selectionIndex + 1];
                var selection = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(selectionPath));
                Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
                File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
                {
                    schemaVersion = "junexis.embedded-flash-receipt/0.1",
                    lifecycleStatus = "succeeded",
                    summary = "Embedded flash completed successfully.",
                    bundleId = new string('c', 64),
                    buildId = ProducedBuildId,
                    port = selection.GetProperty("port").GetString()
                }));
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "upload ok"));
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }

            if (args.Contains("package", StringComparer.Ordinal))
            {
                var outIndex = Array.IndexOf(args.ToArray(), "--out");
                var bundlePath = args[outIndex + 1];
                Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
                File.WriteAllText(bundlePath, "bundle");
                var targets = args.Contains("embedded", StringComparer.Ordinal)
                    ? new[] { "embedded" }
                    : new[] { "report", "server" };
                File.WriteAllText(bundlePath + ".package-receipt.json", JsonSerializer.Serialize(new
                {
                    schemaVersion = "junexis.package-receipt/0.1",
                    bundleId = new string('c', 64),
                    buildId = ProducedBuildId,
                    targets
                }));
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "package ok"));
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }

            if (args.Contains("preflight", StringComparer.Ordinal))
            {
                var reportIndex = Array.IndexOf(args.ToArray(), "--report-out");
                var reportPath = args[reportIndex + 1];
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                var policyIndex = Array.IndexOf(args.ToArray(), "--auth-policy");
                var policy = policyIndex >= 0 ? args[policyIndex + 1] : "permissive";
                var fingerprintIndex = Array.IndexOf(args.ToArray(), "--trusted-signer");
                var fingerprint = fingerprintIndex >= 0 ? args[fingerprintIndex + 1] : null;
                var authenticityStatus = fingerprint is null ? "unsigned" : "trusted";
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new
                {
                    technicalStatus = "ready",
                    bundleId = new string('c', 64),
                    buildId = ProducedBuildId,
                    targets = new[] { "report", "server" },
                    checks = new[]
                    {
                        new { code = "DP0001", status = "passed", target = (string?)null, message = "ok" }
                    },
                    mentor = new { status = "reviewed", summary = "non-blocking" },
                    authenticity = new
                    {
                        status = authenticityStatus,
                        policy,
                        policySatisfied = policy == "permissive" || fingerprint is not null,
                        signer = fingerprint is null ? null : "Release signer",
                        publicKeyFingerprintSha256 = fingerprint,
                        signaturePath = fingerprint is null ? null : reportPath + ".signature.json"
                    }
                }));
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "preflight ok"));
                return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
            }

            if (args.Contains("local", StringComparer.Ordinal))
            {
                if (args.Contains("status", StringComparer.Ordinal))
                {
                    var sessionIndex = Array.IndexOf(args.ToArray(), "--session");
                    var sessionPath = args[sessionIndex + 1];
                    var ownerPath = Path.Combine(_root ?? Path.GetDirectoryName(sessionPath)!, ".junexis", "state", "deploy", "studio-owner-instance-id.txt");
                    var ownerId = File.Exists(ownerPath) ? File.ReadAllText(ownerPath).Trim() : "expired-owner";
                    var tool = ownerId == "expired-owner" ? "junexis-cli" : "junexis-studio";
                    var lease = ownerId == "expired-owner"
                        ? DateTimeOffset.UtcNow.AddSeconds(-10).UtcDateTime.ToString("O")
                        : DateTimeOffset.UtcNow.AddSeconds(20).UtcDateTime.ToString("O");
                    listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, JsonSerializer.Serialize(new
                    {
                        schemaVersion = "junexis.deployment-session/0.2",
                        buildId = new string('b', 64),
                        state = "running",
                        lastHeartbeatUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                        owner = new { tool, instanceId = ownerId, leaseExpiresAtUtc = lease },
                        targets = new[]
                        {
                            new { target = "server", status = "running", details = "Active probe OK: http://127.0.0.1:7101/health/ready" }
                        }
                    })));
                    return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
                }

                if (args.Contains("stop", StringComparer.Ordinal))
                {
                    var ownerToolIndex = Array.IndexOf(args.ToArray(), "--owner-tool");
                    var ownerIdIndex = Array.IndexOf(args.ToArray(), "--owner-instance-id");
                    var ownerTool = args[ownerToolIndex + 1];
                    var ownerInstanceId = args[ownerIdIndex + 1];
                    listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, JsonSerializer.Serialize(new
                    {
                        schemaVersion = "junexis.deployment-session/0.2",
                        buildId = new string('b', 64),
                        state = "stopped",
                        lastHeartbeatUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                        owner = new { tool = ownerTool, instanceId = ownerInstanceId, leaseExpiresAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O") },
                        targets = new[]
                        {
                            new { target = "server", status = "stopped", details = "Stopped by owner" }
                        }
                    })));
                    return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
                }

                if (args.Contains("adopt", StringComparer.Ordinal))
                {
                    var ownerToolIndex = Array.IndexOf(args.ToArray(), "--owner-tool");
                    var ownerIdIndex = Array.IndexOf(args.ToArray(), "--owner-instance-id");
                    var ownerTool = args[ownerToolIndex + 1];
                    var ownerInstanceId = args[ownerIdIndex + 1];
                    listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, JsonSerializer.Serialize(new
                    {
                        schemaVersion = "junexis.deployment-session/0.2",
                        buildId = new string('b', 64),
                        state = "running",
                        lastHeartbeatUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                        owner = new { tool = ownerTool, instanceId = ownerInstanceId, leaseExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(30).UtcDateTime.ToString("O") },
                        targets = new[]
                        {
                            new { target = "server", status = "running", details = "Active probe OK: http://127.0.0.1:7101/health/ready" }
                        }
                    })));
                    return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
                }

                var preflightIndex = Array.IndexOf(args.ToArray(), "--preflight");
                var receiptPath = Path.Combine(Path.GetDirectoryName(args[preflightIndex + 1])!, "deployment-receipt.json");
                Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
                WriteDeploymentReceipt(receiptPath, new string('b', 64), LocalExitCode);
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "READY server"));
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, "READY report"));
                return Task.FromResult(new ProcessResult(LocalExitCode, TimeSpan.Zero));
            }

            throw new InvalidOperationException("Unexpected process invocation.");
        }
    }

    private sealed class DeploymentFixture : IDisposable
    {
        public DeploymentFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"junexis-deploy-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, "src"));
            Directory.CreateDirectory(Path.Combine(Root, "out", "debug"));
            Directory.CreateDirectory(Path.Combine(Root, "out", "analysis"));
            File.WriteAllText(Path.Combine(Root, "junexis.project.toml"), "entry = \"src/main.jnx\"");
            var entry = Path.Combine(Root, "src", "main.jnx");
            var bundleManifest = Path.Combine(Root, "out", "junexis.bundle.json");
            var artifactManifest = Path.Combine(Root, "out", "junexis.artifacts.json");
            var debugManifest = Path.Combine(Root, "out", "debug", "junexis.debug.toml");
            var analysisSnapshot = Path.Combine(Root, "out", "analysis", "junexis.analysis.json");
            File.WriteAllText(entry, "task Main() @server {}\n");
            var identity = JsonSerializer.Serialize(new { buildId = BuildId });
            File.WriteAllText(bundleManifest, identity);
            File.WriteAllText(artifactManifest, identity);
            File.WriteAllText(debugManifest, "{}");
            File.WriteAllText(analysisSnapshot, identity);
            Receipt = new LatestBuildReceipt(
                Path.Combine(Root, ".junexis", "state", "latest-build.json"),
                BuildId,
                "project",
                Root,
                entry,
                Path.Combine(Root, "out"),
                ["report", "server"],
                bundleManifest,
                artifactManifest,
                debugManifest,
                analysisSnapshot);
        }

        public string Root { get; }

        public string BuildId { get; } = new string('b', 64);

        public LatestBuildReceipt Receipt { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FixedIngressClock : IIngressClock
    {
        public FixedIngressClock(DateTimeOffset utcNow)
        {
            this.UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class RecordingIngressSink : IIngressSink
    {
        public List<AuditRecord> AuditRecords { get; } = new();

        public List<HealthSignal> HealthSignals { get; } = new();

        public Task WriteLogAsync(CanonicalEventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAuditAsync(CanonicalEventEnvelope envelope, CancellationToken cancellationToken)
        {
            var record = JsonSerializer.Deserialize<AuditRecord>(envelope.PayloadJson, ContractJson.SerializerOptions)
                ?? throw new InvalidOperationException("Audit payload should deserialize.");
            this.AuditRecords.Add(record);
            return Task.CompletedTask;
        }

        public Task WriteHealthAsync(CanonicalEventEnvelope envelope, CancellationToken cancellationToken)
        {
            var signal = JsonSerializer.Deserialize<HealthSignal>(envelope.PayloadJson, ContractJson.SerializerOptions)
                ?? throw new InvalidOperationException("Health payload should deserialize.");
            this.HealthSignals.Add(signal);
            return Task.CompletedTask;
        }

        public Task WriteTelemetryAsync(CanonicalEventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
