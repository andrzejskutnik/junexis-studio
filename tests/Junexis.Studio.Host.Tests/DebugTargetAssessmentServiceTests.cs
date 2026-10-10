using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

/// <summary>Coverage for evidence-based attach target recommendations.</summary>
public sealed class DebugTargetAssessmentServiceTests
{
    /// <summary>A process inside a versioned server output is correlated with Go and the build id.</summary>
    [Fact]
    public void Assess_JunexisServerArtifact_ExplainsProfileAndNarrativeReadiness()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-target-{Guid.NewGuid():N}");
        var inventory = new ArtifactInventorySnapshot(
            "build-narrative-123456",
            Path.Combine(root, "junexis.artifacts.json"),
            [
                new("server/main.go", Path.Combine(root, "server", "main.go"), "server", "source", "generated", "a"),
                new("debug/junexis.source-map.json", Path.Combine(root, "debug", "junexis.source-map.json"),
                    "debug", "debug-source-map", "generated", "b"),
            ]);
        var service = new DebugTargetAssessmentService(inventory);

        var result = service.Assess(
            new DebugProcessInfo(42, "junexis-server", Path.Combine(root, "server", "junexis-server.exe")),
            "cpp");

        Assert.Equal("go", result.RecommendedProfileId);
        Assert.Equal(DebugTargetConfidence.BuildArtifact, result.Confidence);
        Assert.False(result.RequestedProfileMatches);
        Assert.Equal(inventory.BuildId, result.BuildId);
        Assert.True(result.SourceCorrelationReady);
        Assert.Contains("WHY", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.Evidence, item => item.Contains("local observation", StringComparison.Ordinal));
    }

    /// <summary>A known Node runtime produces an explicit heuristic rather than fabricated build evidence.</summary>
    [Fact]
    public void Assess_NodeWithoutInventory_UsesRuntimeHeuristicOnly()
    {
        var result = new DebugTargetAssessmentService(null).Assess(new DebugProcessInfo(7, "node", "node.exe"), "ts");

        Assert.Equal("typescript", result.RecommendedProfileId);
        Assert.Equal(DebugTargetConfidence.Heuristic, result.Confidence);
        Assert.True(result.RequestedProfileMatches);
        Assert.Null(result.BuildId);
        Assert.False(result.SourceCorrelationReady);
    }

    /// <summary>An unknown process remains unknown and explains that attach alone is non-causal.</summary>
    [Fact]
    public void Assess_UnknownProcess_DoesNotGuessProfile()
    {
        var result = new DebugTargetAssessmentService(null).Assess(
            new DebugProcessInfo(9, "custom-worker", "C:/apps/custom-worker.exe"), "go");

        Assert.Null(result.RecommendedProfileId);
        Assert.Equal(DebugTargetConfidence.Unknown, result.Confidence);
        Assert.Null(result.RequestedProfileMatches);
        Assert.Contains("Narrative WHY", result.Summary, StringComparison.Ordinal);
    }
}
