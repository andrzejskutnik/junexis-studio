using System.Text.Json;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

/// <summary>Opt-in integration coverage for generated C++ and the installed GDB DAP adapter.</summary>
public sealed class RealCppDapSmokeTests
{
    /// <summary>Build-root input stops in generated C++ and maps the frame back to Junexis.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task GeneratedJunexisCppProgram_StopsThroughRealGdbDap()
    {
        var buildRoot = Environment.GetEnvironmentVariable("JUNEXIS_REAL_CPP_DAP_ROOT");
        if (string.IsNullOrWhiteSpace(buildRoot))
            return;

        using var sourceMap = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(buildRoot, "debug", "junexis.source-map.json")));
        var statement = sourceMap.RootElement.GetProperty("mappings").EnumerateArray()
            .Where(item => item.GetProperty("kind").GetString() == "statement"
                           && item.GetProperty("generated").GetProperty("target").GetString() == "embedded")
            .OrderBy(item => item.GetProperty("source").GetProperty("line").GetInt32())
            .Last();
        var source = statement.GetProperty("source");
        var generated = statement.GetProperty("generated");
        var sourcePath = Path.GetFullPath(source.GetProperty("file").GetString()!);
        var runtimePath = Path.GetFullPath(Path.Combine(buildRoot, generated.GetProperty("file").GetString()!));
        var program = Path.Combine(buildRoot, "embedded", "junexis_build.exe");
        Assert.True(File.Exists(program), $"Compiled C++ smoke program was not found at '{program}'.");
        var adapter = new DebugAdapterRegistry().Resolve("cpp");
        Assert.NotNull(adapter);

        var mapper = new DebugSourceMapper(new StubAnalysisService(), new EventBus(), new OutputService());
        mapper.SetMappings(
        [
            new DebugSourceMapper.RuntimeSourceMapping(
                statement.GetProperty("symbolId").GetString()!,
                sourcePath,
                source.GetProperty("line").GetInt32(),
                source.GetProperty("column").GetInt32(),
                generated.GetProperty("target").GetString()!,
                runtimePath,
                generated.GetProperty("startLine").GetInt32(),
                generated.GetProperty("endLine").GetInt32(),
                generated.GetProperty("startColumn").GetInt32(),
                statement.GetProperty("kind").GetString()!,
                statement.GetProperty("irPath").GetString())
        ]);

        await using var service = new DapDebugSessionService(mapper);
        var output = new List<string>();
        service.OutputReceived += item => output.Add(item.Text);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var configuration = new DebugSessionConfiguration(
            adapter.FileName,
            adapter.Arguments,
            Path.Combine(buildRoot, "embedded"),
            new Dictionary<string, object?>
            {
                ["name"] = "Junexis real C++ DAP smoke",
                ["request"] = "launch",
                ["type"] = adapter.ConfigurationType,
                ["program"] = program,
                ["cwd"] = Path.Combine(buildRoot, "embedded"),
                ["stopOnEntry"] = false,
                ["MIMode"] = "gdb",
                ["miDebuggerPath"] = DebugToolLocator.ResolveGdb(),
                ["externalConsole"] = false,
            },
            [new DebugSourceBreakpoint(sourcePath, source.GetProperty("line").GetInt32())],
            adapter.Transport,
            "embedded");

        try
        {
            await service.StartAsync(configuration, timeout.Token);
            await WaitUntilAsync(
                () => service.Snapshot.State is DebugSessionState.Stopped
                    or DebugSessionState.Failed
                    or DebugSessionState.Terminated,
                timeout.Token);

            Assert.Equal(DebugSessionState.Stopped, service.Snapshot.State);
            Assert.Contains(service.BreakpointStatuses, item => item.Verified);
            var frame = Assert.Single(service.Snapshot.StackFrames, frame =>
                frame.SourcePath?.Equals(sourcePath, StringComparison.OrdinalIgnoreCase) == true
                && frame.RuntimeSourcePath?.Equals(runtimePath, StringComparison.OrdinalIgnoreCase) == true
                && frame.JunexisSymbolId == statement.GetProperty("symbolId").GetString());
            var scopes = await service.GetScopesAsync(frame.Id, timeout.Token);
            var locals = scopes.First(scope => scope.Name.Contains("local", StringComparison.OrdinalIgnoreCase));
            var variables = await service.GetVariablesAsync(locals.VariablesReference, timeout.Token);
            Assert.Contains(variables, variable => variable.Name == "answer" && variable.Value == "41");
            var evaluation = await service.EvaluateAsync("answer + 1", frame.Id, timeout.Token);
            Assert.Equal("42", evaluation.Result);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Real C++ DAP smoke failed. State: {service.Snapshot.State}; " +
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

    private sealed class StubAnalysisService : IAnalysisSnapshotService
    {
        public Task<AnalysisSnapshot> LoadAsync(
            string snapshotPath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
