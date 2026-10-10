using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Junexis.Studio.Host;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Junexis.Studio.Shell;

/// <summary>
/// Wspólne okno palety poleceń i szybkiego otwierania plików.
/// </summary>
public partial class QuickAccessWindow : Window
{
    private readonly TaskCompletionSource<QuickAccessItem?> _result = new();
    private IReadOnlyList<QuickAccessItem> _items = Array.Empty<QuickAccessItem>();

    public QuickAccessWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.Icon;
        Opened += (_, _) => QueryInput.Focus();
    }

    /// <summary>
    /// Pokazuje paletę i zwraca wybraną pozycję albo null po anulowaniu.
    /// </summary>
    public static async Task<QuickAccessItem?> ShowAndGetResultAsync(
        Window owner,
        string title,
        string placeholder,
        IReadOnlyList<QuickAccessItem> items)
    {
        var window = new QuickAccessWindow
        {
            Title = title,
            _items = items,
        };
        window.QueryInput.Watermark = placeholder;
        window.RefreshResults();

        await window.ShowDialog(owner);
        return await window._result.Task;
    }

    private void OnQueryChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshResults();
    }

    private void RefreshResults()
    {
        var matches = QuickAccessCatalog.Search(_items, QueryInput.Text);
        ResultsList.ItemsSource = matches;
        ResultsList.SelectedIndex = matches.Count > 0 ? 0 : -1;
        ResultCount.Text = matches.Count == 1 ? "1 result" : $"{matches.Count} results";
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = ConfirmSelection();
                break;
            case Key.Escape:
                Cancel();
                e.Handled = true;
                break;
        }
    }

    private void OnResultsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = ConfirmSelection();
        }
        else if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        ConfirmSelection();
    }

    private void MoveSelection(int delta)
    {
        if (ResultsList.ItemCount == 0)
        {
            return;
        }

        var next = Math.Clamp(ResultsList.SelectedIndex + delta, 0, ResultsList.ItemCount - 1);
        ResultsList.SelectedIndex = next;
        ResultsList.ScrollIntoView(next);
    }

    private bool ConfirmSelection()
    {
        if (ResultsList.SelectedItem is not QuickAccessItem selected)
        {
            return false;
        }

        _result.TrySetResult(selected);
        Close();
        return true;
    }

    private void Cancel()
    {
        _result.TrySetResult(null);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _result.TrySetResult(null);
        base.OnClosed(e);
    }
}
