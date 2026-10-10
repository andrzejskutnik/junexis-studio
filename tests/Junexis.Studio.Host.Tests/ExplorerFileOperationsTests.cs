using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for file operations in <see cref="ExplorerPanelViewModel"/> (#122):
/// Refresh, NewFileInSelection, DeleteSelectedNode.
/// </summary>
[Trait("Issue", "122")]
public sealed class ExplorerFileOperationsTests
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

    // ── Refresh ─────────────────────────────────────────────────

    [Fact]
    public void Refresh_AfterAddingFile_NewFileAppearsInTree()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            var (vm, _, _) = Create(tmp.FullName);

            var nodesCountBefore = vm.Nodes[0].Children.Count;

            var newFile = Path.Combine(tmp.FullName, "created_later.txt");
            File.WriteAllText(newFile, "x");

            vm.Refresh();

            var nodesCountAfter = vm.Nodes[0].Children.Count;
            Assert.True(nodesCountAfter > nodesCountBefore);
            Assert.Contains(vm.Nodes[0].Children, n => n.Name == "created_later.txt");
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Refresh_FiresPropertyChangedForNodes()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            var (vm, _, _) = Create(tmp.FullName);

            var fired = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.Nodes)) fired = true;
            };

            vm.Refresh();

            Assert.True(fired);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    // ── NewFileInSelection ───────────────────────────────────────

    [Fact]
    public void NewFileInSelection_NoSelection_CreatesInRoot()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            var (vm, bus, _) = Create(tmp.FullName);

            // No selected node — should create in workspace root
            FileOpenRequested? publishedEvt = null;
            bus.Subscribe<FileOpenRequested>(e => publishedEvt = e);

            var path = vm.NewFileInSelection();

            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.Equal(tmp.FullName, Path.GetDirectoryName(path));
            // Tree should be updated
            Assert.Contains(vm.Nodes[0].Children, n => n.FullPath == path);
            // FileOpenRequested published
            Assert.NotNull(publishedEvt);
            Assert.Equal(path, publishedEvt!.Path);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void NewFileInSelection_WithDirectorySelected_CreatesInsideDirectory()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(tmp.FullName, "subdir"));

            var (vm, _, _) = Create(tmp.FullName);

            // Select the subdirectory node
            var subDirNode = vm.Nodes[0].Children.FirstOrDefault(n => n.IsDirectory && n.Name == "subdir");
            Assert.NotNull(subDirNode);

            vm.SelectedNode = subDirNode;

            var path = vm.NewFileInSelection("mine.txt");

            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.Equal(sub.FullName, Path.GetDirectoryName(path));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void NewFileInSelection_WithFileSelected_CreatesInSameDirectory()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            var existing = Path.Combine(tmp.FullName, "existing.txt");
            File.WriteAllText(existing, "hello");

            var (vm, _, _) = Create(tmp.FullName);

            var fileNode = vm.Nodes[0].Children.FirstOrDefault(n => !n.IsDirectory && n.Name == "existing.txt");
            Assert.NotNull(fileNode);

            vm.SelectedNode = fileNode;

            var path = vm.NewFileInSelection("sibling.txt");

            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.Equal(tmp.FullName, Path.GetDirectoryName(path));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void NewFileInSelection_DuplicateName_AutoIncrementsName()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            // Pre-create NewFile.txt
            File.WriteAllText(Path.Combine(tmp.FullName, "NewFile.txt"), "x");

            var (vm, _, _) = Create(tmp.FullName);

            var path = vm.NewFileInSelection(); // no name → auto-generate

            Assert.NotNull(path);
            Assert.NotEqual("NewFile.txt", Path.GetFileName(path));
            Assert.True(File.Exists(path));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    // ── DeleteSelectedNode ───────────────────────────────────────

    [Fact]
    public void DeleteSelectedNode_File_DeletesFromDiskAndRefreshesTree()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "todelete.txt");
            File.WriteAllText(filePath, "bye");

            var (vm, _, _) = Create(tmp.FullName);

            var fileNode = vm.Nodes[0].Children.FirstOrDefault(n => n.Name == "todelete.txt");
            Assert.NotNull(fileNode);

            vm.SelectedNode = fileNode;
            var result = vm.DeleteSelectedNode();

            Assert.True(result);
            Assert.False(File.Exists(filePath));
            Assert.DoesNotContain(vm.Nodes[0].Children, n => n.Name == "todelete.txt");
            Assert.Null(vm.SelectedNode);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void DeleteSelectedNode_EmptyDirectory_DeletesAndRefreshes()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            var emptyDir = Directory.CreateDirectory(Path.Combine(tmp.FullName, "emptydir"));

            var (vm, _, _) = Create(tmp.FullName);

            var dirNode = vm.Nodes[0].Children.FirstOrDefault(n => n.IsDirectory && n.Name == "emptydir");
            Assert.NotNull(dirNode);

            vm.SelectedNode = dirNode;
            var result = vm.DeleteSelectedNode();

            Assert.True(result);
            Assert.False(Directory.Exists(emptyDir.FullName));
            Assert.DoesNotContain(vm.Nodes[0].Children, n => n.Name == "emptydir");
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void DeleteSelectedNode_NonEmptyDirectory_ReturnsFalseAndDoesNotDelete()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            var dirPath = Directory.CreateDirectory(Path.Combine(tmp.FullName, "nonempty"));
            File.WriteAllText(Path.Combine(dirPath.FullName, "child.txt"), "x");

            var (vm, _, output) = Create(tmp.FullName);

            var dirNode = vm.Nodes[0].Children.FirstOrDefault(n => n.IsDirectory && n.Name == "nonempty");
            Assert.NotNull(dirNode);

            vm.SelectedNode = dirNode;
            var result = vm.DeleteSelectedNode();

            Assert.False(result);
            Assert.True(Directory.Exists(dirPath.FullName));

            var snap = output.GetSnapshot();
            Assert.Contains(snap, e => e.Message.Contains("nie jest pusty"));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void DeleteSelectedNode_NoSelection_ReturnsFalse()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            var (vm, _, _) = Create(tmp.FullName);
            // No SelectedNode
            var result = vm.DeleteSelectedNode();
            Assert.False(result);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    // ── SelectedNode INPC ────────────────────────────────────────

    [Fact]
    public void SelectedNode_FiresPropertyChanged()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_122_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "x");

            var (vm, _, _) = Create(tmp.FullName);

            var fired = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.SelectedNode)) fired = true;
            };

            var node = vm.Nodes[0].Children.FirstOrDefault(n => n.Name == "a.txt");
            vm.SelectedNode = node;

            Assert.True(fired);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    // ── ExplorerExtension commands ───────────────────────────────

    [Fact]
    public void JunexisStudioHost_RegistersExplorerFileCommands()
    {
        var host = new JunexisStudioHost();
        host.Start();

        var cmds = host.Context.Commands.GetRegisteredCommands();

        Assert.Contains(cmds, c => c.Id == "explorer.newFile");
        Assert.Contains(cmds, c => c.Id == "explorer.delete");
        Assert.Contains(cmds, c => c.Id == "explorer.refresh");

        host.Shutdown();
    }

    [Fact]
    public void JunexisStudioHost_RegistersFileMenuFileCommands()
    {
        var host = new JunexisStudioHost();
        host.Start();

        var cmds = host.Context.Commands.GetRegisteredCommands();

        Assert.Contains(cmds, c => c.Id == "explorer.newFile" && c.MenuPath == "File/New File in Workspace...");
        Assert.Contains(cmds, c => c.Id == "explorer.newFolder" && c.MenuPath == "File/New Folder in Workspace...");
        Assert.Contains(cmds, c => c.Id == "explorer.rename" && c.MenuPath == "File/Rename Selected Item...");
        Assert.Contains(cmds, c => c.Id == "explorer.move" && c.MenuPath == "File/Move Selected Item to...");
        Assert.Contains(cmds, c => c.Id == "explorer.delete" && c.MenuPath == "File/Delete Selected Item");
        Assert.Contains(cmds, c => c.Id == "explorer.refresh" && c.MenuPath == "Workspace/Refresh Explorer");

        host.Shutdown();
    }
}
