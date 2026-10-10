using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>Widok globalnego wyszukiwania workspace.</summary>
public partial class WorkspaceSearchView : UserControl
{
    public WorkspaceSearchView()
    {
        InitializeComponent();
    }

    public void FocusQuery()
    {
        QueryInput.Focus();
        QueryInput.SelectAll();
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e) => await SearchAsync();

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceSearchViewModel viewModel)
            viewModel.Cancel();
    }

    private async void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SearchAsync();
        }
        else if (e.Key == Key.Escape && DataContext is WorkspaceSearchViewModel viewModel)
        {
            viewModel.Cancel();
            e.Handled = true;
        }
    }

    private void OnResultClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkspaceSearchMatch match }
            && DataContext is WorkspaceSearchViewModel viewModel)
        {
            viewModel.Navigate(match);
        }
    }

    private async Task SearchAsync()
    {
        if (DataContext is WorkspaceSearchViewModel viewModel)
            await viewModel.SearchAsync();
    }
}
