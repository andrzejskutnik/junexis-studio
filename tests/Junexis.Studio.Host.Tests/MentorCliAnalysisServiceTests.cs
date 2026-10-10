using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class MentorCliAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_MapsVersionedFindingContract()
    {
        var process = new StubProcessService("""
            {
              "schemaVersion": "1.2",
              "success": true,
              "buildId": "build-1",
              "snapshotPath": "C:/project/out/analysis/junexis.analysis.json",
              "summary": {
                "taskCount": 2,
                "pipelineCount": 1,
                "narrativeCount": 1,
                "platformCount": 2,
                "findingCount": 1,
                "insightCount": 1
              },
              "findings": [
                {
                  "code": "M1001",
                  "severity": "warning",
                  "message": "Disconnected task.",
                  "relatedSymbolId": "task:A",
                  "explanation": "The task has no communication path.",
                  "recommendedAction": "Connect the task to a pipeline.",
                  "source": { "file": "C:/project/main.jnx", "line": 7, "column": 3 }
                }
              ],
              "insights": [
                {
                  "kind": "NarrativeCoverage",
                  "summary": "Narrative coverage: 1/2 tasks.",
                  "score": 0.5,
                  "relatedSymbolIds": ["task:B"]
                }
              ],
              "patterns": [
                {
                  "patternId": "EdgeToServer",
                  "name": "Edge to server",
                  "confidence": 1.0,
                  "rationale": "Embedded data reaches a server task.",
                  "participantSymbolIds": ["task:A", "task:B"]
                }
              ],
              "conformance": {
                "status": "partial",
                "targetLevel": "S3",
                "scenarioCount": 6,
                "targetSatisfiedCount": 5,
                "gapCount": 1,
                "gaps": [
                  { "id": "boundary.open", "achievedLevel": "S3", "targetLevel": "S4" }
                ],
                "blocking": false,
                "bound": true,
                "buildId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "bundleId": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "bindingSha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                "opinion": "One non-blocking gap remains."
              }
            }
            """);
        var service = new MentorCliAnalysisService(process, "junexis-mentor");

        var result = await service.AnalyzeAsync(Path.GetFullPath("junexis.analysis.json"));

        Assert.Equal("build-1", result.BuildId);
        Assert.Equal("1.2", result.SchemaVersion);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("M1001", finding.Code);
        Assert.Equal("task:A", finding.RelatedSymbolId);
        Assert.Equal(7, finding.Source?.Line);
        Assert.Contains("communication path", finding.Explanation);
        Assert.Equal(2, result.Summary?.TaskCount);
        Assert.Equal("NarrativeCoverage", Assert.Single(result.Insights!).Kind);
        Assert.Equal("EdgeToServer", Assert.Single(result.Patterns!).PatternId);
        Assert.Equal("partial", result.Conformance?.Status);
        Assert.False(result.Conformance?.Blocking);
        Assert.True(result.Conformance?.Bound);
        Assert.Equal(new string('b', 64), result.Conformance?.BundleId);
        Assert.Equal("boundary.open", Assert.Single(result.Conformance!.Gaps).Id);
        Assert.Contains("analyze --snapshot", process.Request?.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDevelopmentCli_FindsInstalledPackageFromEnvironment()
    {
        var original = Environment.GetEnvironmentVariable("JUNEXIS_MENTOR_CLI");
        var root = Path.Combine(Path.GetTempPath(), $"junexis-studio-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "junexis-mentor.exe");
        File.WriteAllText(executable, "stub");
        try
        {
            Environment.SetEnvironmentVariable("JUNEXIS_MENTOR_CLI", executable);

            Assert.Equal(executable, MentorCliAnalysisService.ResolveDevelopmentCli());
        }
        finally
        {
            Environment.SetEnvironmentVariable("JUNEXIS_MENTOR_CLI", original);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindConformanceReport_UsesProjectJunexisEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"junexis-studio-conformance-{Guid.NewGuid():N}");
        var analysis = Path.Combine(root, "out", "analysis", "junexis.analysis.json");
        var report = Path.Combine(root, ".junexis", "semantic-conformance.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(analysis)!);
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, "{}");

            Assert.Equal(report, MentorCliAnalysisService.FindConformanceReport(analysis));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubProcessService(string stdout) : IProcessService
    {
        public ProcessRequest? Request { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            Action<ProcessOutputLine>? listener,
            CancellationToken cancellationToken)
        {
            Request = request;
            foreach (var line in stdout.Split('\n'))
                listener?.Invoke(new ProcessOutputLine(ProcessOutputKind.StdOut, line.TrimEnd('\r')));
            return Task.FromResult(new ProcessResult(0, TimeSpan.Zero));
        }
    }
}
