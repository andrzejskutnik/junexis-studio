using Junexis.Studio.Core;
using Junexis.Studio.Abstractions;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class OutputServiceTests
{
    [Fact]
    [Trait("Issue", "13")]
    public void Constructor_WithMaxEntriesBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutputService(0));
    }

    [Fact]
    [Trait("Issue", "13")]
    public void Write_NormalizesChannelAndMessage()
    {
        var sut = new OutputService();

        sut.Write(" ", null!);

        var snapshot = sut.GetSnapshot();
        Assert.Single(snapshot);
        Assert.Equal("General", snapshot[0].Channel);
        Assert.Equal(string.Empty, snapshot[0].Message);
        Assert.Equal(OutputSeverity.Info, snapshot[0].Severity);
    }

    [Fact]
    [Trait("Issue", "65")]
    public void Write_RegistersChannel()
    {
        var sut = new OutputService();

        sut.Write("Core", "hello");

        var channels = sut.GetChannelsSnapshot();
        Assert.Contains("General", channels);
        Assert.Contains("Core", channels);
    }

    [Fact]
    [Trait("Issue", "65")]
    public void RegisterChannel_NormalizesWhitespaceToGeneral()
    {
        var sut = new OutputService();

        var normalized = sut.RegisterChannel("   ");

        Assert.Equal("General", normalized);
        var channels = sut.GetChannelsSnapshot();
        Assert.Contains("General", channels);
    }

    [Fact]
    [Trait("Issue", "13")]
    public void EntryWritten_FiresWithSameEntryAsSnapshot()
    {
        var sut = new OutputService();
        OutputEntry? last = null;
        sut.EntryWritten += e => last = e;

        sut.Write("Core", "hello");

        Assert.NotNull(last);
        var snapshot = sut.GetSnapshot();
        Assert.Single(snapshot);
        Assert.Equal(snapshot[0], last);
    }

    [Fact]
    [Trait("Issue", "66")]
    public void Write_WithSeverity_SetsSeverity()
    {
        var sut = new OutputService();

        sut.Write("Core", "hello", OutputSeverity.Warning);

        var snapshot = sut.GetSnapshot();
        Assert.Single(snapshot);
        Assert.Equal(OutputSeverity.Warning, snapshot[0].Severity);
    }

    [Fact]
    [Trait("Issue", "69")]
    public void Write_WithSourceAndException_SetsMetadata()
    {
        var sut = new OutputService();

        sut.Write("Core", "kaboom", OutputSeverity.Error, "my.ext", "NullRef at line 42");

        var snapshot = sut.GetSnapshot();
        Assert.Single(snapshot);
        Assert.Equal("my.ext", snapshot[0].Source);
        Assert.Equal("NullRef at line 42", snapshot[0].ExceptionText);
        Assert.Equal(OutputSeverity.Error, snapshot[0].Severity);
    }

    [Fact]
    [Trait("Issue", "69")]
    public void Write_WithoutSource_LeavesSourceNull()
    {
        var sut = new OutputService();

        sut.Write("Core", "hello");

        var snapshot = sut.GetSnapshot();
        Assert.Single(snapshot);
        Assert.Null(snapshot[0].Source);
        Assert.Null(snapshot[0].ExceptionText);
    }

    [Fact]
    [Trait("Issue", "13")]
    public void Write_RespectsRingBufferLimit()
    {
        var sut = new OutputService(maxEntries: 2);
        sut.Write("c", "1");
        sut.Write("c", "2");
        sut.Write("c", "3");

        var snapshot = sut.GetSnapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal("2", snapshot[0].Message);
        Assert.Equal("3", snapshot[1].Message);
    }
}
