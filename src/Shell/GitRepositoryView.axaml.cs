using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>Code-behind for explicit repository operations.</summary>
public partial class GitRepositoryView : UserControl
{
    /// <summary>Initializes the repository view.</summary>
    public GitRepositoryView()
    {
        InitializeComponent();
    }

    private GitRepositoryViewModel? Vm => DataContext as GitRepositoryViewModel;

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.RefreshAsync();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Vm?.Cancel();

    private async void OnLoadDetails(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.LoadCommitDetailsAsync();
    }

    private async void OnCommitDoubleTap(object? sender, TappedEventArgs e)
    {
        if (Vm is { } vm) await vm.LoadCommitDetailsAsync();
    }

    private async void OnCreateBranch(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.CreateBranchAsync();
    }

    private async void OnSwitchBranch(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.SwitchBranchAsync();
    }

    private async void OnCompareBranches(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.CompareBranchesAsync();
    }

    private async void OnCreateStash(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.CreateStashAsync();
    }

    private async void OnApplyStash(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.ApplyStashAsync();
    }

    private async void OnPopStash(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.PopStashAsync();
    }

    private async void OnFetch(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.FetchAsync();
    }

    private async void OnPull(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.PullAsync();
    }

    private async void OnPush(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.PushAsync();
    }
}
