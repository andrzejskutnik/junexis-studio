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
/// Widok panelu czatu OMWiA. Subskrybuje zdarzenie MessagesChanged
/// z <see cref="OmwiaChatViewModel"/> i aktualizuje listę wiadomości
/// na wątku UI. Wysyłanie wiadomości obsługuje w async void, aby
/// nie blokować UI podczas oczekiwania na odpowiedź asystenta.
/// </summary>
public partial class OmwiaChatView : UserControl
{
    private OmwiaChatViewModel? _vm;
    private readonly ObservableCollection<AssistantMessage> _messages = new();

    /// <summary>
    /// Inicjalizuje widok i ustawia źródło danych dla listy wiadomości.
    /// </summary>
    public OmwiaChatView()
    {
        InitializeComponent();
        MessagesList.ItemsSource = _messages;
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm is not null)
        {
            _vm.MessagesChanged -= OnMessagesChanged;
        }

        _vm = DataContext as OmwiaChatViewModel;

        if (_vm is not null)
        {
            _vm.MessagesChanged += OnMessagesChanged;
            _vm.EnsureSession();
            RefreshMessages();
        }
    }

    private void OnMessagesChanged()
    {
        Dispatcher.UIThread.Post(RefreshMessages);
    }

    private void RefreshMessages()
    {
        if (_vm is null)
        {
            return;
        }

        _messages.Clear();

        foreach (var message in _vm.Messages)
        {
            _messages.Add(message);
        }

        if (_messages.Count > 0)
        {
            MessagesList.ScrollIntoView(_messages[_messages.Count - 1]);
        }
    }

    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || _vm.IsBusy)
        {
            return;
        }

        var text = InputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        InputBox.Text = string.Empty;

        try
        {
            await _vm.SendAsync(text).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Anulowanie jest prawidłowe — nic nie robimy.
        }
        catch (Exception)
        {
            // Błąd jest już zalogowany do Output przez OmwiaChatViewModel.
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
        {
            e.Handled = true;
            OnSendClick(sender, new RoutedEventArgs());
        }
    }
}
