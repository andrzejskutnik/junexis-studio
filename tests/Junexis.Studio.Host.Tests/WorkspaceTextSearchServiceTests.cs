using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class WorkspaceTextSearchServiceTests
{
    [Fact]
    public void Host_RegistersSearchAsLeftToolPanel()
    {
        var host = new JunexisStudioHost();
        host.Start();

        var panel = Assert.Single(
            host.Context.Panels.GetRegisteredPanels(),
            registration => registration.Id == "junexis.workspace.search");

        Assert.Equal(PanelType.Tool, panel.Type);
        Assert.Equal(DockGroup.Left, panel.DockGroup);
        host.Shutdown();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryOccurrenceWithExactLocation()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(
                Path.Combine(root, "src", "main.jnx"),
                "intent sensor\nsensor -> sensor\n");

            var response = await new WorkspaceTextSearchService().SearchAsync(
                new WorkspaceSearchRequest(root, "sensor"));

            Assert.Equal(3, response.Matches.Count);
            Assert.Collection(
                response.Matches.OrderBy(match => match.Line).ThenBy(match => match.Column),
                match => Assert.Equal((1, 8), (match.Line, match.Column)),
                match => Assert.Equal((2, 1), (match.Line, match.Column)),
                match => Assert.Equal((2, 11), (match.Line, match.Column)));
            Assert.All(response.Matches, match =>
                Assert.Equal(Path.Combine("src", "main.jnx").Replace('\\', '/'), match.RelativePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_RespectsCaseIncludeExcludeAndIgnoredDirectories()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "generated"));
            Directory.CreateDirectory(Path.Combine(root, "node_modules", "pkg"));
            File.WriteAllText(Path.Combine(root, "src", "main.jnx"), "Signal signal");
            File.WriteAllText(Path.Combine(root, "src", "main.ts"), "Signal");
            File.WriteAllText(Path.Combine(root, "generated", "copy.jnx"), "Signal");
            File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "index.jnx"), "Signal");

            var response = await new WorkspaceTextSearchService().SearchAsync(
                new WorkspaceSearchRequest(
                    root,
                    "Signal",
                    MatchCase: true,
                    IncludePattern: "*.jnx",
                    ExcludePattern: "generated/**"));

            var match = Assert.Single(response.Matches);
            Assert.Equal("src/main.jnx", match.RelativePath);
            Assert.Equal(1, match.Column);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_StopsAtLimitAndReportsTruncation()
    {
        var root = CreateRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "many.txt"), "x x x x x");

            var response = await new WorkspaceTextSearchService().SearchAsync(
                new WorkspaceSearchRequest(root, "x", MaxResults: 3));

            Assert.Equal(3, response.Matches.Count);
            Assert.True(response.IsTruncated);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_HonorsCancellation()
    {
        var root = CreateRoot();
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                new WorkspaceTextSearchService().SearchAsync(
                    new WorkspaceSearchRequest(root, "anything"),
                    cancellation.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
