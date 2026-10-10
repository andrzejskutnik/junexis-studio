using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Junexis.Studio.Host;

namespace Junexis.Studio.Shell;

/// <summary>
/// Widok panelu <c>Why?</c> dla TraceCollector.
/// </summary>
public partial class WhyPanelView : UserControl
{
    /// <summary>
    /// Inicjalizuje widok i podpina zdarzenia UI.
    /// </summary>
    public WhyPanelView()
    {
        InitializeComponent();
        LoadButton.Click += OnLoadClick;
        EventIdTextBox.KeyDown += OnEventIdKeyDown;
    }

    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel vm)
        {
            await vm.LoadAsync();
        }
    }

    private async void OnEventIdKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is WhyPanelViewModel vm)
        {
            e.Handled = true;
            await vm.LoadAsync();
        }
    }

    private async void OnPreviousEventClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            await viewModel.NavigateHistoryAsync(previous: true);
    }

    private async void OnNextEventClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            await viewModel.NavigateHistoryAsync(previous: false);
    }

    private void OnCommunicationDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel &&
            CommunicationsList.SelectedItem is WhyCommunicationResolution resolution)
            viewModel.NavigateTo(resolution);
    }

    private async void OnLoadCorrelatedWhyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            await viewModel.LoadCorrelatedWhyAsync();
    }

    private async void OnRestartNarrativeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            await viewModel.RestartNarrativeAsync();
    }

    private async void OnRefreshNarrativeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            await viewModel.RefreshNarrativeAsync();
    }

    private async void OnStopNarrativeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            await viewModel.StopNarrativeAsync();
    }

    private void OnPauseRuntimeFollowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.PauseRuntimeFollow();
    }

    private async void OnResumeRuntimeFollowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            await viewModel.ResumeRuntimeFollowAsync();
    }

    private void OnPinStoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.PinCurrentStory();
    }

    private void OnCompareStoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.CompareWithPinnedStory();
    }

    private void OnClearPinnedStoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.ClearPinnedStory();
    }

    private void OnSaveBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.SaveBookmark();
    }

    private void OnOpenBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.OpenSelectedBookmark();
    }

    private void OnRemoveBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.RemoveSelectedBookmark();
    }

    private async void OnImportDiagnosticSessionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WhyPanelViewModel viewModel)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Junexis Narrative diagnostic session",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Junexis diagnostic bundle")
                {
                    Patterns = ["*.zip"]
                }
            ]
        });
        if (files.Count == 1)
            await viewModel.ImportDiagnosticSessionAsync(files[0].Path.LocalPath);
    }

    private void OnCloseImportedSessionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.CloseImportedDiagnosticSession();
    }

    private void OnCompareImportedWithLiveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.CompareImportedWithLive();
    }

    private void OnTrustImportedSignerClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.TrustImportedSigner();
    }

    private void OnRemoveImportedSignerTrustClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.RemoveImportedSignerTrust();
    }

    private void OnAddReviewEntryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.AddReviewEntry();
    }

    private void OnGenerateMentorReviewProposalsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.GenerateMentorReviewProposals();
    }

    private void OnAcceptMentorReviewProposalClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.AcceptSelectedMentorReviewProposal();
    }

    private void OnDismissMentorReviewProposalClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.DismissSelectedMentorReviewProposal();
    }

    private void OnRemoveReviewEntryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.RemoveSelectedReviewEntry();
    }

    private void OnSaveReviewRevisionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.SaveReviewRevision();
    }

    private void OnCompareReviewRevisionsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WhyPanelViewModel viewModel)
            viewModel.CompareLatestReviewRevisions();
    }

    private async void OnSignReviewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WhyPanelViewModel viewModel)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;
        var keys = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select ECDSA P-256 key for the saved review",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PEM private key")
                {
                    Patterns = ["*.pem", "*.key"]
                }
            ]
        });
        if (keys.Count != 1)
            return;
        var keyPath = keys[0].Path.LocalPath;
        await viewModel.SignReviewAsync(
            keyPath,
            Path.GetFileNameWithoutExtension(keyPath));
    }

    private async void OnExportDiagnosticCaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WhyPanelViewModel viewModel)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            DiagnosticCaseSaveOptions("junexis-diagnostic-case.zip"));
        if (file is not null)
            await viewModel.ExportDiagnosticCaseAsync(file.Path.LocalPath);
    }

    private async void OnImportDiagnosticCaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WhyPanelViewModel viewModel)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Junexis Narrative diagnostic case",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Junexis diagnostic case")
                {
                    Patterns = ["*.zip"]
                }
            ]
        });
        if (files.Count == 1)
            await viewModel.ImportDiagnosticCaseAsync(files[0].Path.LocalPath);
    }

    private async void OnExportSignedDiagnosticSessionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WhyPanelViewModel viewModel)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;
        var keys = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select externally managed ECDSA P-256 private key",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PEM private key")
                {
                    Patterns = ["*.pem", "*.key"]
                }
            ]
        });
        if (keys.Count != 1)
            return;
        var destination = await topLevel.StorageProvider.SaveFilePickerAsync(
            DiagnosticBundleSaveOptions("junexis-diagnostic-session-signed.zip"));
        if (destination is null)
            return;
        var keyPath = keys[0].Path.LocalPath;
        var signerName = Path.GetFileNameWithoutExtension(keyPath);
        await viewModel.ExportSignedDiagnosticSessionAsync(
            destination.Path.LocalPath,
            keyPath,
            signerName);
    }

    private async void OnExportDiagnosticSessionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WhyPanelViewModel viewModel)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            DiagnosticBundleSaveOptions("junexis-diagnostic-session.zip"));
        if (file is not null)
            await viewModel.ExportDiagnosticSessionAsync(file.Path.LocalPath);
    }

    private static FilePickerSaveOptions DiagnosticBundleSaveOptions(
        string suggestedFileName) => new()
        {
            Title = "Export Junexis Narrative diagnostic session",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                new FilePickerFileType("Junexis diagnostic bundle")
                {
                    Patterns = ["*.zip"]
                }
            ]
        };

    private static FilePickerSaveOptions DiagnosticCaseSaveOptions(
        string suggestedFileName) => new()
        {
            Title = "Export Junexis Narrative diagnostic case",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                new FilePickerFileType("Junexis diagnostic case")
                {
                    Patterns = ["*.zip"]
                }
            ]
        };

    private async void OnExportJsonClick(object? sender, RoutedEventArgs e) =>
        await ExportAsync("json", "JSON", (viewModel, path) => viewModel.ExportJsonAsync(path));

    private async void OnExportMarkdownClick(object? sender, RoutedEventArgs e) =>
        await ExportAsync("md", "Markdown", (viewModel, path) => viewModel.ExportMarkdownAsync(path));

    private async Task ExportAsync(
        string extension,
        string formatName,
        Func<WhyPanelViewModel, string, Task> export)
    {
        if (DataContext is not WhyPanelViewModel viewModel || viewModel.DiagnosticStory is null)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export Diagnostic Story as {formatName}",
            SuggestedFileName = $"diagnostic-story-{viewModel.DiagnosticStory.TargetEventId}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices =
            [
                new FilePickerFileType(formatName)
                {
                    Patterns = [$"*.{extension}"]
                }
            ]
        });
        if (file is not null)
            await export(viewModel, file.Path.LocalPath);
    }
}
