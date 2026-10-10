using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>
/// Code-behind for <see cref="VcsChangesView"/>.
/// Handles toolbar actions and double-click stage/unstage.
/// </summary>
public partial class VcsChangesView : UserControl
{
    /// <summary>Initializes the VcsChangesView.</summary>
    public VcsChangesView()
    {
        InitializeComponent();
    }

    private VcsChangesViewModel? Vm => DataContext as VcsChangesViewModel;

    private async void OnStageAll(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } viewModel)
            await viewModel.StageAllAsync();
    }

    private async void OnUnstageAll(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } viewModel)
            await viewModel.UnstageAllAsync();
    }

    private async void OnStageSelected(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } viewModel)
            return;
        var selected = (ChangesList.SelectedItems
                ?? (System.Collections.IList)Array.Empty<object>())
            .OfType<VcsFileChange>()
            .Concat((UntrackedList.SelectedItems
                ?? (System.Collections.IList)Array.Empty<object>())
                .OfType<VcsFileChange>())
            .ToArray();
        await viewModel.StageSelectedAsync(selected);
    }

    private async void OnUnstageSelected(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } viewModel)
            await viewModel.UnstageSelectedAsync(
                (StagedList.SelectedItems
                    ?? (System.Collections.IList)Array.Empty<object>())
                .OfType<VcsFileChange>()
                .ToArray());
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } viewModel)
            await viewModel.RefreshAsync();
    }
    private async void OnCommit(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } viewModel)
            await viewModel.CommitAsync();
    }

    private void OnCancelCommit(object? sender, RoutedEventArgs e) =>
        Vm?.CancelCommit();

    private void OnStagedDoubleTap(object? sender, TappedEventArgs e)
    {
        if (StagedList.SelectedItem is VcsFileChange change)
            _ = Vm?.UnstageAsync(change.Path);
    }

    private void OnChangeDoubleTap(object? sender, TappedEventArgs e)
    {
        if (ChangesList.SelectedItem is VcsFileChange change)
            _ = Vm?.StageAsync(change.Path);
    }

    private void OnUntrackedDoubleTap(object? sender, TappedEventArgs e)
    {
        if (UntrackedList.SelectedItem is VcsFileChange change)
            _ = Vm?.StageAsync(change.Path);
    }

    private void OnConflictDoubleTap(object? sender, TappedEventArgs e)
    {
        if (ConflictsList.SelectedItem is VcsFileChange change)
            _ = Vm?.ShowDiffAsync(change, DiffTarget.WorkingTreeVsHead);
    }

    private async void OnDiffStaged(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } viewModel && StagedList.SelectedItem is VcsFileChange change)
            await viewModel.ShowDiffAsync(change, DiffTarget.IndexVsHead);
    }

    private async void OnDiffChange(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } viewModel && ChangesList.SelectedItem is VcsFileChange change)
            await viewModel.ShowDiffAsync(change, DiffTarget.WorkingTreeVsIndex);
    }
}
