using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class AnalysisSnapshotServiceTests
{
    [Fact]
    public async Task LoadAsync_MapsStableSymbolsRelationsAndTopology()
    {
        var path = CreateSnapshot();
        try
        {
            var snapshot = await new AnalysisSnapshotService().LoadAsync(path);

            Assert.Equal("build-1", snapshot.BuildId);
            Assert.Equal(3, snapshot.Symbols.Count);
            var relation = Assert.Single(snapshot.Relations);
            Assert.Equal("chan", relation.CommunicationKind);
            Assert.Equal("task:Producer", relation.SourceSymbolId);
            Assert.Equal(1, snapshot.Topology.MaxDepth);
            Assert.Equal(1, snapshot.Topology.FanOut["task:Producer"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsRelationToUnknownSymbol()
    {
        var path = Path.Combine(Path.GetTempPath(), $"junexis-analysis-invalid-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, ValidJson.Replace(
            "\"targetSymbolId\": \"task:Consumer\"",
            "\"targetSymbolId\": \"task:Missing\"",
            StringComparison.Ordinal));
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => new AnalysisSnapshotService().LoadAsync(path));

            Assert.Contains("unknown symbol", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateSnapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"junexis-analysis-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, ValidJson);
        return path;
    }

    private const string ValidJson = """
        {
          "schemaVersion": "1.0",
          "buildId": "build-1",
          "sourceFile": "src/main.jnx",
          "symbols": [
            { "id": "task:Producer", "kind": "task", "name": "Producer", "source": { "file": "src/main.jnx", "line": 1, "column": 1 }, "platforms": ["server"], "dataType": "chan<Text>" },
            { "id": "task:Consumer", "kind": "task", "name": "Consumer", "source": null, "platforms": ["report"], "dataType": null },
            { "id": "pipeline:Main", "kind": "pipeline", "name": "Main", "source": null, "platforms": [], "dataType": null }
          ],
          "relations": [
            { "id": "communication:Main:Producer->Consumer", "kind": "communication", "communicationKind": "chan", "pipelineSymbolId": "pipeline:Main", "sourceSymbolId": "task:Producer", "sourcePort": null, "targetSymbolId": "task:Consumer", "targetPort": null, "source": null }
          ],
          "topology": {
            "hasCycles": false,
            "maxDepth": 1,
            "stronglyConnectedComponents": [["task:Consumer"], ["task:Producer"]],
            "fanIn": { "task:Producer": 0, "task:Consumer": 1 },
            "fanOut": { "task:Producer": 1, "task:Consumer": 0 },
            "depth": { "task:Producer": 0, "task:Consumer": 1 }
          }
        }
        """;
}
