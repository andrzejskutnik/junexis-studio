using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using System;
using System.IO;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests verifying that <see cref="EditorPanelViewModel"/> reacts to
/// <see cref="FileRenamedEvent"/> published by the Explorer (#124).
/// </summary>
[Trait("Issue", "124")]
public sealed class EditorHandlesExplorerRenameTests
{
    private static (EditorPanelViewModel editor, EventBus bus) CreateEditor(string root)
    {
        var ws = new WorkspaceService();
        ws.OpenFolder(root);
        var bus = new EventBus();
        var output = new OutputService();
        var active = new ActiveDocumentService();
        var editor = new EditorPanelViewModel(ws, bus, output, active);
        return (editor, bus);
    }

    // ── Tab path update ──────────────────────────────────────────

    [Fact]
    public void FileRenamedEvent_UpdatesOpenTabFilePath()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_ed_");
        try
        {
            var oldPath = Path.Combine(tmp.FullName, "original.txt");
            var newPath = Path.Combine(tmp.FullName, "renamed.txt");
            File.WriteAllText(oldPath, "content");
            File.Move(oldPath, newPath); // simulate: file already renamed on disk

            var (editor, bus) = CreateEditor(tmp.FullName);

            // Open with old path (simulate tab opened before rename)
            File.WriteAllText(oldPath, "content");
            bus.Publish(new FileOpenRequested(oldPath));
            Assert.Equal(oldPath, editor.CurrentPath);

            // Explorer renames file and publishes event
            bus.Publish(new FileRenamedEvent(oldPath, newPath));

            Assert.Equal(newPath, editor.CurrentPath);
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void FileRenamedEvent_TabNotOpen_DoesNotThrow()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_ed_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "noopen.txt");
            File.WriteAllText(filePath, "x");

            var (editor, bus) = CreateEditor(tmp.FullName);

            // No tab opened — publishing rename should be a no-op (no throw)
            var ex = Record.Exception(() => bus.Publish(
                new FileRenamedEvent(filePath, Path.Combine(tmp.FullName, "other.txt"))));

            Assert.Null(ex);
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void FileRenamedEvent_DifferentOldPath_LeavesActiveTabUnchanged()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_ed_");
        try
        {
            var fileA = Path.Combine(tmp.FullName, "a.txt");
            var fileB = Path.Combine(tmp.FullName, "b.txt");
            File.WriteAllText(fileA, "a");
            File.WriteAllText(fileB, "b");

            var (editor, bus) = CreateEditor(tmp.FullName);
            bus.Publish(new FileOpenRequested(fileA));
            Assert.Equal(fileA, editor.CurrentPath);

            // Rename of B should not affect A's tab
            var newB = Path.Combine(tmp.FullName, "b_new.txt");
            bus.Publish(new FileRenamedEvent(fileB, newB));

            Assert.Equal(fileA, editor.CurrentPath);
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void FileRenamedEvent_EmptyPaths_DoesNotThrow()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_124_ed_");
        try
        {
            var (editor, bus) = CreateEditor(tmp.FullName);

            var ex1 = Record.Exception(() => bus.Publish(new FileRenamedEvent(string.Empty, "x.txt")));
            var ex2 = Record.Exception(() => bus.Publish(new FileRenamedEvent("x.txt", string.Empty)));

            Assert.Null(ex1);
            Assert.Null(ex2);
        }
        finally
        {
            try { new DirectoryInfo(tmp.FullName).Delete(recursive: true); } catch { }
        }
    }
}
