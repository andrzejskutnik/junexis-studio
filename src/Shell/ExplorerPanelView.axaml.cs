using Avalonia.Controls;
using Avalonia.Input;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using System;

namespace Junexis.Studio.Shell;

/// <summary>
/// Widok panelu Explorer (TreeView).
/// </summary>
public partial class ExplorerPanelView : UserControl
{
    private const string OpenFolderCommandId = "workspace.openFolder";
    private const string CloneRepositoryCommandId = "workspace.cloneRepository";

    /// <summary>
    /// Tworzy widok i ładuje XAML.
    /// </summary>
    public ExplorerPanelView()
    {
        InitializeComponent();
    }

    private void OnOpenFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is IHostContext context
            && context.Commands.CanExecute(OpenFolderCommandId))
        {
            context.Commands.Execute(OpenFolderCommandId);
        }
    }

    private void OnCloneRepositoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is IHostContext context
            && context.Commands.CanExecute(CloneRepositoryCommandId))
        {
            context.Commands.Execute(CloneRepositoryCommandId);
        }
    }

    private async void OnOpenRecentClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folderPath }
            && TopLevel.GetTopLevel(this) is MainWindow window)
        {
            await window.OpenWorkspaceFolderAsync(folderPath);
        }
    }

    // Single click → preview open (replaces existing preview tab).
    private void OnTreeTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ExplorerPanelViewModel vm) return;
        var node = ResolveNode(e.Source) ?? (Tree.SelectedItem as ExplorerNode);
        if (node is null || node.IsDirectory) return;
        try { vm.RequestPreview(node); }
        catch (Exception) { }
    }

    // Double click → permanent open.
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ExplorerPanelViewModel vm) return;
        var node = ResolveNode(e.Source) ?? (Tree.SelectedItem as ExplorerNode);
        if (node is null) return;
        try { vm.RequestOpen(node); }
        catch (Exception) { }
    }

    // Enter key → permanent open, F2 → rename, Delete → delete selected node.
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ExplorerPanelViewModel vm) return;
        if (Tree.SelectedItem is ExplorerNode node)
        {
            if (e.Key == Key.Enter)
            {
                try { vm.RequestOpen(node); }
                catch (Exception) { }
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                if (TopLevel.GetTopLevel(this)?.DataContext is IHostContext ctx
                    && ctx.Commands.CanExecute("explorer.rename"))
                    ctx.Commands.Execute("explorer.rename");
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                if (TopLevel.GetTopLevel(this)?.DataContext is IHostContext ctx
                    && ctx.Commands.CanExecute("explorer.delete"))
                    ctx.Commands.Execute("explorer.delete");
                e.Handled = true;
            }
        }
    }

    private static ExplorerNode? ResolveNode(object? source)
    {
        // Szuka węzła ExplorerNode w DataContext źródła lub jego przodkach wizualnych.
        var control = source as Avalonia.Controls.Control;
        while (control is not null)
        {
            if (control.DataContext is ExplorerNode node)
            {
                return node;
            }

            control = control.Parent as Avalonia.Controls.Control;
        }

        return null;
    }
}
