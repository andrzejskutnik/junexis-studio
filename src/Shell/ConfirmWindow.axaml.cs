using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace Junexis.Studio.Shell;

/// <summary>
/// Prosty dialog potwierdzenia OK/Cancel używany przez workflow operacji na plikach.
/// </summary>
public partial class ConfirmWindow : Window
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    /// <summary>
    /// Tworzy okno dialogowe potwierdzenia.
    /// </summary>
    public ConfirmWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.Icon;
    }

    /// <summary>
    /// Wyświetla dialog potwierdzenia i zwraca <see langword="true"/> gdy użytkownik kliknie OK.
    /// </summary>
    /// <param name="owner">Okno-rodzic.</param>
    /// <param name="title">Tytuł okna.</param>
    /// <param name="message">Treść pytania.</param>
    /// <returns><see langword="true"/> gdy potwierdzone; <see langword="false"/> gdy anulowane.</returns>
    public static async Task<bool> ShowAndGetResultAsync(Window owner, string title, string message)
    {
        var window = new ConfirmWindow { Title = title };
        window.ConfirmMessage.Text = message;
        await window.ShowDialog(owner);
        return await window._tcs.Task;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(true);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(false);
        Close();
    }
}
