using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Xunit;

namespace Junexis.Studio.Core.Tests;

[Trait("Issue", "37")]
[Trait("Issue", "106")]
public sealed class KeybindingRegistryTests
{
    [Fact]
    public void Bind_ThenTryGetCommand_ReturnsCommandId()
    {
        var reg = new KeybindingRegistry();
        var gesture = new KeyGesture("S", KeyModifiers.Ctrl);

        reg.Bind(gesture, "file.save");

        Assert.True(reg.TryGetCommand(gesture, out var cmd));
        Assert.Equal("file.save", cmd);
    }

    [Fact]
    public void TryGetCommand_NotBound_ReturnsFalse()
    {
        var reg = new KeybindingRegistry();

        Assert.False(reg.TryGetCommand(new KeyGesture("X"), out _));
    }

    [Fact]
    public void Bind_Conflict_LastWins()
    {
        var output = new OutputService();
        var reg = new KeybindingRegistry(output);
        var gesture = new KeyGesture("S", KeyModifiers.Ctrl);

        reg.Bind(gesture, "file.save");
        reg.Bind(gesture, "file.saveAll");

        Assert.True(reg.TryGetCommand(gesture, out var cmd));
        Assert.Equal("file.saveAll", cmd);

        // Conflict logged.
        var logs = output.GetSnapshot();
        Assert.Contains(logs, e => e.Message.Contains("conflict") && e.Message.Contains("file.save"));
    }

    [Fact]
    public void Bind_SameCommandAgain_NoConflictLog()
    {
        var output = new OutputService();
        var reg = new KeybindingRegistry(output);
        var gesture = new KeyGesture("S", KeyModifiers.Ctrl);

        reg.Bind(gesture, "file.save");
        reg.Bind(gesture, "file.save");

        var logs = output.GetSnapshot();
        Assert.DoesNotContain(logs, e => e.Message.Contains("conflict"));
    }

    [Fact]
    public void Unbind_RemovesBinding()
    {
        var reg = new KeybindingRegistry();
        var gesture = new KeyGesture("S", KeyModifiers.Ctrl);

        reg.Bind(gesture, "file.save");
        reg.Unbind(gesture);

        Assert.False(reg.TryGetCommand(gesture, out _));
    }

    [Fact]
    public void GetBindings_ReturnsSnapshot()
    {
        var reg = new KeybindingRegistry();
        reg.Bind(new KeyGesture("S", KeyModifiers.Ctrl), "file.save");
        reg.Bind(new KeyGesture("O", KeyModifiers.Ctrl), "file.open");

        var bindings = reg.GetBindings();

        Assert.Equal(2, bindings.Count);
    }

    [Fact]
    public void GetBindings_ReturnsCopy()
    {
        var reg = new KeybindingRegistry();
        reg.Bind(new KeyGesture("S", KeyModifiers.Ctrl), "file.save");

        var snap1 = reg.GetBindings();
        reg.Bind(new KeyGesture("O", KeyModifiers.Ctrl), "file.open");
        var snap2 = reg.GetBindings();

        Assert.Single(snap1);
        Assert.Equal(2, snap2.Count);
    }

    [Fact]
    public void KeyGesture_ToString_FormatsCorrectly()
    {
        Assert.Equal("Ctrl+S", new KeyGesture("S", KeyModifiers.Ctrl).ToString());
        Assert.Equal("Ctrl+Shift+S", new KeyGesture("S", KeyModifiers.Ctrl | KeyModifiers.Shift).ToString());
        Assert.Equal("Alt+F4", new KeyGesture("F4", KeyModifiers.Alt).ToString());
        Assert.Equal("F5", new KeyGesture("F5").ToString());
    }

    [Fact]
    public void KeyGesture_RecordEquality_MatchesCorrectly()
    {
        var g1 = new KeyGesture("S", KeyModifiers.Ctrl);
        var g2 = new KeyGesture("S", KeyModifiers.Ctrl);

        Assert.Equal(g1, g2);
        Assert.Equal(g1.GetHashCode(), g2.GetHashCode());
    }

    [Fact]
    public void Bind_Null_Throws()
    {
        var reg = new KeybindingRegistry();

        Assert.Throws<ArgumentNullException>(() => reg.Bind(null!, "cmd"));
        Assert.Throws<ArgumentNullException>(() => reg.Bind(new KeyGesture("S"), null!));
    }
}
