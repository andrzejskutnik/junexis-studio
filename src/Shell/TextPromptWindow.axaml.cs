using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace Junexis.Studio.Shell;

/// <summary>
/// Prosty dialog tekstowy używany przez workflow operacji na plikach.
/// </summary>
public partial class TextPromptWindow : Window
{
    private readonly TaskCompletionSource<string?> _tcs = new();

    /// <summary>
    /// Tworzy okno dialogowe promptu tekstowego.
    /// </summary>
    public TextPromptWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.Icon;
        PromptInput.KeyDown += OnPromptInputKeyDown;
    }

    /// <summary>
    /// Wyświetla dialog promptu i zwraca tekst podany przez użytkownika.
    /// </summary>
    /// <param name="owner">Okno-rodzic.</param>
    /// <param name="title">Tytuł okna.</param>
    /// <param name="message">Treść promptu.</param>
    /// <param name="initialValue">Wartość początkowa pola tekstowego.</param>
    /// <returns>Wprowadzony tekst albo <see langword="null"/>.</returns>
    public static async Task<string?> ShowAndGetResultAsync(Window owner, string title, string message, string initialValue)
    {
        var window = new TextPromptWindow
        {
            Title = title,
        };

        window.PromptMessage.Text = message;
        window.PromptInput.Text = initialValue;
        window.Opened += (_, _) =>
        {
            window.PromptInput.Focus();
            window.PromptInput.SelectAll();
        };

        await window.ShowDialog(owner);
        return await window._tcs.Task;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(PromptInput.Text);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(null);
        Close();
    }

    private void OnPromptInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = TrueConfirm();
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = TrueCancel();
        }
    }

    private bool TrueConfirm()
    {
        _tcs.TrySetResult(PromptInput.Text);
        Close();
        return true;
    }

    private bool TrueCancel()
    {
        _tcs.TrySetResult(null);
        Close();
        return true;
    }

    /// <summary>
    /// Zamknięcie okna przez system traktowane jest jako anulowanie.
    /// </summary>
    /// <param name="e">Argument zdarzenia.</param>
    protected override void OnClosed(System.EventArgs e)
    {
        _tcs.TrySetResult(null);
        base.OnClosed(e);
    }
}