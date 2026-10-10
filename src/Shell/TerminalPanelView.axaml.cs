using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using System;
using System.Collections.ObjectModel;

namespace Junexis.Studio.Shell;

/// <summary>
/// Widok panelu terminala.
/// Wyświetla linie stdout/stderr w czasie rzeczywistym i umożliwia uruchamianie komend.
/// </summary>
public partial class TerminalPanelView : UserControl
{
    private TerminalPanelViewModel? _vm;
    private readonly ObservableCollection<ProcessOutputLine> _displayLines = new();
    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    /// <summary>
    /// Tworzy widok i rejestruje handlery UI.
    /// </summary>
    public TerminalPanelView()
    {
        InitializeComponent();
        OutputLines.ItemsSource = _displayLines;
        RunButton.Click += OnRunClick;
        CancelButton.Click += OnCancelClick;
        CommandInput.KeyDown += OnCommandInputKeyDown;
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.OutputReceived -= OnOutputReceived;
            _vm.ProcessFinished -= OnProcessFinished;
        }

        base.OnDataContextChanged(e);

        _vm = DataContext as TerminalPanelViewModel;
        _displayLines.Clear();

        if (_vm is not null)
        {
            // Populate from already-collected lines (re-attach scenario).
            foreach (var line in _vm.Lines)
            {
                _displayLines.Add(line);
            }

            _vm.OutputReceived += OnOutputReceived;
            _vm.ProcessFinished += OnProcessFinished;
        }
    }

    private void OnOutputReceived(ProcessOutputLine line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _displayLines.Add(line);
            ScrollToBottom();
        }, DispatcherPriority.Input);
    }

    private void OnProcessFinished(ProcessResult result)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _displayLines.Add(new ProcessOutputLine(
                ProcessOutputKind.StdOut,
                $"[process exited: {result.ExitCode}]"));
            ScrollToBottom();
        }, DispatcherPriority.Input);
    }

    private void ScrollToBottom()
    {
        Dispatcher.UIThread.Post(() =>
        {
            OutputScroll.Offset = new Vector(0, double.PositiveInfinity);
        }, DispatcherPriority.Loaded);
    }

    private void OnRunClick(object? sender, RoutedEventArgs e)
    {
        ExecuteCommand();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _vm?.Cancel();
    }

    private void OnCommandInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
        {
            ExecuteCommand();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
        {
            NavigateHistory(+1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
        {
            NavigateHistory(-1);
            e.Handled = true;
        }
        // Shift+Enter passes through to TextBox (inserts newline via AcceptsReturn).
    }

    private void NavigateHistory(int direction)
    {
        if (_history.Count == 0)
            return;

        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count - 1);
        CommandInput.Text = _history[_history.Count - 1 - _historyIndex];
        CommandInput.CaretIndex = CommandInput.Text?.Length ?? 0;
    }

    private void ExecuteCommand()
    {
        if (_vm is null)
        {
            return;
        }

        var command = CommandInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        CommandInput.Text = string.Empty;
        _history.Add(command);
        _historyIndex = -1;

        // Echo the command in display.
        _displayLines.Add(new ProcessOutputLine(ProcessOutputKind.StdOut, $"❯ {command}"));

        _ = RunAndCatchAsync(command);
    }

    private async Task RunAndCatchAsync(string command)
    {
        try
        {
            await _vm!.RunCommandLineAsync(command);
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _displayLines.Add(new ProcessOutputLine(ProcessOutputKind.StdErr, $"[error: {ex.Message}]"));
                ScrollToBottom();
            });
        }
    }
}
