using Junexis.Studio.Abstractions;
using Junexis.Studio.Core;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

/// <summary>
/// Tests for <see cref="DocumentCommandService"/>.
/// </summary>
[Trait("Issue", "103")]
public sealed class DocumentCommandServiceTests
{
    private static (DocumentCommandService service, EditorPanelViewModel editor, OutputService output, EventBus eventBus, CapabilityRegistry capabilities, WorkspaceService workspace) Create()
    {
        var workspace = new WorkspaceService();
        var eventBus = new EventBus();
        var output = new OutputService();
        var activeDocument = new ActiveDocumentService();
        var capabilities = new CapabilityRegistry();
        var editor = new EditorPanelViewModel(workspace, eventBus, output, activeDocument);
        var service = new DocumentCommandService(editor, activeDocument, capabilities, output);
        return (service, editor, output, eventBus, capabilities, workspace);
    }

    [Fact]
    public void SaveActiveDocument_WithoutActiveTab_LogsMessage()
    {
        var (service, _, output, _, _, _) = Create();

        service.SaveActiveDocument();

        Assert.Contains(output.GetSnapshot(), e => e.Channel == "Editor" && e.Message.Contains("No active file"));
    }

    [Fact]
    public void CloseActiveTab_WithoutActiveTab_LogsMessage()
    {
        var (service, _, output, _, _, _) = Create();

        service.CloseActiveTab();

        Assert.Contains(output.GetSnapshot(), e => e.Channel == "Editor" && e.Message.Contains("No active tab"));
    }

    [Fact]
    public void CopyActivePath_WithoutActiveDocument_LogsMessage()
    {
        var (service, _, output, _, _, _) = Create();

        service.CopyActivePath();

        Assert.Contains(output.GetSnapshot(), e => e.Channel == "Editor" && e.Message.Contains("No active file"));
    }

    [Fact]
    public void ClipboardCommands_WithoutClipboardCapability_DoNotThrow()
    {
        var (service, _, _, _, _, _) = Create();

        var ex = Record.Exception(() =>
        {
            service.Undo();
            service.Redo();
            service.Cut();
            service.Copy();
            service.Paste();
            service.SelectAll();
        });

        Assert.Null(ex);
    }

    [Fact]
    public async Task GoToLineAsync_WithLineOnlyInput_NavigatesToRequestedLine()
    {
        var (service, editor, _, eventBus, capabilities, workspace) = Create();
        var root = Directory.CreateTempSubdirectory();

        try
        {
            workspace.OpenFolder(root.FullName);
            var path = Path.Combine(root.FullName, "main.jnx");
            workspace.WriteFile(path, "line1\nline2\nline3\nline4");
            eventBus.Publish(new FileOpenRequested(path));

            capabilities.Register<IWorkspaceInteractionService>(new StubWorkspaceInteractionService("3"));

            await service.GoToLineAsync();

            Assert.NotNull(editor.ActiveTab);
            Assert.Equal(3, editor.ActiveTab!.RequestedLine);
            Assert.Equal(1, editor.ActiveTab.RequestedColumn);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GoToLineAsync_WithLineAndColumnInput_NavigatesToRequestedLocation()
    {
        var (service, editor, _, eventBus, capabilities, workspace) = Create();
        var root = Directory.CreateTempSubdirectory();

        try
        {
            workspace.OpenFolder(root.FullName);
            var path = Path.Combine(root.FullName, "main.jnx");
            workspace.WriteFile(path, "line1\nline2\nline3\nline4");
            eventBus.Publish(new FileOpenRequested(path));

            capabilities.Register<IWorkspaceInteractionService>(new StubWorkspaceInteractionService("3:7"));

            await service.GoToLineAsync();

            Assert.NotNull(editor.ActiveTab);
            Assert.Equal(3, editor.ActiveTab!.RequestedLine);
            Assert.Equal(7, editor.ActiveTab.RequestedColumn);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class StubWorkspaceInteractionService : IWorkspaceInteractionService
    {
        private readonly string? _response;

        public StubWorkspaceInteractionService(string? response)
        {
            _response = response;
        }

        public Task<string?> PromptForTextAsync(string title, string message, string initialValue)
        {
            return Task.FromResult(_response);
        }

        public Task<string?> PickFolderAsync(string title)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ConfirmAsync(string title, string message)
        {
            return Task.FromResult(true);
        }
    }
}