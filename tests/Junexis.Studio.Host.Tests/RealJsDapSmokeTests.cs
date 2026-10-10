using System.Text.Json;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class RealJsDapSmokeTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task GeneratedJunexisReport_StopsThroughRealJsDebugDap()
    {
        var buildRoot = Environment.GetEnvironmentVariable("JUNEXIS_REAL_JS_DAP_ROOT");
        if (string.IsNullOrWhiteSpace(buildRoot))
            return;

        using var sourceMap = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(buildRoot, "debug", "junexis.source-map.json")));
        var statement = sourceMap.RootElement.GetProperty("mappings").EnumerateArray()
            .First(item => item.GetProperty("kind").GetString() == "statement"
                           && item.GetProperty("generated").GetProperty("target").GetString() == "report");
        var generated = statement.GetProperty("generated");
        var sourcePath = Path.GetFullPath(Path.Combine(buildRoot, generated.GetProperty("file").GetString()!));
        var irPath = statement.GetProperty("irPath").GetString();
        var reportRoot = Path.GetDirectoryName(sourcePath)!;
        var program = Path.Combine(reportRoot, "dist", "smoke.js");
        Assert.True(File.Exists(program), $"Compiled smoke runner was not found at '{program}'.");
        var sourceLines = await File.ReadAllLinesAsync(sourcePath);
        var sourceLine = Array.FindIndex(sourceLines,
            line => line.Contains($"junexis:source {irPath}", StringComparison.Ordinal)) + 2;
        Assert.True(sourceLine > 1, $"Generated TypeScript does not contain marker '{irPath}'.");
        var adapter = new DebugAdapterRegistry().Resolve("typescript");
        Assert.NotNull(adapter);

        await using var service = new DapDebugSessionService();
        var output = new List<string>();
        service.OutputReceived += item => output.Add(item.Text);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var configuration = new DebugSessionConfiguration(
            adapter.FileName,
            adapter.Arguments,
            reportRoot,
            new Dictionary<string, object?>
            {
                ["name"] = "Junexis real JavaScript DAP smoke",
                ["request"] = "launch",
                ["type"] = "pwa-node",
                ["program"] = program,
                ["cwd"] = reportRoot,
                ["sourceMaps"] = true,
                ["outFiles"] = new[] { Path.Combine(reportRoot, "dist", "**", "*.js") },
                ["console"] = "internalConsole",
            },
            [new DebugSourceBreakpoint(sourcePath, sourceLine)],
            adapter.Transport,
            "report");

        try
        {
            await service.StartAsync(configuration, timeout.Token);
            await WaitUntilAsync(
                () => service.Snapshot.State is DebugSessionState.Stopped
                    or DebugSessionState.Failed
                    or DebugSessionState.Terminated,
                timeout.Token);

            Assert.Equal(DebugSessionState.Stopped, service.Snapshot.State);
            Assert.Contains(service.Snapshot.StackFrames,
                frame => frame.SourcePath?.EndsWith("index.ts", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Real js-debug DAP smoke failed. State: {service.Snapshot.State}; " +
                $"status: {service.Snapshot.Status}; breakpoints: " +
                $"{string.Join(" | ", service.BreakpointStatuses.Select(item => $"verified={item.Verified}, message={item.Message}"))}; " +
                $"adapter output: {string.Join(" | ", output)}",
                ex);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(20, cancellationToken);
        }
    }
}
