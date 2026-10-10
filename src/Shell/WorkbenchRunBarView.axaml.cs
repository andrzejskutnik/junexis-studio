using Avalonia.Controls;
using Avalonia.Interactivity;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>Compact launch-profile toolbar shared by build, run and debug workflows.</summary>
public partial class WorkbenchRunBarView : UserControl
{
    /// <summary>Initializes the workbench run bar.</summary>
    public WorkbenchRunBarView()
    {
        InitializeComponent();
    }

    private async void Build_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchRunViewModel viewModel)
            await viewModel.BuildAsync();
    }

    private async void Run_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchRunViewModel viewModel)
            await viewModel.RunAsync();
    }

    private async void Debug_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkbenchRunViewModel viewModel)
            await viewModel.DebugAsync();
    }

    private void Stop_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel?.Stop();

    private void Problems_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel?.OpenProblems();

    private void Output_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel?.OpenOutput();

    private void Why_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel?.OpenWhy();

    private WorkbenchRunViewModel? ViewModel => DataContext as WorkbenchRunViewModel;
}
