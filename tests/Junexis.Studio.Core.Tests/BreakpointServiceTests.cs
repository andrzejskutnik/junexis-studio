using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class BreakpointServiceTests
{
    [Fact]
    public void Toggle_AddsThenRemovesSameSourceLocation()
    {
        var service = new BreakpointService();
        var path = Path.Combine(Path.GetTempPath(), "main.cpp");

        Assert.True(service.Toggle(path, 12));
        Assert.Single(service.GetBreakpoints());
        Assert.False(service.Toggle(path, 12));
        Assert.Empty(service.GetBreakpoints());
    }

    [Fact]
    public void GetBreakpoints_ReturnsDeterministicPathAndLineOrder()
    {
        var service = new BreakpointService();
        service.Toggle(Path.Combine(Path.GetTempPath(), "b.go"), 8);
        service.Toggle(Path.Combine(Path.GetTempPath(), "a.go"), 10);
        service.Toggle(Path.Combine(Path.GetTempPath(), "a.go"), 3);

        var result = service.GetBreakpoints();

        Assert.Equal([3, 10, 8], result.Select(item => item.Line));
    }

    [Fact]
    public void Clear_RaisesChangeAndRemovesAllBreakpoints()
    {
        var service = new BreakpointService();
        service.Toggle(Path.Combine(Path.GetTempPath(), "main.ts"), 4);
        var changes = 0;
        service.BreakpointsChanged += () => changes++;

        service.Clear();

        Assert.Equal(1, changes);
        Assert.Empty(service.GetBreakpoints());
    }

    [Fact]
    public void Set_ReplacesBehaviorAtSameLocationAndNormalizesOptionalValues()
    {
        var service = new BreakpointService();
        var path = Path.Combine(Path.GetTempPath(), "conditional.cpp");
        service.Toggle(path, 12);

        service.Set(new DebugSourceBreakpoint(path, 12, null, " count > 3 ", " 5 ", " value={count} "));

        var breakpoint = Assert.Single(service.GetBreakpoints());
        Assert.Equal("count > 3", breakpoint.Condition);
        Assert.Equal("5", breakpoint.HitCondition);
        Assert.Equal("value={count}", breakpoint.LogMessage);
    }

    [Fact]
    public void WorkspacePersistence_RestoresPortableBreakpointDefinitions()
    {
        var root = Directory.CreateTempSubdirectory("junexis-breakpoints-");
        try
        {
            var source = Path.Combine(root.FullName, "src", "main.jnx");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            var firstWorkspace = new WorkspaceService();
            var first = new BreakpointService(firstWorkspace);
            firstWorkspace.OpenFolder(root.FullName);
            first.Set(new DebugSourceBreakpoint(source, 19, 2, "ready", ">= 3", "value={value}"));

            var storePath = Path.Combine(root.FullName, ".junexis", "studio-breakpoints.json");
            Assert.True(File.Exists(storePath));
            var json = File.ReadAllText(storePath);
            Assert.Contains("main.jnx", json, StringComparison.Ordinal);
            Assert.DoesNotContain(root.FullName, json, StringComparison.OrdinalIgnoreCase);

            var secondWorkspace = new WorkspaceService();
            var second = new BreakpointService(secondWorkspace);
            secondWorkspace.OpenFolder(root.FullName);

            var restored = Assert.Single(second.GetBreakpoints());
            Assert.Equal(Path.GetFullPath(source), restored.Path);
            Assert.Equal(19, restored.Line);
            Assert.Equal("ready", restored.Condition);
            Assert.Equal(">= 3", restored.HitCondition);
            Assert.Equal("value={value}", restored.LogMessage);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
