using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using System;
using System.ComponentModel;

namespace Junexis.Studio.Shell;

/// <summary>
/// Widok pojedynczej zakładki dokumentu (AvaloniaEdit TextEditor z synchronizacją VM).
/// Każda zakładka posiada własną instancję <see cref="TextEditor"/>.
/// </summary>
public partial class DocumentTabView : UserControl
{
    private DocumentTabViewModel? _vm;
    private bool _updatingFromVm;
    private bool _updatingFromEditor;
    private SearchPanel? _searchPanel;
    private BreakpointMargin? _breakpointMargin;
    private DiagnosticMargin? _diagnosticMargin;
    private ExecutionLineRenderer? _executionLineRenderer;
    private DiagnosticSquiggleRenderer? _diagnosticRenderer;
    private CancellationTokenSource? _hoverCts;
    private CancellationTokenSource? _completionCts;
    private Popup? _completionPopup;
    private ListBox? _completionList;
    private IReadOnlyList<LanguageCompletionItem> _completionItems = [];
    private SemanticTokenColorizer? _semanticColorizer;
    private CancellationTokenSource? _semanticTokensCts;

    /// <summary>
    /// Tworzy widok zakładki i rejestruje handler zmian tekstu.
    /// </summary>
    public DocumentTabView()
    {
        InitializeComponent();
        EditorTextBox.TextChanged += OnEditorTextChanged;
        // Add breathing room between the line-number gutter and the first character of code.
        EditorTextBox.TextArea.TextView.Margin = new Avalonia.Thickness(6, 0, 0, 0);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnDataContextChanged(e);

        _vm = DataContext as DocumentTabViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            SyncEditorFromVm();
            ApplySyntaxHighlighting();
            ApplyEditorPreferences();
            PushCaretPositionToVm();
        }
    }

    /// <summary>
    /// Po podłączeniu do drzewa wizualnego ponownie synchronizuje treść,
    /// bo <see cref="OnDataContextChanged"/> może odpalić ZANIM ControlTheme
    /// zostanie przyłożony (TextArea nie jest jeszcze w visual tree).
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncEditorFromVm();

        if (_searchPanel is null)
        {
            _searchPanel = SearchPanel.Install(EditorTextBox);
        }

        EnsureBreakpointMargin();
        EnsureDiagnosticVisuals();
        EnsureExecutionLineRenderer();
        EnsureSemanticColorizer();
        _ = RefreshSemanticTokensAsync();

        EditorTextBox.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        EditorTextBox.TextArea.TextEntered += OnEditorTextEntered;
        EditorTextBox.TextArea.KeyDown += OnEditorKeyDown;
        EditorTextBox.TextArea.TextView.PointerHover += OnEditorPointerHover;
        EditorTextBox.TextArea.TextView.PointerHoverStopped += OnEditorPointerHoverStopped;
        EditorTextBox.GotFocus += OnEditorGotFocus;
        PushCaretPositionToVm();
        UpdateClipboardService();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        EditorTextBox.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
        EditorTextBox.TextArea.TextEntered -= OnEditorTextEntered;
        EditorTextBox.TextArea.KeyDown -= OnEditorKeyDown;
        EditorTextBox.TextArea.TextView.PointerHover -= OnEditorPointerHover;
        EditorTextBox.TextArea.TextView.PointerHoverStopped -= OnEditorPointerHoverStopped;
        EditorTextBox.GotFocus -= OnEditorGotFocus;
        CancelDebugHover();
        CloseCompletion();
        _semanticTokensCts?.Cancel();
        _semanticTokensCts?.Dispose();
        _semanticTokensCts = null;
        if (_semanticColorizer is not null)
        {
            EditorTextBox.TextArea.TextView.LineTransformers.Remove(_semanticColorizer);
            _semanticColorizer = null;
        }
        _executionLineRenderer?.Dispose();
        _executionLineRenderer = null;
        _diagnosticRenderer?.Dispose();
        _diagnosticRenderer = null;
        ClearClipboardServiceIfOwned();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is DocumentTabViewModel vm)
        {
            vm.Save();
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        PushCaretPositionToVm();
    }

    private void OnEditorGotFocus(object? sender, GotFocusEventArgs e)
    {
        UpdateClipboardService();
    }

    private void PushCaretPositionToVm()
    {
        if (_vm is null)
        {
            return;
        }

        var caret = EditorTextBox.TextArea.Caret;
        _vm.CaretLine = caret.Line;
        _vm.CaretColumn = caret.Column;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTabViewModel.Content))
        {
            if (!_updatingFromEditor)
            {
                Dispatcher.UIThread.Post(SyncEditorFromVm, DispatcherPriority.Input);
            }
            _ = RefreshSemanticTokensAsync();
        }
        else if (e.PropertyName == nameof(DocumentTabViewModel.FilePath))
        {
            ApplySyntaxHighlighting();
            _executionLineRenderer?.Invalidate();
            _ = RefreshSemanticTokensAsync();
        }
        else if (e.PropertyName == nameof(DocumentTabViewModel.NavigationRequestVersion))
        {
            ApplyNavigation();
        }
        else if (e.PropertyName == nameof(DocumentTabViewModel.IsSearchVisible))
        {
            ApplySearchVisibility();
        }
        else if (e.PropertyName == nameof(DocumentTabViewModel.SearchQuery))
        {
            ApplySearchPattern();
        }
        else if (e.PropertyName == nameof(DocumentTabViewModel.ReplaceQuery))
        {
            ApplyReplacePattern();
        }
    }

    private void SyncEditorFromVm()
    {
        if (_vm is null)
        {
            return;
        }

        var text = _vm.Content ?? string.Empty;

        var document = EditorTextBox.Document;
        if (document is not null)
        {
            if (document.Text == text)
            {
                return;
            }

            _updatingFromVm = true;
            try
            {
                document.Text = text;
                EditorTextBox.CaretOffset = 0;
            }
            finally
            {
                _updatingFromVm = false;
            }
        }
        else
        {
            if (EditorTextBox.Text == text)
            {
                return;
            }

            _updatingFromVm = true;
            try
            {
                EditorTextBox.Text = text;
            }
            finally
            {
                _updatingFromVm = false;
            }
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_updatingFromVm || _vm is null)
        {
            return;
        }

        _updatingFromEditor = true;
        try
        {
            _vm.Content = EditorTextBox.Text;
        }
        finally
        {
            _updatingFromEditor = false;
        }
    }

    private void ApplyNavigation()
    {
        if (_vm is null)
        {
            return;
        }

        var line = _vm.RequestedLine;
        var col = _vm.RequestedColumn;
        if (line is null || col is null)
        {
            return;
        }

        var document = EditorTextBox.Document;
        if (document is null)
        {
            return;
        }

        var clampedLine = Math.Clamp(line.Value, 1, document.LineCount);
        var docLine = document.GetLineByNumber(clampedLine);
        var offset = docLine.Offset + Math.Max(0, col.Value - 1);
        offset = Math.Min(offset, docLine.EndOffset);

        EditorTextBox.CaretOffset = offset;
        EditorTextBox.ScrollToLine(clampedLine);
        EditorTextBox.Focus();
    }

    /// <summary>
    /// Otwiera lub zamyka panel wyszukiwania AvaloniaEdit zgodnie z <see cref="DocumentTabViewModel.IsSearchVisible"/>.
    /// </summary>
    private void ApplySearchVisibility()
    {
        if (_searchPanel is null || _vm is null)
        {
            return;
        }

        if (_vm.IsSearchVisible)
        {
            _searchPanel.Open();
        }
        else
        {
            _searchPanel.Close();
        }
    }

    /// <summary>
    /// Synchronizuje wzorzec wyszukiwania SearchPanel z <see cref="DocumentTabViewModel.SearchQuery"/>.
    /// </summary>
    private void ApplySearchPattern()
    {
        if (_searchPanel is null || _vm is null)
        {
            return;
        }

        _searchPanel.SearchPattern = _vm.SearchQuery;
    }

    /// <summary>
    /// Synchronizuje wzorzec zamiany SearchPanel z <see cref="DocumentTabViewModel.ReplaceQuery"/>.
    /// </summary>
    private void ApplyReplacePattern()
    {
        if (_searchPanel is null || _vm is null)
        {
            return;
        }

        _searchPanel.ReplacePattern = _vm.ReplaceQuery;
    }

    /// <summary>
    /// Ustawia kolorowanie składni na podstawie rozszerzenia otwartego pliku.
    /// </summary>
    private void ApplySyntaxHighlighting()
    {
        var path = _vm?.FilePath;
        EditorTextBox.SyntaxHighlighting = JunexisHighlightingRegistrar.GetHighlightingForFile(path);
    }

    /// <summary>
    /// Stosuje preferencje edytora (czcionka, rozmiar, word wrap, tab size, numery linii)
    /// z <see cref="IPreferencesService"/>.
    /// </summary>
    private void ApplyEditorPreferences()
    {
        var prefs = GetPreferences();
        if (prefs is null)
        {
            return;
        }

        var editor = prefs.Editor;

        if (!string.IsNullOrWhiteSpace(editor.FontFamily))
        {
            EditorTextBox.FontFamily = new FontFamily(editor.FontFamily);
        }

        if (editor.FontSize > 0)
        {
            EditorTextBox.FontSize = editor.FontSize;
        }

        EditorTextBox.WordWrap = editor.WordWrap;
        EditorTextBox.ShowLineNumbers = editor.ShowLineNumbers;

        if (editor.TabSize > 0)
        {
            EditorTextBox.Options.IndentationSize = editor.TabSize;
        }
    }

    /// <summary>
    /// Pobiera bieżące preferencje z IHostContext, jeśli dostępny.
    /// </summary>
    private JunexisPreferences? GetPreferences()
    {
        var window = this.VisualRoot as Avalonia.Controls.Window;
        if (window?.DataContext is IHostContext hostContext)
        {
            return hostContext.Preferences.Current;
        }

        return null;
    }

    private void UpdateClipboardService()
    {
        if (TryGetHostContext(out var hostContext)
            && hostContext is not null
            && hostContext.Capabilities.TryGet<IEditorClipboardService>(out var clipboard)
            && clipboard is EditorClipboardService editorClipboard)
        {
            editorClipboard.SetActiveEditor(EditorTextBox);
        }
    }

    private void ClearClipboardServiceIfOwned()
    {
        if (TryGetHostContext(out var hostContext)
            && hostContext is not null
            && hostContext.Capabilities.TryGet<IEditorClipboardService>(out var clipboard)
            && clipboard is EditorClipboardService editorClipboard)
        {
            editorClipboard.ClearActiveEditor(EditorTextBox);
        }
    }

    private void OnEditorTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is "." or ":" or "<" or "@")
            _ = ShowCompletionAsync();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            e.Handled = true;
            _ = ShowCompletionAsync();
            return;
        }

        if (_completionPopup?.IsOpen != true || _completionList is null)
            return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseCompletion();
        }
        else if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            e.Handled = true;
            AcceptCompletion();
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            _completionList.SelectedIndex = Math.Min(
                _completionItems.Count - 1,
                _completionList.SelectedIndex + 1);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            _completionList.SelectedIndex = Math.Max(0, _completionList.SelectedIndex - 1);
        }
    }

    private async Task ShowCompletionAsync()
    {
        if (_vm is null || !TryGetHostContext(out var context) || context is null
            || !context.Capabilities.TryGet<ILanguageService>(out var languageService)
            || languageService is null || !languageService.Supports(_vm.FilePath))
            return;

        _completionCts?.Cancel();
        _completionCts?.Dispose();
        var cancellation = new CancellationTokenSource();
        _completionCts = cancellation;
        try
        {
            var caret = EditorTextBox.TextArea.Caret;
            var items = await languageService.CompletionAsync(
                _vm.FilePath,
                new LanguagePosition(caret.Line - 1, caret.Column - 1),
                cancellation.Token).ConfigureAwait(false);
            if (items.Count == 0 || cancellation.IsCancellationRequested)
                return;
            await Dispatcher.UIThread.InvokeAsync(() => OpenCompletion(items));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            context.Output.Write("Language Services", $"Completion failed: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private void OpenCompletion(IReadOnlyList<LanguageCompletionItem> items)
    {
        _completionItems = items;
        _completionList ??= CreateCompletionList();
        _completionList.ItemsSource = items.Select(item =>
            string.IsNullOrWhiteSpace(item.Detail) ? item.Label : $"{item.Label}  {item.Detail}").ToArray();
        _completionList.SelectedIndex = 0;
        _completionPopup ??= new Popup
        {
            PlacementTarget = EditorTextBox,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child = _completionList,
        };
        _completionPopup.IsOpen = true;
    }

    private ListBox CreateCompletionList()
    {
        var list = new ListBox
        {
            MinWidth = 260,
            MaxWidth = 640,
            MaxHeight = 300,
        };
        list.DoubleTapped += (_, _) => AcceptCompletion();
        return list;
    }

    private void AcceptCompletion()
    {
        if (_completionList is null || EditorTextBox.Document is not { } document)
            return;
        var index = _completionList.SelectedIndex;
        if (index < 0 || index >= _completionItems.Count)
            return;

        var offset = EditorTextBox.CaretOffset;
        var start = offset;
        while (start > 0 && IsCompletionCharacter(document.GetCharAt(start - 1)))
            start--;
        document.Replace(start, offset - start, _completionItems[index].InsertText);
        EditorTextBox.CaretOffset = start + _completionItems[index].InsertText.Length;
        CloseCompletion();
    }

    private void CloseCompletion()
    {
        _completionCts?.Cancel();
        _completionCts?.Dispose();
        _completionCts = null;
        if (_completionPopup is not null)
            _completionPopup.IsOpen = false;
        _completionItems = [];
    }

    private static bool IsCompletionCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '$';

    private void EnsureBreakpointMargin()
    {
        if (_breakpointMargin is not null || !TryGetHostContext(out var hostContext) || hostContext is null
            || !hostContext.Capabilities.TryGet<IBreakpointGlyphService>(out var glyphs) || glyphs is null)
            return;
        _breakpointMargin = new BreakpointMargin(glyphs, () => _vm?.FilePath);
        EditorTextBox.TextArea.LeftMargins.Insert(0, _breakpointMargin);
    }

    private void EnsureExecutionLineRenderer()
    {
        if (_executionLineRenderer is not null || !TryGetHostContext(out var hostContext) || hostContext is null
            || !hostContext.Capabilities.TryGet<IDebugExecutionLocationService>(out var locations) || locations is null)
            return;
        var textView = EditorTextBox.TextArea.TextView;
        _executionLineRenderer = new ExecutionLineRenderer(textView, locations, () => _vm?.FilePath);
        textView.BackgroundRenderers.Add(_executionLineRenderer);
    }

    private void EnsureDiagnosticVisuals()
    {
        if (!TryGetHostContext(out var hostContext) || hostContext is null
            || !hostContext.Capabilities.TryGet<IEditorDiagnosticService>(out var diagnostics)
            || diagnostics is null)
            return;
        if (_diagnosticMargin is null)
        {
            _diagnosticMargin = new DiagnosticMargin(diagnostics, () => _vm?.FilePath);
            var breakpointIndex = _breakpointMargin is null ? 0 : 1;
            EditorTextBox.TextArea.LeftMargins.Insert(breakpointIndex, _diagnosticMargin);
        }
        if (_diagnosticRenderer is null)
        {
            var textView = EditorTextBox.TextArea.TextView;
            _diagnosticRenderer = new DiagnosticSquiggleRenderer(
                textView,
                diagnostics,
                () => _vm?.FilePath);
            textView.BackgroundRenderers.Add(_diagnosticRenderer);
        }
    }

    private void EnsureSemanticColorizer()
    {
        if (_semanticColorizer is not null)
            return;
        _semanticColorizer = new SemanticTokenColorizer();
        EditorTextBox.TextArea.TextView.LineTransformers.Add(_semanticColorizer);
    }

    private async Task RefreshSemanticTokensAsync()
    {
        if (_vm is null || _semanticColorizer is null
            || !TryGetHostContext(out var context) || context is null
            || !context.Capabilities.TryGet<ILanguageService>(out var languageService)
            || languageService is null || !languageService.Supports(_vm.FilePath))
            return;
        _semanticTokensCts?.Cancel();
        _semanticTokensCts?.Dispose();
        var cancellation = new CancellationTokenSource();
        _semanticTokensCts = cancellation;
        try
        {
            await Task.Delay(180, cancellation.Token).ConfigureAwait(false);
            var tokens = await languageService.SemanticTokensAsync(_vm.FilePath, cancellation.Token)
                .ConfigureAwait(false);
            if (cancellation.IsCancellationRequested)
                return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _semanticColorizer?.SetTokens(tokens);
                EditorTextBox.TextArea.TextView.InvalidateVisual();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            context.Output.Write("Language Services", $"Semantic tokens failed: {ex.Message}", OutputSeverity.Warning);
        }
    }

    private async void OnEditorPointerHover(object? sender, PointerEventArgs e)
    {
        if (!TryGetHostContext(out var hostContext) || hostContext is null
            || EditorTextBox.Document is not { } document
            || _vm is null)
            return;
        var textView = EditorTextBox.TextArea.TextView;
        var position = textView.GetPositionFloor(e.GetPosition(textView) + textView.ScrollOffset);
        if (position is null)
            return;
        var offset = document.GetOffset(position.Value.Line, position.Value.Column);

        CancelDebugHover();
        var cancellation = new CancellationTokenSource();
        _hoverCts = cancellation;
        try
        {
            string? tooltip = null;
            var expression = ExtractHoverExpression(document.Text, offset);
            if (expression is not null
                && hostContext.Capabilities.TryGet<IDebugHoverService>(out var debugHover)
                && debugHover is not null)
            {
                var evaluation = await debugHover.EvaluateAsync(expression, cancellation.Token);
                if (evaluation is not null)
                {
                    var type = string.IsNullOrWhiteSpace(evaluation.Type) ? string.Empty : $" ({evaluation.Type})";
                    tooltip = $"{evaluation.Expression} = {evaluation.Result}{type}";
                }
            }

            if (tooltip is null
                && hostContext.Capabilities.TryGet<IEditorDiagnosticService>(out var editorDiagnostics)
                && editorDiagnostics is not null)
            {
                var diagnostics = editorDiagnostics.GetAt(
                    _vm.FilePath,
                    position.Value.Line,
                    position.Value.Column);
                if (diagnostics.Count > 0)
                {
                    tooltip = string.Join(
                        Environment.NewLine,
                        diagnostics.Select(diagnostic => $"{diagnostic.Severity}: {diagnostic.Message}"));
                }
            }

            if (tooltip is null
                && hostContext.Capabilities.TryGet<ILanguageService>(out var languageService)
                && languageService is not null
                && languageService.Supports(_vm.FilePath))
            {
                var hover = await languageService.HoverAsync(
                    _vm.FilePath,
                    new LanguagePosition(position.Value.Line - 1, position.Value.Column - 1),
                    cancellation.Token);
                tooltip = hover?.Content;
            }

            // Prepend any local doc comment to whatever tooltip was found (or use it alone).
            if (expression is not null)
            {
                var docComment = ExtractLocalDocComment(document.Text, expression);
                if (!string.IsNullOrWhiteSpace(docComment))
                    tooltip = string.IsNullOrWhiteSpace(tooltip)
                        ? docComment
                        : $"{docComment}\n\n{tooltip}";
            }

            if (string.IsNullOrWhiteSpace(tooltip) || cancellation.IsCancellationRequested)
                return;
            ToolTip.SetTip(EditorTextBox, tooltip);
            ToolTip.SetIsOpen(EditorTextBox, true);
        }
        catch (OperationCanceledException)
        {
            // Moving the pointer invalidates the pending hover evaluation.
        }
        catch (Exception)
        {
            // Adapter evaluation errors should not interrupt editor interaction.
        }
    }

    private void OnEditorPointerHoverStopped(object? sender, PointerEventArgs e) => CancelDebugHover();

    private void CancelDebugHover()
    {
        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        _hoverCts = null;
        ToolTip.SetIsOpen(EditorTextBox, false);
        ToolTip.SetTip(EditorTextBox, null);
    }

    internal static string? ExtractHoverExpression(string text, int offset)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        offset = Math.Clamp(offset, 0, text.Length);
        if (offset == text.Length || !IsHoverCharacter(text[offset]))
            offset--;
        if (offset < 0 || !IsHoverCharacter(text[offset]))
            return null;
        var start = offset;
        while (start > 0 && IsHoverCharacter(text[start - 1]))
            start--;
        var end = offset + 1;
        while (end < text.Length && IsHoverCharacter(text[end]))
            end++;
        var expression = text[start..end].Trim('.');
        return expression.Length > 0 && (char.IsLetter(expression[0]) || expression[0] is '_' or '$')
            ? expression
            : null;
    }

    // Searches the document for a declaration of `word` and returns preceding // comments.
    internal static string? ExtractLocalDocComment(string documentText, string word)
    {
        if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(documentText))
            return null;

        var lines = documentText.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!IsDeclarationOf(trimmed, word))
                continue;

            // Walk backwards collecting consecutive // comment lines.
            var comments = new System.Collections.Generic.List<string>();
            for (var j = i - 1; j >= 0; j--)
            {
                var prev = lines[j].Trim();
                if (prev.StartsWith("//", StringComparison.Ordinal))
                    comments.Insert(0, prev.TrimStart('/').Trim());
                else if (string.IsNullOrWhiteSpace(prev))
                    break;
                else
                    break;
            }

            return comments.Count > 0 ? string.Join("\n", comments) : null;
        }

        return null;
    }

    private static bool IsDeclarationOf(string trimmedLine, string word)
    {
        // Declaration keywords that introduce a named symbol.
        var prefixes = new[]
        {
            "task ", "type ", "protocol ", "device ", "fn ", "resource ",
            "extern task ", "extern type ",
            "class ", "struct ", "enum ", "union ", "namespace ",
            "void ", "int ", "bool ", "float ", "double ", "char ", "auto ",
            "uint8_t ", "uint16_t ", "uint32_t ", "uint64_t ",
            "public ", "private ", "protected ",
        };
        foreach (var prefix in prefixes)
        {
            if (trimmedLine.StartsWith(prefix, StringComparison.Ordinal)
                && trimmedLine.Contains(word, StringComparison.Ordinal))
                return true;
        }
        // Also match: word( — bare function/task definition.
        return trimmedLine.StartsWith(word + "(", StringComparison.Ordinal)
            || trimmedLine.StartsWith(word + " ", StringComparison.Ordinal)
            || trimmedLine.StartsWith(word + "{", StringComparison.Ordinal);
    }

    private static bool IsHoverCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '$' or '.';

    private bool TryGetHostContext(out IHostContext? hostContext)
    {
        var window = VisualRoot as Window;
        hostContext = window?.DataContext as IHostContext;
        return hostContext is not null;
    }
}
