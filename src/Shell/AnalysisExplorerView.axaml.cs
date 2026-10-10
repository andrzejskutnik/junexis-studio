using Avalonia.Controls;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>Displays compiler symbols, communications, and topology for the latest build.</summary>
public partial class AnalysisExplorerView : UserControl
{
    /// <summary>Initializes the Analysis panel view.</summary>
    public AnalysisExplorerView()
    {
        InitializeComponent();
    }

    private void OnSymbolDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is AnalysisExplorerViewModel viewModel && SymbolsList.SelectedItem is AnalysisSymbol symbol)
            viewModel.NavigateTo(symbol);
    }

    private void OnRelationDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is AnalysisExplorerViewModel viewModel && RelationsList.SelectedItem is AnalysisRelation relation)
            viewModel.NavigateTo(relation);
    }

    private void OnFindingDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is AnalysisExplorerViewModel viewModel && FindingsList.SelectedItem is MentorFinding finding)
            viewModel.NavigateTo(finding);
    }

    private void OnPreviewRefactoringClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AnalysisExplorerViewModel viewModel)
            viewModel.PreviewSelectedRefactoring();
    }

    private async void OnApplyRefactoringClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AnalysisExplorerViewModel viewModel)
            await viewModel.ApplyRefactoringAsync();
    }

    private async void OnValidateRefactoringClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AnalysisExplorerViewModel viewModel)
            await viewModel.ValidateRefactoringAsync();
    }

    private void OnKeepRefactoringClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AnalysisExplorerViewModel viewModel)
            viewModel.KeepRefactoring();
    }

    private async void OnRevertRefactoringClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AnalysisExplorerViewModel viewModel)
            await viewModel.RevertRefactoringAsync();
    }
}
