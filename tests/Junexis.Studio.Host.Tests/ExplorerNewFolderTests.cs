using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="ExplorerPanelViewModel.NewFolderInSelection"/> and
/// the <c>explorer.newFolder</c> command registration (#124).
/// </summary>
[Trait("Issue", "124")]
public sealed class ExplorerNewFolderTests
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

    // ── NewFolderInSelection — basic creation ────────────────────

    [Fact]
    public void NewFolderInSelection_NoSelection_CreatesInWorkspaceRoot()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_");
        try
        {
            var (vm, _, _) = Create(tmp.FullName);
            // no selection

            var result = vm.NewFolderInSelection("MyFolder");

            Assert.NotNull(result);
            Assert.True(Directory.Exists(result));
            Assert.Equal(Path.Combine(tmp.FullName, "MyFolder"), result);
            Assert.Contains(vm.Nodes[0].Children, n => n.Name == "MyFolder" && n.IsDirectory);
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void NewFolderInSelection_DirectorySelected_CreatesInsideSelection()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_");
        try
        {
            var subDir = Directory.CreateDirectory(Path.Combine(tmp.FullName, "parent"));

            var (vm, _, _) = Create(tmp.FullName);

            var parentNode = vm.Nodes[0].Children.First(n => n.Name == "parent" && n.IsDirectory);
            vm.SelectedNode = parentNode;

            var result = vm.NewFolderInSelection("child");

            Assert.NotNull(result);
            Assert.Equal(Path.Combine(subDir.FullName, "child"), result);
            Assert.True(Directory.Exists(result));
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void NewFolderInSelection_FileSelected_CreatesInParentDirectory()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_");
        try
        {
            var file = Path.Combine(tmp.FullName, "note.txt");
            File.WriteAllText(file, "x");

            var (vm, _, _) = Create(tmp.FullName);

            var fileNode = vm.Nodes[0].Children.First(n => n.Name == "note.txt");
            vm.SelectedNode = fileNode;

            var result = vm.NewFolderInSelection("siblings");

            Assert.NotNull(result);
            Assert.Equal(Path.Combine(tmp.FullName, "siblings"), result);
            Assert.True(Directory.Exists(result));
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }

    // ── Validation ───────────────────────────────────────────────

    [Fact]
    public void NewFolderInSelection_DuplicateName_ReturnsNull()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_");
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp.FullName, "Existing"));

            var (vm, _, _) = Create(tmp.FullName);

            var result = vm.NewFolderInSelection("Existing");

            Assert.Null(result);
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void NewFolderInSelection_InvalidChars_ReturnsNull()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_");
        try
        {
            var (vm, _, _) = Create(tmp.FullName);

            var result = vm.NewFolderInSelection("bad:name*");

            Assert.Null(result);
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void NewFolderInSelection_AutoName_CreatesUniqueFolder()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_");
        try
        {
            var (vm, _, _) = Create(tmp.FullName);

            var result = vm.NewFolderInSelection(); // null name → auto

            Assert.NotNull(result);
            Assert.True(Directory.Exists(result));
            // auto name should start with "NewFolder"
            Assert.StartsWith("NewFolder", Path.GetFileName(result));
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void NewFolderInSelection_RefreshesTree()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_");
        try
        {
            var (vm, _, _) = Create(tmp.FullName);

            var beforeCount = vm.Nodes[0].Children.Count;

            vm.NewFolderInSelection("FreshDir");

            Assert.True(vm.Nodes[0].Children.Count > beforeCount);
            Assert.Contains(vm.Nodes[0].Children, n => n.Name == "FreshDir" && n.IsDirectory);
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }

    // ── JunexisStudioHost command registration ───────────────────────────

    [Fact]
    public void JunexisStudioHost_RegistersExplorerNewFolderCommand()
    {
        var host = new JunexisStudioHost();
        host.Start();

        Assert.Contains(host.Context.Commands.GetRegisteredCommands(),
            c => c.Id == "explorer.newFolder" && c.MenuPath == "File/New Folder in Workspace...");

        host.Shutdown();
    }
}
