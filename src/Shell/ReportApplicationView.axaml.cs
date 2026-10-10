using Avalonia.Controls;
using Avalonia.Interactivity;
using JunexisLang.Studio.CompilerExtension;

namespace Junexis.Studio.Shell;

/// <summary>Panel sterowania lokalną aplikacją Junexis i jej widokiem <c>@report</c>.</summary>
public partial class ReportApplicationView : UserControl
{
    /// <summary>Inicjalizuje panel aplikacji.</summary>
    public ReportApplicationView()
    {
        InitializeComponent();
    }

    private async void Start_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReportApplicationViewModel viewModel)
            await viewModel.StartAsync();
    }

    private void Open_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReportApplicationViewModel viewModel)
            viewModel.Open();
    }

    private void Stop_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ReportApplicationViewModel viewModel)
            viewModel.Stop();
    }
}
