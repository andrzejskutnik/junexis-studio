using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Host.Tests;

public sealed class DebugProcessServiceTests
{
    [Fact]
    public void GetProcesses_DeduplicatesAndOrdersAttachTargets()
    {
        var service = new DebugProcessService(() =>
        [
            new DebugProcessInfo(9, "node", "node.exe"),
            new DebugProcessInfo(4, "app", "app.exe"),
            new DebugProcessInfo(9, "duplicate", null),
            new DebugProcessInfo(0, "invalid", null),
        ]);

        var processes = service.GetProcesses();

        Assert.Equal([4, 9], processes.Select(item => item.Id));
    }
}
