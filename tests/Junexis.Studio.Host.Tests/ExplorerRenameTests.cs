using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="ExplorerPanelViewModel.RenameSelectedNode"/> (#123).
/// </summary>
[Trait("Issue", "123")]
public sealed class ExplorerRenameTests
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

    // ── Basic rename ─────────────────────────────────────────────

    [Fact]
    public void RenameSelectedNode_File_RenamesOnDiskAndRefreshesTree()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_123_");
        try
        {
            var oldPath = Path.Combine(tmp.FullName, "original.txt");
            File.WriteAllText(oldPath, "hello");

            var (vm, _, _) = Create(tmp.FullName);

            var node = vm.Nodes[0].Children.First(n => n.Name == "original.txt");
            vm.SelectedNode = node;

            var result = vm.RenameSelectedNode("renamed.txt");

            Assert.True(result);
            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(Path.Combine(tmp.FullName, "renamed.txt")));
            Assert.Contains(vm.Nodes[0].Children, n => n.Name == "renamed.txt");
            Assert.DoesNotContain(vm.Nodes[0].Children, n => n.Name == "original.txt");
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void RenameSelectedNode_Directory_RenamesDirAndRefreshes()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_123_");
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp.FullName, "olddir"));

            var (vm, _, _) = Create(tmp.FullName);

            var node = vm.Nodes[0].Children.First(n => n.IsDirectory && n.Name == "olddir");
            vm.SelectedNode = node;

            var result = vm.RenameSelectedNode("newdir");

            Assert.True(result);
            Assert.False(Directory.Exists(Path.Combine(tmp.FullName, "olddir")));
            Assert.True(Directory.Exists(Path.Combine(tmp.FullName, "newdir")));
            Assert.Contains(vm.Nodes[0].Children, n => n.Name == "newdir");
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    // ── FileRenamedEvent ─────────────────────────────────────────

    [Fact]
    public void RenameSelectedNode_PublishesFileRenamedEvent()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_123_");
        try
        {
            var oldPath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(oldPath, "x");

            var (vm, bus, _) = Create(tmp.FullName);

            FileRenamedEvent? evt = null;
            bus.Subscribe<FileRenamedEvent>(e => evt = e);

            var node = vm.Nodes[0].Children.First(n => n.Name == "a.txt");
            vm.SelectedNode = node;
            vm.RenameSelectedNode("b.txt");

            Assert.NotNull(evt);
            Assert.Equal(oldPath, evt!.OldPath);
            Assert.Equal(Path.Combine(tmp.FullName, "b.txt"), evt.NewPath);
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void RenameSelectedNode_ClearsSelectedNodeAfterRename()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_123_");
        try
        {
            File.WriteAllText(Path.Combine(tmp.FullName, "x.txt"), "");
            var (vm, _, _) = Create(tmp.FullName);

            var node = vm.Nodes[0].Children.First(n => n.Name == "x.txt");
            vm.SelectedNode = node;
            vm.RenameSelectedNode("y.txt");

            Assert.Null(vm.SelectedNode);
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    // ── Validation errors ────────────────────────────────────────

    [Fact]
    public void RenameSelectedNode_NoSelection_ReturnsFalse()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_123_");
        try
        {
            var (vm, _, _) = Create(tmp.FullName);
            Assert.False(vm.RenameSelectedNode("anything.txt"));
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void RenameSelectedNode_EmptyName_ReturnsFalse()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_123_");
        try
        {
            File.WriteAllText(Path.Combine(tmp.FullName, "a.txt"), "");
            var (vm, _, output) = Create(tmp.FullName);

            var node = vm.Nodes[0].Children.First(n => n.Name == "a.txt");
            vm.SelectedNode = node;

            Assert.False(vm.RenameSelectedNode("   "));
            Assert.True(File.Exists(Path.Combine(tmp.FullName, "a.txt")));
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void RenameSelectedNode_InvalidChars_ReturnsFalse()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_123_");
        try
        {
            File.WriteAllText(Path.Combine(tmp.FullName, "a.txt"), "");
            var (vm, _, _) = Create(tmp.FullName);

            var node = vm.Nodes[0].Children.First(n => n.Name == "a.txt");
            vm.SelectedNode = node;

            // ':' is invalid on Windows
            Assert.False(vm.RenameSelectedNode("bad:name.txt"));
            Assert.True(File.Exists(Path.Combine(tmp.FullName, "a.txt")));
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void RenameSelectedNode_NameAlreadyExists_ReturnsFalse()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_123_");
        try
        {
            File.WriteAllText(Path.Combine(tmp.FullName, "a.txt"), "");
            File.WriteAllText(Path.Combine(tmp.FullName, "b.txt"), "");

            var (vm, _, _) = Create(tmp.FullName);

            var node = vm.Nodes[0].Children.First(n => n.Name == "a.txt");
            vm.SelectedNode = node;

            Assert.False(vm.RenameSelectedNode("b.txt"));
            Assert.True(File.Exists(Path.Combine(tmp.FullName, "a.txt")));
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    [Fact]
    public void RenameSelectedNode_SameName_ReturnsTrueWithoutMove()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_123_");
        try
        {
            File.WriteAllText(Path.Combine(tmp.FullName, "same.txt"), "");
            var (vm, bus, _) = Create(tmp.FullName);

            FileRenamedEvent? evt = null;
            bus.Subscribe<FileRenamedEvent>(e => evt = e);

            var node = vm.Nodes[0].Children.First(n => n.Name == "same.txt");
            vm.SelectedNode = node;

            var result = vm.RenameSelectedNode("same.txt");

            Assert.True(result);
            Assert.True(File.Exists(Path.Combine(tmp.FullName, "same.txt")));
            Assert.Null(evt); // no event — nothing actually changed
        }
        finally { try { tmp.Delete(recursive: true); } catch { } }
    }

    // ── JunexisStudioHost registers command ──────────────────────────────

    [Fact]
    public void JunexisStudioHost_RegistersExplorerRenameCommand()
    {
        var host = new JunexisStudioHost();
        host.Start();

        Assert.Contains(host.Context.Commands.GetRegisteredCommands(),
            c => c.Id == "explorer.rename" && c.MenuPath == "File/Rename Selected Item...");

        host.Shutdown();
    }
}
