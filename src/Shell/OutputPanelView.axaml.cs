using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Threading;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Junexis.Studio.Shell;

/// <summary>
/// Widok panelu Output z filtrowaniem kanału i severity.
/// Subskrybuje <see cref="OutputPanelViewModel.EntriesChanged"/> i odświeża
/// listę oraz dostępne kanały na wątku UI.
/// </summary>
public partial class OutputPanelView : UserControl
{
    private OutputPanelViewModel? _vm;
    private readonly ObservableCollection<OutputEntry> _entries = new();
    private bool _updatingCombo;

    private const string AllChannels = "(wszystkie)";
    private const string AllSeverities = "(wszystkie)";

    /// <summary>Tworzy widok i inicjalizuje listę i ComboBox severity.</summary>
    public OutputPanelView()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _entries;
        InitSeverityCombo();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
        {
            _vm.EntriesChanged -= OnEntriesChanged;
        }

        _vm = DataContext as OutputPanelViewModel;

        if (_vm is not null)
        {
            _vm.EntriesChanged += OnEntriesChanged;
            RefreshAll();
        }
    }

    private void InitSeverityCombo()
    {
        SeverityFilterBox.Items.Add(AllSeverities);
        SeverityFilterBox.Items.Add(OutputSeverity.Info.ToString());
        SeverityFilterBox.Items.Add(OutputSeverity.Warning.ToString());
        SeverityFilterBox.Items.Add(OutputSeverity.Error.ToString());
        SeverityFilterBox.SelectedIndex = 0;
    }

    private void OnEntriesChanged()
    {
        Dispatcher.UIThread.Post(RefreshAll);
    }

    private void RefreshAll()
    {
        if (_vm is null)
        {
            return;
        }

        // Odśwież kanały (bez wyzwalania filtra).
        var previousChannel = ChannelFilterBox.SelectedItem as string;
        _updatingCombo = true;

        ChannelFilterBox.Items.Clear();
        ChannelFilterBox.Items.Add(AllChannels);

        foreach (var ch in _vm.AvailableChannels)
        {
            ChannelFilterBox.Items.Add(ch);
        }

        var idx = ChannelFilterBox.Items.IndexOf(previousChannel);
        ChannelFilterBox.SelectedIndex = idx >= 0 ? idx : 0;

        _updatingCombo = false;

        // Odśwież wpisy.
        _entries.Clear();

        foreach (var entry in _vm.FilteredEntries)
        {
            _entries.Add(entry);
        }

        // Auto-scroll do ostatniego wpisu.
        if (_entries.Count > 0)
        {
            EntriesList.ScrollIntoView(_entries[_entries.Count - 1]);
        }
    }

    private void OnChannelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingCombo || _vm is null)
        {
            return;
        }

        var selected = ChannelFilterBox.SelectedItem as string;
        _vm.SetChannelFilter(string.Equals(selected, AllChannels, StringComparison.Ordinal) ? null : selected);

        RefreshEntries();
    }

    private void OnSeverityChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var selected = SeverityFilterBox.SelectedItem as string;

        OutputSeverity? severity = selected switch
        {
            var s when s == OutputSeverity.Info.ToString() => OutputSeverity.Info,
            var s when s == OutputSeverity.Warning.ToString() => OutputSeverity.Warning,
            var s when s == OutputSeverity.Error.ToString() => OutputSeverity.Error,
            _ => null,
        };

        _vm.SetSeverityFilter(severity);
        RefreshEntries();
    }

    private void OnClearClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm?.Clear();
        RefreshEntries();
    }

    private void OnCopyLineClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: OutputEntry entry })
        {
            return;
        }

        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(FormatEntry(entry));
    }

    private void OnCopyAllClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine, _entries.Select(FormatEntry));
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
    }

    private static string FormatEntry(OutputEntry entry) =>
        $"{entry.Timestamp:HH:mm:ss}\t{entry.Channel}\t{entry.Severity}\t{entry.Message}";

    private void RefreshEntries()
    {
        if (_vm is null)
        {
            return;
        }

        _entries.Clear();

        foreach (var entry in _vm.FilteredEntries)
        {
            _entries.Add(entry);
        }

        if (_entries.Count > 0)
        {
            EntriesList.ScrollIntoView(_entries[_entries.Count - 1]);
        }
    }
}
