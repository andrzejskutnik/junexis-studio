using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;
using System;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>Displays the latest Agent session summary.</summary>
public partial class AgentReportPanelView : UserControl
{
    private AgentReportPanelViewModel? _viewModel;

    /// <summary>Initializes the Agent report view.</summary>
    public AgentReportPanelView() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.ReportChanged -= OnReportChanged;
        }

        _viewModel = DataContext as AgentReportPanelViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ReportChanged += OnReportChanged;
        }
    }

    private void OnReportChanged() => Dispatcher.UIThread.Post(InvalidateVisual);

    private void OnChangedFileClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not Button { DataContext: AgentFileItem item })
        {
            return;
        }

        _viewModel.OpenChangedFile(item.Path);
    }
}