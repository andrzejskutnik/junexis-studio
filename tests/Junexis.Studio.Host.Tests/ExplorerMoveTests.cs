using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System.IO;
using System.Linq;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="ExplorerPanelViewModel.MoveSelectedNode"/> (#128).
/// </summary>
[Trait("Issue", "128")]
public sealed class ExplorerMoveTests
{
    private static (ExplorerPanelViewModel vm, EventBus bus, OutputService output) Create(string root)
    {
        var ws = new WorkspaceService();
        ws.OpenFolder(root);
        var bus = new EventBus();
        var output = new OutputService();
        var vm = new ExplorerPanelViewModel(ws, bus, output, versionControl: null);
        return (vm, bus, output);
    }

    // ── Guards ────────────────────────────────────────────────────

    [Fact]
    public void MoveSelectedNode_NoSelection_ReturnsFalse()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_128_");
        try
        {
            var (vm, _, _) = Create(tmp.FullName);
            vm.SelectedNode = null;

            var result = vm.MoveSelectedNode(tmp.FullName);

            Assert.False(result);
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void MoveSelectedNode_NullDestination_ReturnsFalse()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_128_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "x");

            var (vm, _, _) = Create(tmp.FullName);
            vm.SelectedNode = vm.Nodes[0].Children.First(n => n.Name == "a.txt");

            var result = vm.MoveSelectedNode(null!);

            Assert.False(result);
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void MoveSelectedNode_NonExistentDestination_ReturnsFalse()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_128_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "x");

            var (vm, _, _) = Create(tmp.FullName);
            vm.SelectedNode = vm.Nodes[0].Children.First(n => n.Name == "a.txt");

            var result = vm.MoveSelectedNode(Path.Combine(tmp.FullName, "nonexistent"));

            Assert.False(result);
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void MoveSelectedNode_SameDirectory_ReturnsTrueNoOp()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_128_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "x");

            var (vm, _, _) = Create(tmp.FullName);
            vm.SelectedNode = vm.Nodes[0].Children.First(n => n.Name == "a.txt");

            var result = vm.MoveSelectedNode(tmp.FullName);

            Assert.True(result);
            Assert.True(File.Exists(filePath)); // unchanged
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void MoveSelectedNode_ConflictInDestination_ReturnsFalse()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_128_");
        try
        {
            var src = Path.Combine(tmp.FullName, "a.txt");
            var destDir = Path.Combine(tmp.FullName, "sub");
            Directory.CreateDirectory(destDir);
            File.WriteAllText(src, "source");
            File.WriteAllText(Path.Combine(destDir, "a.txt"), "conflict");

            var (vm, _, _) = Create(tmp.FullName);
            vm.SelectedNode = vm.Nodes[0].Children.First(n => n.Name == "a.txt");

            var result = vm.MoveSelectedNode(destDir);

            Assert.False(result);
            Assert.True(File.Exists(src)); // nie przeniesiony
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    // ── File move ─────────────────────────────────────────────────

    [Fact]
    public void MoveSelectedNode_File_MovesOnDiskAndRefreshesTree()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_128_");
        try
        {
            var src = Path.Combine(tmp.FullName, "file.txt");
            var destDir = Path.Combine(tmp.FullName, "sub");
            Directory.CreateDirectory(destDir);
            File.WriteAllText(src, "hello");

            var (vm, _, _) = Create(tmp.FullName);
            vm.SelectedNode = vm.Nodes[0].Children.First(n => n.Name == "file.txt");

            var result = vm.MoveSelectedNode(destDir);

            Assert.True(result);
            Assert.False(File.Exists(src));
            Assert.True(File.Exists(Path.Combine(destDir, "file.txt")));
            // Po Refresh korzeń nie powinien zawierać file.txt bezpośrednio
            Assert.DoesNotContain(vm.Nodes[0].Children, n => n.Name == "file.txt" && !n.IsDirectory);
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void MoveSelectedNode_File_PublishesFileRenamedEvent()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_128_");
        try
        {
            var src = Path.Combine(tmp.FullName, "a.txt");
            var destDir = Path.Combine(tmp.FullName, "sub");
            Directory.CreateDirectory(destDir);
            File.WriteAllText(src, "x");

            var (vm, bus, _) = Create(tmp.FullName);
            FileRenamedEvent? evt = null;
            bus.Subscribe<FileRenamedEvent>(e => evt = e);

            vm.SelectedNode = vm.Nodes[0].Children.First(n => n.Name == "a.txt");
            vm.MoveSelectedNode(destDir);

            Assert.NotNull(evt);
            Assert.Equal(src, evt!.OldPath);
            Assert.Equal(Path.Combine(destDir, "a.txt"), evt.NewPath);
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void MoveSelectedNode_File_ClearsSelectedNode()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_128_");
        try
        {
            var src = Path.Combine(tmp.FullName, "a.txt");
            var destDir = Path.Combine(tmp.FullName, "sub");
            Directory.CreateDirectory(destDir);
            File.WriteAllText(src, "x");

            var (vm, _, _) = Create(tmp.FullName);
            vm.SelectedNode = vm.Nodes[0].Children.First(n => n.Name == "a.txt");

            vm.MoveSelectedNode(destDir);

            Assert.Null(vm.SelectedNode);
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    // ── Directory move ────────────────────────────────────────────

    [Fact]
    public void MoveSelectedNode_Directory_MovesDirAndRefreshesTree()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_128_");
        try
        {
            var srcDir = Path.Combine(tmp.FullName, "olddir");
            var destDir = Path.Combine(tmp.FullName, "sub");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            File.WriteAllText(Path.Combine(srcDir, "inner.txt"), "hi");

            var (vm, _, _) = Create(tmp.FullName);
            vm.SelectedNode = vm.Nodes[0].Children.First(n => n.IsDirectory && n.Name == "olddir");

            var result = vm.MoveSelectedNode(destDir);

            Assert.True(result);
            Assert.False(Directory.Exists(srcDir));
            Assert.True(Directory.Exists(Path.Combine(destDir, "olddir")));
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void MoveSelectedNode_Directory_PublishesFileRenamedEvent()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_128_");
        try
        {
            var srcDir = Path.Combine(tmp.FullName, "mydir");
            var destDir = Path.Combine(tmp.FullName, "sub");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);

            var (vm, bus, _) = Create(tmp.FullName);
            FileRenamedEvent? evt = null;
            bus.Subscribe<FileRenamedEvent>(e => evt = e);

            vm.SelectedNode = vm.Nodes[0].Children.First(n => n.IsDirectory && n.Name == "mydir");
            vm.MoveSelectedNode(destDir);

            Assert.NotNull(evt);
            Assert.Equal(srcDir, evt!.OldPath);
            Assert.Equal(Path.Combine(destDir, "mydir"), evt.NewPath);
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    // ── Command registration ──────────────────────────────────────

    [Fact]
    public void JunexisStudioHost_RegistersExplorerMoveCommand()
    {
        var host = new JunexisStudioHost();
        host.Start();

        Assert.Contains(host.Context.Commands.GetRegisteredCommands(),
            c => c.Id == "explorer.move" && c.MenuPath == "File/Move Selected Item to...");

        host.Shutdown();
    }
}
