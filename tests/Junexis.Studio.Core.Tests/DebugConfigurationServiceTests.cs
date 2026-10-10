using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;

namespace Junexis.Studio.Core.Tests;

public sealed class DebugConfigurationServiceTests
{
    [Fact]
    public void WorkspaceStore_PersistsConfigurationsAndSelection()
    {
        var root = Directory.CreateTempSubdirectory("junexis-debug-config-");
        try
        {
            var firstWorkspace = new WorkspaceService();
            firstWorkspace.OpenFolder(root.FullName);
            var first = new DebugConfigurationService(firstWorkspace);
            first.Upsert(new DebugLaunchConfiguration(
                "attach-node", "Attach Node", "typescript", DebugConfigurationRequest.Attach,
                ProcessId: 4242, WorkingDirectory: ".", Arguments: ["--inspect"]));

            var path = Path.Combine(root.FullName, ".junexis", "studio-debug.json");
            Assert.True(File.Exists(path));
            Assert.Equal("attach-node", first.SelectedId);

            var secondWorkspace = new WorkspaceService();
            secondWorkspace.OpenFolder(root.FullName);
            var second = new DebugConfigurationService(secondWorkspace);

            var restored = Assert.Single(second.GetConfigurations(), item => item.Id == "attach-node");
            Assert.Equal(DebugConfigurationRequest.Attach, restored.Request);
            Assert.Equal(4242, restored.ProcessId);
            Assert.Equal("attach-node", second.SelectedId);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
