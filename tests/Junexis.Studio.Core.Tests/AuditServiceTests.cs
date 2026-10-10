using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

public sealed class AuditServiceTests
{
    [Fact]
    [Trait("Issue", "70")]
    public void Record_StoresEntry()
    {
        var sut = new AuditService();
        var entry = new AuditEntry("config.changed", "timeout=30", DateTime.UtcNow, "Host");

        sut.Record(entry);

        var entries = sut.GetEntries();
        Assert.Single(entries);
        Assert.Equal("config.changed", entries[0].Action);
    }

    [Fact]
    [Trait("Issue", "70")]
    public void Record_FiresEntryRecordedEvent()
    {
        var sut = new AuditService();
        AuditEntry? received = null;
        sut.EntryRecorded += e => received = e;

        var entry = new AuditEntry("token.rotated", "masked", DateTime.UtcNow);
        sut.Record(entry);

        Assert.NotNull(received);
        Assert.Equal("token.rotated", received!.Action);
    }

    [Fact]
    [Trait("Issue", "70")]
    public void Record_WritesToOutputAuditChannel()
    {
        var output = new OutputService();
        var sut = new AuditService(output);

        sut.Record(new AuditEntry("test.action", "detail", DateTime.UtcNow, "TestSource"));

        var snapshot = output.GetSnapshot();
        Assert.Contains(snapshot, e => e.Channel == "Audit" && e.Message.Contains("test.action"));
    }

    [Fact]
    [Trait("Issue", "70")]
    public void Record_WithNullEntry_Throws()
    {
        var sut = new AuditService();
        Assert.Throws<ArgumentNullException>(() => sut.Record(null!));
    }

    [Fact]
    [Trait("Issue", "70")]
    public void GetEntries_ReturnsSnapshot()
    {
        var sut = new AuditService();
        sut.Record(new AuditEntry("a", "1", DateTime.UtcNow));
        sut.Record(new AuditEntry("b", "2", DateTime.UtcNow));

        var snapshot = sut.GetEntries();
        Assert.Equal(2, snapshot.Count);

        // Snapshot is isolated — adding more doesn't change it.
        sut.Record(new AuditEntry("c", "3", DateTime.UtcNow));
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    [Trait("Issue", "70")]
    public void MaskSensitive_LongValue_KeepsFirst4Chars()
    {
        var masked = AuditService.MaskSensitive("sk-1234abcdef");
        Assert.Equal("sk-1*********", masked);
    }

    [Fact]
    [Trait("Issue", "70")]
    public void MaskSensitive_ShortValue_MasksAll()
    {
        Assert.Equal("***", AuditService.MaskSensitive("abc"));
        Assert.Equal("****", AuditService.MaskSensitive(""));
    }

    [Fact]
    [Trait("Issue", "70")]
    public void AuditChannelIsPreRegistered()
    {
        var output = new OutputService();
        _ = new AuditService(output);

        var channels = output.GetChannelsSnapshot();
        Assert.Contains("Audit", channels);
    }
}
