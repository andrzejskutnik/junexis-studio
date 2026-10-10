using Junexis.Studio.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Junexis.Studio.Host;

/// <summary>
/// Rozszerzenie Explorer: rejestruje panel narzędziowy "explorer" i komendy menu kontekstowego,
/// w tym operacje na plikach (New File, Delete, Refresh).
/// </summary>
internal sealed class ExplorerExtension : IJunexisExtension
{
    private ExplorerPanelViewModel? _vm;

    /// <inheritdoc />
    public string Id => "junexis.explorer";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.Panels.RegisterToolPanel(
            "explorer",
            () =>
            {
                context.Capabilities.TryGet<IVcsService>(out var vcs);
                _vm = new ExplorerPanelViewModel(
                    context.Workspace,
                    context.EventBus,
                    context.Output,
                    versionControl: null,
                    vcs: vcs,
                    recentFolders: new Junexis.Studio.Core.WorkspaceHistoryStore().LoadRecentFolders());
                return _vm;
            });

        context.Commands.Register(
            id: "explorer.revealInFileManager",
            execute: () => RevealWorkspaceRoot(context),
            menuPath: "File/Reveal in File Manager",
            displayName: "Reveal in File Manager",
            category: "Explorer",
            contextTarget: "explorer");

        context.Commands.Register(
            id: "explorer.copyPath",
            execute: () => CopyWorkspaceRootPath(context),
            menuPath: "File/Copy Workspace Path",
            displayName: "Copy Workspace Path",
            category: "Explorer",
            contextTarget: "explorer");

        context.Commands.Register(
            id: "explorer.newFile",
            execute: () =>
            {
                if (_vm is null)
                {
                    context.Output.Write("Explorer", "New File: panel nie jest jeszcze otwarty.");
                    return;
                }

                _ = CreateNewFileAsync(context);
            },
            menuPath: "File/New File in Workspace...",
            displayName: "New File in Workspace...",
            category: "Explorer",
            contextTarget: "explorer");

        context.Commands.Register(
            id: "explorer.delete",
            execute: () =>
            {
                if (_vm is null)
                {
                    context.Output.Write("Explorer", "Delete: panel nie jest jeszcze otwarty.");
                    return;
                }

                var node = _vm.SelectedNode;
                if (node is null)
                {
                    context.Output.Write("Explorer", "Delete: brak wybranego węzła.");
                    return;
                }

                _ = DeleteSelectedNodeAsync(context, node.Name);
            },
            menuPath: "File/Delete Selected Item",
            displayName: "Delete Selected Item",
            category: "Explorer",
            contextTarget: "explorer");

        context.Commands.Register(
            id: "explorer.refresh",
            execute: () =>
            {
                if (_vm is null)
                {
                    context.Output.Write("Explorer", "Refresh: panel nie jest jeszcze otwarty.");
                    return;
                }

                _vm.Refresh();
            },
            menuPath: "Workspace/Refresh Explorer",
            displayName: "Refresh",
            category: "Explorer",
            contextTarget: "explorer");

        context.Commands.Register(
            id: "explorer.rename",
            execute: () =>
            {
                if (_vm is null)
                {
                    context.Output.Write("Explorer", "Rename: panel nie jest jeszcze otwarty.");
                    return;
                }

                var node = _vm.SelectedNode;
                if (node is null)
                {
                    context.Output.Write("Explorer", "Rename: brak wybranego węzła.");
                    return;
                }

                _ = RenameSelectedNodeAsync(context, node.Name);
            },
            menuPath: "File/Rename Selected Item...",
            displayName: "Rename Selected Item...",
            category: "Explorer",
            contextTarget: "explorer");

        context.Commands.Register(
            id: "explorer.newFolder",
            execute: () =>
            {
                if (_vm is null)
                {
                    context.Output.Write("Explorer", "New Folder: panel nie jest jeszcze otwarty.");
                    return;
                }

                _ = CreateNewFolderAsync(context);
            },
            menuPath: "File/New Folder in Workspace...",
            displayName: "New Folder in Workspace...",
            category: "Explorer",
            contextTarget: "explorer");

        context.Commands.Register(
            id: "explorer.move",
            execute: () =>
            {
                if (_vm is null)
                {
                    context.Output.Write("Explorer", "Move: panel nie jest jeszcze otwarty.");
                    return;
                }

                var node = _vm.SelectedNode;
                if (node is null)
                {
                    context.Output.Write("Explorer", "Move: brak wybranego węzła.");
                    return;
                }

                _ = MoveSelectedNodeAsync(context, node.Name);
            },
            menuPath: "File/Move Selected Item to...",
            displayName: "Move Selected Item to...",
            category: "Explorer",
            contextTarget: "explorer");

        context.Output.Write("Extensions", "ExplorerExtension loaded");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _vm?.Dispose();
        _vm = null;
    }

    private static void RevealWorkspaceRoot(IHostContext context)
    {
        var root = context.Workspace.RootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            context.Output.Write("Explorer", "No workspace folder to reveal.");
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", root);
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", root);
            }
            else
            {
                System.Diagnostics.Process.Start("xdg-open", root);
            }
        }
        catch (Exception ex)
        {
            context.Output.Write("Explorer", $"Reveal failed: {ex.Message}");
        }
    }

    private static void CopyWorkspaceRootPath(IHostContext context)
    {
        var root = context.Workspace.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            context.Output.Write("Explorer", "No workspace path to copy.");
            return;
        }

        context.Output.Write("Explorer", $"Path copied: {root}");
    }

    private async Task RenameSelectedNodeAsync(IHostContext context, string currentName)
    {
        if (_vm is null)
        {
            return;
        }

        if (!context.Capabilities.TryGet<IWorkspaceInteractionService>(out var interaction) || interaction is null)
        {
            context.Output.Write("Explorer", "Rename: brak serwisu interakcji UI.", OutputSeverity.Warning);
            return;
        }

        var newName = await interaction.PromptForTextAsync("Rename", "New name", currentName);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        _vm.RenameSelectedNode(newName.Trim());
    }

    private async Task CreateNewFileAsync(IHostContext context)
    {
        if (_vm is null)
        {
            return;
        }

        string? fileName = null;
        if (context.Capabilities.TryGet<IWorkspaceInteractionService>(out var interaction) && interaction is not null)
        {
            fileName = await interaction.PromptForTextAsync("New File", "File name", "NewFile.txt");
            if (fileName is null)
            {
                return;
            }

            fileName = fileName.Trim();
            if (fileName.Length == 0)
            {
                return;
            }
        }

        _vm.NewFileInSelection(fileName);
    }

    private async Task CreateNewFolderAsync(IHostContext context)
    {
        if (_vm is null)
        {
            return;
        }

        string? folderName = null;
        if (context.Capabilities.TryGet<IWorkspaceInteractionService>(out var interaction) && interaction is not null)
        {
            folderName = await interaction.PromptForTextAsync("New Folder", "Folder name", "NewFolder");
            if (folderName is null)
            {
                return;
            }

            folderName = folderName.Trim();
            if (folderName.Length == 0)
            {
                return;
            }
        }

        _vm.NewFolderInSelection(folderName);
    }

    private async Task MoveSelectedNodeAsync(IHostContext context, string currentName)
    {
        if (_vm is null)
        {
            return;
        }

        if (!context.Capabilities.TryGet<IWorkspaceInteractionService>(out var interaction) || interaction is null)
        {
            context.Output.Write("Explorer", "Move: brak serwisu interakcji UI.", OutputSeverity.Warning);
            return;
        }

        var destinationDir = await interaction.PickFolderAsync($"Move '{currentName}' to...");
        if (string.IsNullOrWhiteSpace(destinationDir))
        {
            return;
        }

        _vm.MoveSelectedNode(destinationDir);
    }

    private async Task DeleteSelectedNodeAsync(IHostContext context, string name)
    {
        if (_vm is null)
            return;

        if (context.Capabilities.TryGet<IWorkspaceInteractionService>(out var interaction) && interaction is not null)
        {
            var confirmed = await interaction.ConfirmAsync("Delete", $"Delete '{name}'?");
            if (!confirmed)
                return;
        }

        _vm.DeleteSelectedNode();
    }
}
