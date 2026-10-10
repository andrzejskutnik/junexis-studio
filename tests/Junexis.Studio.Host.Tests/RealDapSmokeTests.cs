using System.Text.Json;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class RealDapSmokeTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task GeneratedJunexisGoProgram_StartsThroughRealDelveDap()
    {
        var buildRoot = Environment.GetEnvironmentVariable("JUNEXIS_REAL_DAP_ROOT");
        if (string.IsNullOrWhiteSpace(buildRoot))
            return;

        var mapPath = Path.Combine(buildRoot, "debug", "junexis.source-map.json");
        using var sourceMap = JsonDocument.Parse(await File.ReadAllTextAsync(mapPath));
        var statement = sourceMap.RootElement.GetProperty("mappings").EnumerateArray()
            .First(item => item.GetProperty("kind").GetString() == "statement"
                           && item.GetProperty("generated").GetProperty("target").GetString() == "server");
        var generated = statement.GetProperty("generated");
        var runtimePath = Path.GetFullPath(
            Path.Combine(buildRoot, generated.GetProperty("file").GetString()!));
        var runtimeLine = generated.GetProperty("startLine").GetInt32();
        var programRoot = Path.GetDirectoryName(runtimePath)!;
        var generatedLines = await File.ReadAllLinesAsync(runtimePath);
        var mainLine = Array.FindIndex(generatedLines, line => line.Trim() == "func main() {") + 2;
        Assert.True(mainLine > 1, "Generated Go program does not contain an executable main function.");
        var delve = DebugToolLocator.ResolveDelve();
        Assert.True(File.Exists(delve), $"Delve was not found at '{delve}'.");

        await using var service = new DapDebugSessionService();
        var output = new List<string>();
        service.OutputReceived += item => output.Add(item.Text);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var configuration = new DebugSessionConfiguration(
            delve,
            "dap --listen=127.0.0.1:{port}",
            programRoot,
            new Dictionary<string, object?>
            {
                ["name"] = "Junexis real DAP smoke",
                ["request"] = "launch",
                ["type"] = "go",
                ["mode"] = "debug",
                ["program"] = programRoot,
                ["cwd"] = programRoot,
                ["stopOnEntry"] = false,
            },
            [
                new DebugSourceBreakpoint(runtimePath, runtimeLine),
                new DebugSourceBreakpoint(runtimePath, mainLine),
            ],
            DebugAdapterTransport.Tcp,
            "server");

        try
        {
            await service.StartAsync(configuration, timeout.Token);
            await WaitUntilAsync(
                () => service.Snapshot.State is DebugSessionState.Stopped or DebugSessionState.Failed,
                timeout.Token);

            Assert.Equal(DebugSessionState.Stopped, service.Snapshot.State);
            Assert.NotEmpty(service.Snapshot.StackFrames);
            Assert.Contains(service.BreakpointStatuses, item => item.Verified);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Real Delve DAP smoke failed. State: {service.Snapshot.State}; " +
                $"status: {service.Snapshot.Status}; adapter output: {string.Join(" | ", output)}",
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
