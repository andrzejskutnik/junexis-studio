using Avalonia.Controls;
using Avalonia.Interactivity;
using Junexis.Studio.Abstractions;
using System.Threading.Tasks;

namespace Junexis.Studio.Shell;

/// <summary>
/// Minimalny dialog pytający o niezapisane zmiany przed zamknięciem dokumentu.
/// Zwraca <see cref="UnsavedChangesResult"/> przez <see cref="ShowAndGetResultAsync"/>.
/// </summary>
public partial class UnsavedChangesWindow : Window
{
    private readonly TaskCompletionSource<UnsavedChangesResult> _tcs = new();

    /// <summary>Tworzy okno dialogowe.</summary>
    public UnsavedChangesWindow()
    {
        InitializeComponent();
        Icon = AppIconFactory.Icon;
    }

    /// <summary>
    /// Wyświetla dialog dla podanej nazwy pliku i czeka na odpowiedź użytkownika.
    /// </summary>
    /// <param name="owner">Okno-rodzic.</param>
    /// <param name="fileName">Nazwa pliku z niezapisanymi zmianami.</param>
    /// <returns>Wynik wybrany przez użytkownika.</returns>
    public static async Task<UnsavedChangesResult> ShowAndGetResultAsync(Window owner, string fileName)
    {
        var window = new UnsavedChangesWindow();
        window.MessageText.Text = $"'{fileName}' has unsaved changes. Save before closing?";
        await window.ShowDialog(owner);
        return await window._tcs.Task;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(UnsavedChangesResult.Save);
        Close();
    }

    private void OnDiscard(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(UnsavedChangesResult.Discard);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(UnsavedChangesResult.Cancel);
        Close();
    }

    /// <summary>
    /// Gdy okno zamknięte przez Alt+F4 lub system — traktuj jako Cancel.
    /// </summary>
    protected override void OnClosed(System.EventArgs e)
    {
        _tcs.TrySetResult(UnsavedChangesResult.Cancel);
        base.OnClosed(e);
    }
}
