using Avalonia.Controls;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Shell;

public partial class HelpCenterView : UserControl
{
    public HelpCenterView()
    {
        InitializeComponent();
    }

    private void OnCommandClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string commandId })
            return;
        var context = TopLevel.GetTopLevel(this)?.DataContext as IHostContext;
        if (commandId == "help.openOutput")
        {
            context?.EventBus.Publish(new ActivatePanelRequested("junexis.output.filtered"));
            return;
        }
        if (context?.Commands.CanExecute(commandId) == true)
            context.Commands.Execute(commandId);
    }
}
