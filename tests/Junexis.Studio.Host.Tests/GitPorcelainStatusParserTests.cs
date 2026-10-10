using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class GitPorcelainStatusParserTests
{
    [Fact]
    [Trait("Issue", "25")]
    public void Parse_Untracked_Modified_Staged_Ignored()
    {
        var porcelain = string.Join("\n", new[]
        {
            "?? a.txt",
            " M b.txt",
            "M  c.txt",
            "!! d.txt",
        });

        var result = GitPorcelainStatusParser.Parse(porcelain);

        Assert.Collection(
            result,
            e => Assert.Equal(VersionControlFileStatus.Untracked, e.Status),
            e => Assert.Equal(VersionControlFileStatus.Modified, e.Status),
            e => Assert.Equal(VersionControlFileStatus.Staged, e.Status),
            e => Assert.Equal(VersionControlFileStatus.Ignored, e.Status));
    }

    [Fact]
    [Trait("Issue", "25")]
    public void Parse_Rename_TakesNewPath()
    {
        var porcelain = "R  old.txt -> new.txt";

        var result = GitPorcelainStatusParser.Parse(porcelain);

        Assert.Single(result);
        Assert.Equal("new.txt", result[0].Path);
        Assert.Equal(VersionControlFileStatus.Staged, result[0].Status);
    }
}
