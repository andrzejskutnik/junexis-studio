using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public sealed class EditorPanelViewModelTests
{
    [Fact]
    [Trait("Issue", "12")]
    public void FileOpenRequested_LoadsContent_AndNotDirty()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "hello");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(filePath));

            Assert.Equal(filePath, sut.CurrentPath);
            Assert.Equal("hello", sut.Content);
            Assert.False(sut.IsDirty);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "12")]
    public void Save_WritesFile_AndClearsDirty()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "hello");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);
            bus.Publish(new FileOpenRequested(filePath));

            sut.Content = "changed";
            Assert.True(sut.IsDirty);

            sut.Save();

            Assert.False(sut.IsDirty);
            Assert.Equal("changed", File.ReadAllText(filePath));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "42")]
    public void PublishesDocumentOpenedAndChangedAndSaved()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "hello");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var opened = 0;
            var changed = 0;
            var saved = 0;

            bus.Subscribe<DocumentOpened>(e => { if (e.Path == filePath) opened++; });
            bus.Subscribe<DocumentChanged>(e => { if (e.Path == filePath) changed++; });
            bus.Subscribe<DocumentSaved>(e => { if (e.Path == filePath) saved++; });

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(filePath));
            Assert.Equal(1, opened);

            sut.Content = "changed";
            Assert.True(changed >= 1);

            sut.Save();
            Assert.Equal(1, saved);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "43")]
    public void FileChangedExternally_WhenNotDirty_ReloadsAndClearsOutOfDate()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "v1");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);
            bus.Publish(new FileOpenRequested(filePath));
            Assert.Equal("v1", sut.Content);
            Assert.False(sut.IsDirty);

            File.WriteAllText(filePath, "v2");
            bus.Publish(new FileChangedExternally(filePath));

            Assert.Equal("v2", sut.Content);
            Assert.False(sut.IsDirty);
            Assert.False(sut.IsOutOfDate);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "43")]
    public void FileChangedExternally_WhenDirty_MarksOutOfDateAndDoesNotReload()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "v1");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);
            bus.Publish(new FileOpenRequested(filePath));

            sut.Content = "local";
            Assert.True(sut.IsDirty);

            File.WriteAllText(filePath, "external");
            bus.Publish(new FileChangedExternally(filePath));

            Assert.Equal("local", sut.Content);
            Assert.True(sut.IsDirty);
            Assert.True(sut.IsOutOfDate);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Regresja #102: PropertyChanged("Content") musi strzelić gdy plik jest otwierany,
    /// nawet gdy _suppressDirty = true. Widok (EditorPanelView) zależy na tym zdarzeniu
    /// do synchronizacji AvaloniaEdit TextEditor z VM.
    /// </summary>
    [Fact]
    [Trait("Issue", "102")]
    public void FileOpenRequested_ContentPropertyChangedFires_EvenWithSuppressDirty()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "b.txt");
            File.WriteAllText(filePath, "file content");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            var contentChanged = false;
            sut.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(EditorPanelViewModel.Content))
                    contentChanged = true;
            };

            bus.Publish(new FileOpenRequested(filePath));

            Assert.True(contentChanged,
                "PropertyChanged(\"Content\") musi strzelić przy otwarciu pliku. " +
                "EditorPanelView zależy na tym zdarzeniu do aktualizacji AvaloniaEdit.");
            Assert.Equal("file content", sut.Content);
            Assert.False(sut.IsDirty);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Regresja #102: po otwarciu jednego pliku, otwarcie INNEGO pliku musi znów
    /// wystrzelić PropertyChanged("Content") i zaktualizować Content do nowej treści.
    /// </summary>
    [Fact]
    [Trait("Issue", "102")]
    public void FileOpenRequested_SwitchingFiles_UpdatesContentAndFiresPropertyChanged()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "first.txt");
            var file2 = Path.Combine(tmp.FullName, "second.txt");
            File.WriteAllText(file1, "content 1");
            File.WriteAllText(file2, "content 2");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            Assert.Equal("content 1", sut.Content);

            var contentChangedCount = 0;
            sut.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(EditorPanelViewModel.Content))
                    contentChangedCount++;
            };

            bus.Publish(new FileOpenRequested(file2));

            Assert.Equal("content 2", sut.Content);
            Assert.Equal(file2, sut.CurrentPath);
            Assert.True(contentChangedCount >= 1,
                "Przełączenie na inny plik musi wystrzelić PropertyChanged(\"Content\").");
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    // ─── Multi-tab tests ────────────────────────────────────────

    [Fact]
    [Trait("Issue", "103")]
    public void OpeningMultipleFiles_CreatesMultipleTabs()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            var file2 = Path.Combine(tmp.FullName, "b.txt");
            var file3 = Path.Combine(tmp.FullName, "c.txt");
            File.WriteAllText(file1, "aaa");
            File.WriteAllText(file2, "bbb");
            File.WriteAllText(file3, "ccc");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            bus.Publish(new FileOpenRequested(file2));
            bus.Publish(new FileOpenRequested(file3));

            Assert.Equal(3, sut.Tabs.Count);
            Assert.Equal(file3, sut.ActiveTab?.FilePath);
            Assert.Equal("ccc", sut.Content);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void OpeningSameFileTwice_DoesNotDuplicate_ActivatesExisting()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(file1, "aaa");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            bus.Publish(new FileOpenRequested(file1));

            Assert.Single(sut.Tabs);
            Assert.Equal(file1, sut.ActiveTab?.FilePath);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void CloseTab_RemovesTab_PublishesDocumentClosed()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            var file2 = Path.Combine(tmp.FullName, "b.txt");
            File.WriteAllText(file1, "aaa");
            File.WriteAllText(file2, "bbb");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var closedPaths = new List<string>();
            bus.Subscribe<DocumentClosed>(e => closedPaths.Add(e.Path));

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            bus.Publish(new FileOpenRequested(file2));

            Assert.Equal(2, sut.Tabs.Count);

            var tabToClose = sut.Tabs[0]; // file1
            sut.CloseTab(tabToClose);

            Assert.Single(sut.Tabs);
            Assert.Equal(file2, sut.Tabs[0].FilePath);
            Assert.Contains(file1, closedPaths);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void CloseActiveTab_ActivatesNeighbor()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            var file2 = Path.Combine(tmp.FullName, "b.txt");
            var file3 = Path.Combine(tmp.FullName, "c.txt");
            File.WriteAllText(file1, "aaa");
            File.WriteAllText(file2, "bbb");
            File.WriteAllText(file3, "ccc");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            bus.Publish(new FileOpenRequested(file2));
            bus.Publish(new FileOpenRequested(file3));

            // Aktywuj środkowy tab
            sut.ActiveTab = sut.Tabs[1];
            Assert.Equal(file2, sut.ActiveTab?.FilePath);

            // Zamknij aktywny (środkowy)
            sut.CloseTab(sut.ActiveTab!);

            Assert.Equal(2, sut.Tabs.Count);
            Assert.NotNull(sut.ActiveTab);
            // Po zamknięciu środkowego, powinien aktywować sąsiedni (indeks clamp)
            Assert.True(
                sut.ActiveTab!.FilePath == file1 || sut.ActiveTab!.FilePath == file3,
                "Po zamknięciu aktywnego taba powinien aktywować sąsiedni.");
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void CloseLastTab_SetsActiveTabToNull()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(file1, "aaa");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            Assert.Single(sut.Tabs);

            sut.CloseTab(sut.Tabs[0]);

            Assert.Empty(sut.Tabs);
            Assert.Null(sut.ActiveTab);
            Assert.Null(sut.CurrentPath);
            Assert.False(sut.HasOpenTabs);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void HasOpenTabs_ReflectsTabCount()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(file1, "aaa");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            Assert.False(sut.HasOpenTabs);

            bus.Publish(new FileOpenRequested(file1));
            Assert.True(sut.HasOpenTabs);

            sut.CloseTab(sut.Tabs[0]);
            Assert.False(sut.HasOpenTabs);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void RequestCloseOnTab_ClosesViaParent()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(file1, "aaa");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            Assert.Single(sut.Tabs);

            // Symuluj kliknięcie przycisku X
            sut.Tabs[0].RequestClose();

            Assert.Empty(sut.Tabs);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void SwitchingActiveTab_UpdatesCurrentPathAndContent()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            var file2 = Path.Combine(tmp.FullName, "b.txt");
            File.WriteAllText(file1, "aaa");
            File.WriteAllText(file2, "bbb");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            bus.Publish(new FileOpenRequested(file2));

            Assert.Equal("bbb", sut.Content);
            Assert.Equal(file2, sut.CurrentPath);

            sut.ActiveTab = sut.Tabs[0];

            Assert.Equal("aaa", sut.Content);
            Assert.Equal(file1, sut.CurrentPath);
            Assert.Equal(file1, active.ActivePath);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "106")]
    public void ActivateNextTab_WrapsFromLastToFirst()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            var file2 = Path.Combine(tmp.FullName, "b.txt");
            var file3 = Path.Combine(tmp.FullName, "c.txt");
            File.WriteAllText(file1, "aaa");
            File.WriteAllText(file2, "bbb");
            File.WriteAllText(file3, "ccc");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            bus.Publish(new FileOpenRequested(file2));
            bus.Publish(new FileOpenRequested(file3));

            sut.ActiveTab = sut.Tabs[2];

            sut.ActivateNextTab();

            Assert.Same(sut.Tabs[0], sut.ActiveTab);
            Assert.Equal(file1, active.ActivePath);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "106")]
    public void ActivatePreviousTab_WrapsFromFirstToLast()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            var file2 = Path.Combine(tmp.FullName, "b.txt");
            var file3 = Path.Combine(tmp.FullName, "c.txt");
            File.WriteAllText(file1, "aaa");
            File.WriteAllText(file2, "bbb");
            File.WriteAllText(file3, "ccc");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            bus.Publish(new FileOpenRequested(file2));
            bus.Publish(new FileOpenRequested(file3));

            sut.ActiveTab = sut.Tabs[0];

            sut.ActivatePreviousTab();

            Assert.Same(sut.Tabs[2], sut.ActiveTab);
            Assert.Equal(file3, active.ActivePath);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void EachTabHasOwnContent_IndependentEditing()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            var file2 = Path.Combine(tmp.FullName, "b.txt");
            File.WriteAllText(file1, "aaa");
            File.WriteAllText(file2, "bbb");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            bus.Publish(new FileOpenRequested(file2));

            // Edytuj aktywny tab (file2)
            sut.Content = "bbb-modified";
            Assert.True(sut.Tabs[1].IsDirty);

            // Przełącz na tab 1 (file1)
            sut.ActiveTab = sut.Tabs[0];
            Assert.Equal("aaa", sut.Content);
            Assert.False(sut.IsDirty);

            // Przełącz z powrotem — file2 ma zachowaną zmianę
            sut.ActiveTab = sut.Tabs[1];
            Assert.Equal("bbb-modified", sut.Content);
            Assert.True(sut.IsDirty);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "103")]
    public void FileChangedExternally_RoutedToCorrectTab()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            var file2 = Path.Combine(tmp.FullName, "b.txt");
            File.WriteAllText(file1, "v1");
            File.WriteAllText(file2, "v1");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();

            var sut = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            bus.Publish(new FileOpenRequested(file2));

            // Zmień file1 na dysku
            File.WriteAllText(file1, "v2");
            bus.Publish(new FileChangedExternally(file1));

            // Tab file1 powinien mieć nową treść
            Assert.Equal("v2", sut.Tabs[0].Content);
            // Tab file2 nie zmieniony
            Assert.Equal("v1", sut.Tabs[1].Content);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    // -----------------------------------------------------------------------
    // Issue #105 – Unsaved changes dialog
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Issue", "105")]
    public async Task CloseTabWithConfirmAsync_NotDirty_ClosesWithoutCallingConfirm()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "hello");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus     = new EventBus();
            var output  = new OutputService();
            var active  = new ActiveDocumentService();
            var sut     = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(filePath));
            Assert.Single(sut.Tabs);
            var tab = sut.Tabs[0];
            Assert.False(tab.IsDirty);

            var confirmCalled = false;
            sut.ConfirmCloseAsync = _ =>
            {
                confirmCalled = true;
                return Task.FromResult(UnsavedChangesResult.Cancel);
            };

            await sut.CloseTabWithConfirmAsync(tab);

            Assert.False(confirmCalled, "Nie powinno pytać gdy plik jest czysty.");
            Assert.Empty(sut.Tabs);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "105")]
    public async Task CloseTabWithConfirmAsync_Dirty_ConfirmSave_SavesThenCloses()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "hello");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus     = new EventBus();
            var output  = new OutputService();
            var active  = new ActiveDocumentService();
            var sut     = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(filePath));
            var tab = sut.Tabs[0];
            tab.Content = "modified";
            Assert.True(tab.IsDirty);

            sut.ConfirmCloseAsync = _ => Task.FromResult(UnsavedChangesResult.Save);

            await sut.CloseTabWithConfirmAsync(tab);

            Assert.Empty(sut.Tabs);
            Assert.Equal("modified", File.ReadAllText(filePath));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "105")]
    public async Task CloseTabWithConfirmAsync_Dirty_ConfirmCancel_TabRemainsOpen()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "hello");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus     = new EventBus();
            var output  = new OutputService();
            var active  = new ActiveDocumentService();
            var sut     = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(filePath));
            var tab = sut.Tabs[0];
            tab.Content = "modified";

            sut.ConfirmCloseAsync = _ => Task.FromResult(UnsavedChangesResult.Cancel);

            await sut.CloseTabWithConfirmAsync(tab);

            Assert.Single(sut.Tabs);
            Assert.True(tab.IsDirty, "Tab powinien być nadal dirty.");
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "105")]
    public async Task CloseTabWithConfirmAsync_Dirty_ConfirmDiscard_ClosesWithoutSaving()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "hello");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus     = new EventBus();
            var output  = new OutputService();
            var active  = new ActiveDocumentService();
            var sut     = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(filePath));
            var tab = sut.Tabs[0];
            tab.Content = "modified";
            Assert.True(tab.IsDirty);

            sut.ConfirmCloseAsync = _ => Task.FromResult(UnsavedChangesResult.Discard);

            await sut.CloseTabWithConfirmAsync(tab);

            Assert.Empty(sut.Tabs);
            Assert.Equal("hello", File.ReadAllText(filePath), ignoreWhiteSpaceDifferences: false);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "105")]
    public async Task CloseTabWithConfirmAsync_Dirty_NoConfirmDelegate_ClosesDirectly()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "hello");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus     = new EventBus();
            var output  = new OutputService();
            var active  = new ActiveDocumentService();
            var sut     = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(filePath));
            var tab = sut.Tabs[0];
            tab.Content = "modified";
            Assert.True(tab.IsDirty);

            // ConfirmCloseAsync NIE jest podłączone
            Assert.Null(sut.ConfirmCloseAsync);

            await sut.CloseTabWithConfirmAsync(tab);

            Assert.Empty(sut.Tabs);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    // -----------------------------------------------------------------------
    // Issue #107 – Status bar
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Issue", "107")]
    public void StatusText_NoActiveTab_ReturnsEmpty()
    {
        var workspace = new WorkspaceService();
        var bus       = new EventBus();
        var output    = new OutputService();
        var active    = new ActiveDocumentService();
        var sut       = new EditorPanelViewModel(workspace, bus, output, active);

        Assert.Equal(string.Empty, sut.StatusText);
    }

    [Fact]
    [Trait("Issue", "107")]
    public void StatusText_CleanTab_ShowsFileNameAndDefaultCaret()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "hello.txt");
            File.WriteAllText(filePath, "content");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus    = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();
            var sut    = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(filePath));
            var tab = sut.Tabs[0];
            Assert.False(tab.IsDirty);
            Assert.Equal(1, tab.CaretLine);
            Assert.Equal(1, tab.CaretColumn);

            Assert.Equal("hello.txt  Ln 1, Col 1", sut.StatusText);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "107")]
    public void StatusText_DirtyTab_ShowsBulletPrefix()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "doc.cs");
            File.WriteAllText(filePath, "original");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus    = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();
            var sut    = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(filePath));
            var tab = sut.Tabs[0];
            tab.Content = "modified";
            Assert.True(tab.IsDirty);

            Assert.StartsWith("● ", sut.StatusText);
            Assert.Contains("doc.cs", sut.StatusText);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "107")]
    public void StatusText_UpdatesWhenCaretChanges()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var filePath = Path.Combine(tmp.FullName, "a.txt");
            File.WriteAllText(filePath, "line1\nline2\n");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus    = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();
            var sut    = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(filePath));
            var tab = sut.Tabs[0];

            string? notified = null;
            sut.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(EditorPanelViewModel.StatusText))
                    notified = sut.StatusText;
            };

            tab.CaretLine   = 2;
            tab.CaretColumn = 5;

            Assert.Equal(2, tab.CaretLine);
            Assert.Equal(5, tab.CaretColumn);
            Assert.NotNull(notified);
            Assert.Contains("Ln 2, Col 5", sut.StatusText);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Issue", "107")]
    public void StatusText_UpdatesWhenTabBecomesActiveOrInactive()
    {
        var tmp = Directory.CreateTempSubdirectory("junexis_studio_tests_");
        try
        {
            var file1 = Path.Combine(tmp.FullName, "a.txt");
            var file2 = Path.Combine(tmp.FullName, "b.txt");
            File.WriteAllText(file1, "aaa");
            File.WriteAllText(file2, "bbb");

            var workspace = new WorkspaceService();
            workspace.OpenFolder(tmp.FullName);

            var bus    = new EventBus();
            var output = new OutputService();
            var active = new ActiveDocumentService();
            var sut    = new EditorPanelViewModel(workspace, bus, output, active);

            bus.Publish(new FileOpenRequested(file1));
            bus.Publish(new FileOpenRequested(file2));

            sut.ActiveTab = sut.Tabs[0];
            Assert.Contains("a.txt", sut.StatusText);

            sut.ActiveTab = sut.Tabs[1];
            Assert.Contains("b.txt", sut.StatusText);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { }
        }
    }
}
