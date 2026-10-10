using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class DebugSourceMapperTests
{
    [Fact]
    public void MapFrame_MatchesGeneratedFunctionNameToStableJunexisSymbol()
    {
        var mapper = new DebugSourceMapper(new StubAnalysisService(), new EventBus(), new OutputService());
        var source = Path.GetFullPath(Path.Combine("examples", "sensor.jnx"));
        mapper.SetSnapshot(CreateSnapshot(source));
        var generated = Path.GetFullPath(Path.Combine("generated", "sensor.go"));

        var mapped = mapper.MapFrame(new DebugStackFrame(3, "main.CzytnikSensora", generated, 120, 1));

        Assert.Equal("task:CzytnikSensora", mapped.JunexisSymbolId);
        Assert.Equal(source, mapped.SourcePath);
        Assert.Equal(generated, mapped.RuntimeSourcePath);
        Assert.Equal(7, mapped.Line);
        Assert.Equal(5, mapped.Column);
    }

    [Fact]
    public void MapFrame_LeavesUnknownRuntimeFrameUnchanged()
    {
        var mapper = new DebugSourceMapper(new StubAnalysisService(), new EventBus(), new OutputService());
        mapper.SetSnapshot(CreateSnapshot(Path.GetFullPath("main.jnx")));
        var frame = new DebugStackFrame(4, "runtime.goexit", "runtime/asm_amd64.s", 1700, 1);

        var mapped = mapper.MapFrame(frame);

        Assert.Same(frame, mapped);
    }

    [Fact]
    public void MapFrame_UsesGeneratedRangeBeforeFunctionNameHeuristic()
    {
        var mapper = new DebugSourceMapper(new StubAnalysisService(), new EventBus(), new OutputService());
        var source = Path.GetFullPath("main.jnx");
        var generated = Path.GetFullPath(Path.Combine("out", "server", "main.go"));
        mapper.SetMappings(
        [
            new DebugSourceMapper.RuntimeSourceMapping(
                "task:CzytnikSensora", source, 20, 3, "server", generated, 100, 130, 1),
            new DebugSourceMapper.RuntimeSourceMapping(
                "task:CzytnikSensora", source, 22, 5, "server", generated, 112, 112, 1,
                "statement", "/Tasks[0]/Body[1]"),
        ]);

        var mapped = mapper.MapFrame(new DebugStackFrame(8, "anonymous", generated, 112, 7));

        Assert.Equal(source, mapped.SourcePath);
        Assert.Equal(22, mapped.Line);
        Assert.Equal("task:CzytnikSensora", mapped.JunexisSymbolId);
        Assert.Equal(generated, mapped.RuntimeSourcePath);
        Assert.Equal(112, mapped.RuntimeLine);
        Assert.Equal(7, mapped.RuntimeColumn);
    }

    [Fact]
    public void MapBreakpoints_ProjectsJunexisTaskToSelectedGeneratedTarget()
    {
        var mapper = new DebugSourceMapper(new StubAnalysisService(), new EventBus(), new OutputService());
        var source = Path.GetFullPath("main.jnx");
        var go = Path.GetFullPath(Path.Combine("out", "server", "main.go"));
        var cpp = Path.GetFullPath(Path.Combine("out", "embedded", "Read.cpp"));
        mapper.SetMappings(
        [
            new DebugSourceMapper.RuntimeSourceMapping("task:Read", source, 10, 1, "server", go, 80, 95, 1),
            new DebugSourceMapper.RuntimeSourceMapping(
                "task:Read", source, 12, 3, "server", go, 85, 85, 1,
                "statement", "/Tasks[0]/Body[0]"),
            new DebugSourceMapper.RuntimeSourceMapping(
                "task:Read", source, 12, 3, "server", go, 87, 87, 5,
                "statement", "/Tasks[0]/Body[0]"),
            new DebugSourceMapper.RuntimeSourceMapping("task:Read", source, 10, 1, "embedded", cpp, 20, 35, 1),
        ]);

        var mapped = mapper.MapBreakpoints("server", source,
            [new DebugSourceBreakpoint(source, 12, null, "ready", "3", "value={value}")]);

        var runtimeBreakpoints = Assert.Single(mapped).Value;
        Assert.Equal(go, Assert.Single(mapped).Key);
        Assert.Equal([85, 87], runtimeBreakpoints.Select(item => item.Line).ToArray());
        Assert.Equal([1, 5], runtimeBreakpoints.Select(item => item.Column).ToArray());
        Assert.All(runtimeBreakpoints, item =>
        {
            Assert.Equal("ready", item.Condition);
            Assert.Equal("3", item.HitCondition);
            Assert.Equal("value={value}", item.LogMessage);
        });
    }

    private static AnalysisSnapshot CreateSnapshot(string source) => new(
        "build-1",
        source,
        Path.GetFullPath("analysis.json"),
        [new AnalysisSymbol(
            "task:CzytnikSensora",
            "task",
            "CzytnikSensora",
            new AnalysisSourceLocation(source, 7, 5),
            ["server"],
            null)],
        [],
        new AnalysisTopology(false, 0, [], new Dictionary<string, int>(), new Dictionary<string, int>(), new Dictionary<string, int>()));

    private sealed class StubAnalysisService : IAnalysisSnapshotService
    {
        public Task<AnalysisSnapshot> LoadAsync(string snapshotPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
