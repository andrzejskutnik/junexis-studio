using Junexis.Studio.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Junexis.Studio.Host;

/// <summary>
/// ViewModel pojedynczej zakładki dokumentu w edytorze.
/// Przechowuje ścieżkę, treść, stan dirty/out-of-date oraz informacje nawigacyjne.
/// </summary>
public sealed class DocumentTabViewModel : INotifyPropertyChanged
{
    private readonly IWorkspace _workspace;
    private readonly IEventBus _eventBus;
    private readonly IOutput _output;

    private string _filePath;
    private string _fileName;
    private string _content = string.Empty;
    private bool _isDirty;
    private bool _isPreview;
    private bool _isOutOfDate;
    private bool _suppressDirty;

    private int? _requestedLine;
    private int? _requestedColumn;
    private int _navigationRequestVersion;
    private bool _isSearchVisible;
    private string _searchQuery = string.Empty;
    private string _replaceQuery = string.Empty;
    private bool _searchMatchCase;
    private int _searchMatchCount;
    private int _caretLine = 1;
    private int _caretColumn = 1;

    /// <summary>
    /// Tworzy ViewModel zakładki z wczytaną treścią pliku.
    /// </summary>
    /// <param name="filePath">Pełna ścieżka do pliku.</param>
    /// <param name="content">Początkowa treść pliku.</param>
    /// <param name="workspace">Workspace do IO.</param>
    /// <param name="eventBus">EventBus do publikacji zdarzeń dokumentu.</param>
    /// <param name="output">Output do logowania problemów.</param>
    public DocumentTabViewModel(string filePath, string content, IWorkspace workspace, IEventBus eventBus, IOutput output)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _fileName = Path.GetFileName(filePath) ?? filePath;

        _suppressDirty = true;
        _content = content ?? string.Empty;
        _suppressDirty = false;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Zdarzenie wywoływane gdy użytkownik żąda zamknięcia tej zakładki.
    /// </summary>
    public event Action<DocumentTabViewModel>? CloseRequested;

    /// <summary>
    /// Pełna ścieżka do pliku powiązanego z zakładką.
    /// </summary>
    public string FilePath
    {
        get => _filePath;
        private set
        {
            if (SetField(ref _filePath, value))
            {
                FileName = Path.GetFileName(value) ?? value;
            }
        }
    }

    /// <summary>
    /// Nazwa pliku (bez katalogu) — wyświetlana w nagłówku zakładki.
    /// </summary>
    public string FileName
    {
        get => _fileName;
        private set => SetField(ref _fileName, value);
    }

    /// <summary>
    /// Aktualna treść dokumentu.
    /// </summary>
    public string Content
    {
        get => _content;
        set
        {
            if (SetField(ref _content, value ?? string.Empty))
            {
                if (!_suppressDirty)
                {
                    if (_isPreview) Promote();
                    IsDirty = true;
                    _eventBus.Publish(new DocumentChanged(FilePath, Content));
                }

                SearchMatchCount = CountMatches(_content, _searchQuery, _searchMatchCase);
            }
        }
    }

    /// <summary>Whether this tab is in preview mode (italic title, replaced by next single-click).</summary>
    public bool IsPreview
    {
        get => _isPreview;
        private set => SetField(ref _isPreview, value);
    }

    /// <summary>Promotes this tab from preview to permanent.</summary>
    public void Promote() => IsPreview = false;

    /// <summary>Sets the preview flag directly (called by EditorPanelViewModel after tab is created).</summary>
    internal void SetPreview(bool value) => IsPreview = value;

    /// <summary>
    /// Czy w edytorze są niezapisane zmiany.
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetField(ref _isDirty, value);
    }

    /// <summary>
    /// Czy dokument może być nieaktualny względem dysku (np. zmieniony/usunięty poza IDE).
    /// </summary>
    public bool IsOutOfDate
    {
        get => _isOutOfDate;
        private set => SetField(ref _isOutOfDate, value);
    }

    /// <summary>
    /// Ostatnio żądana linia (1-based) do ustawienia kursora.
    /// </summary>
    public int? RequestedLine
    {
        get => _requestedLine;
        private set => SetField(ref _requestedLine, value);
    }

    /// <summary>
    /// Ostatnio żądana kolumna (1-based) do ustawienia kursora.
    /// </summary>
    public int? RequestedColumn
    {
        get => _requestedColumn;
        private set => SetField(ref _requestedColumn, value);
    }

    /// <summary>
    /// Licznik żądań nawigacji — zwiększany przy każdej prośbie,
    /// aby UI mogło reagować również na powtórzenie tej samej lokacji.
    /// </summary>
    public int NavigationRequestVersion
    {
        get => _navigationRequestVersion;
        private set => SetField(ref _navigationRequestVersion, value);
    }

    /// <summary>
    /// Aktualna linia kursora (1-based) — ustawiana przez widok po zmianie pozycji karetki.
    /// </summary>
    public int CaretLine
    {
        get => _caretLine;
        set => SetField(ref _caretLine, Math.Max(1, value));
    }

    /// <summary>
    /// Aktualna kolumna kursora (1-based) — ustawiana przez widok po zmianie pozycji karetki.
    /// </summary>
    public int CaretColumn
    {
        get => _caretColumn;
        set => SetField(ref _caretColumn, Math.Max(1, value));
    }

    /// <summary>
    /// Czy panel wyszukiwania jest widoczny w tym dokumencie.
    /// </summary>
    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        set => SetField(ref _isSearchVisible, value);
    }

    /// <summary>
    /// Aktualna fraza wyszukiwania. Zmiana powoduje aktualizację <see cref="SearchMatchCount"/>.
    /// </summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value ?? string.Empty))
                SearchMatchCount = CountMatches(_content, _searchQuery, _searchMatchCase);
        }
    }

    /// <summary>
    /// Czy wyszukiwanie rozróżnia wielkie i małe litery.
    /// </summary>
    public bool SearchMatchCase
    {
        get => _searchMatchCase;
        set
        {
            if (SetField(ref _searchMatchCase, value))
                SearchMatchCount = CountMatches(_content, _searchQuery, _searchMatchCase);
        }
    }

    /// <summary>
    /// Liczba dopasowań frazy wyszukiwania w bieżącym dokumencie.
    /// Aktualizowana po zmianie <see cref="SearchQuery"/>, <see cref="SearchMatchCase"/> lub <see cref="Content"/>.
    /// </summary>
    public int SearchMatchCount
    {
        get => _searchMatchCount;
        private set => SetField(ref _searchMatchCount, value);
    }

    /// <summary>
    /// Fraza zastępująca używana w operacjach Replace.
    /// </summary>
    public string ReplaceQuery
    {
        get => _replaceQuery;
        set => SetField(ref _replaceQuery, value ?? string.Empty);
    }

    /// <summary>
    /// Przełącza widoczność panelu wyszukiwania.
    /// </summary>
    public void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
    }

    /// <summary>
    /// Otwiera panel wyszukiwania i zamiany bez przełączania go do stanu ukrytego.
    /// </summary>
    public void OpenReplace()
    {
        IsSearchVisible = true;
    }

    /// <summary>
    /// Otwiera panel wyszukiwania i ustawia frazę, a następnie przechodzi do pierwszego dopasowania.
    /// </summary>
    /// <param name="query">Fraza wyszukiwania.</param>
    public void OpenSearch(string query)
    {
        SearchQuery = query ?? string.Empty;
        IsSearchVisible = true;
        if (!string.IsNullOrEmpty(_searchQuery))
            FindNext();
    }

    /// <summary>
    /// Przechodzi do następnego wystąpienia <see cref="SearchQuery"/> w dokumencie.
    /// Wyszukiwanie wraca na początek po dotarciu do końca. Pokazuje panel wyszukiwania.
    /// </summary>
    public void FindNext()
    {
        if (string.IsNullOrEmpty(_searchQuery)) return;
        IsSearchVisible = true;

        var comparison = _searchMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var content = _content;
        var startOffset = Math.Min(GetOffset(content, _caretLine, _caretColumn) + 1, content.Length);

        var idx = startOffset < content.Length
            ? content.IndexOf(_searchQuery, startOffset, comparison)
            : -1;
        if (idx < 0)
            idx = content.IndexOf(_searchQuery, 0, comparison);

        if (idx >= 0)
        {
            var (line, col) = GetLineColumn(content, idx);
            NavigateTo(line, col);
        }
    }

    /// <summary>
    /// Przechodzi do poprzedniego wystąpienia <see cref="SearchQuery"/> w dokumencie.
    /// Wyszukiwanie przeskakuje na koniec po dotarciu do początku. Pokazuje panel wyszukiwania.
    /// </summary>
    public void FindPrev()
    {
        if (string.IsNullOrEmpty(_searchQuery)) return;
        IsSearchVisible = true;

        var comparison = _searchMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var content = _content;
        var caretOffset = GetOffset(content, _caretLine, _caretColumn);
        var startFrom = caretOffset - 1;

        int idx;
        if (startFrom < 0)
        {
            idx = LastIndexOf(content, _searchQuery, content.Length - 1, comparison);
        }
        else
        {
            idx = LastIndexOf(content, _searchQuery, startFrom, comparison);
            if (idx < 0)
                idx = LastIndexOf(content, _searchQuery, content.Length - 1, comparison);
        }

        if (idx >= 0)
        {
            var (line, col) = GetLineColumn(content, idx);
            NavigateTo(line, col);
        }
    }

    /// <summary>
    /// Zastępuje następne wystąpienie <see cref="SearchQuery"/> wartością <see cref="ReplaceQuery"/>.
    /// Po zamianie przechodzi do kolejnego dopasowania.
    /// </summary>
    /// <returns>Liczba wykonanych zamian (0 lub 1).</returns>
    public int ReplaceNext()
    {
        if (string.IsNullOrEmpty(_searchQuery)) return 0;

        var comparison = _searchMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var content = _content;
        var caretOffset = GetOffset(content, _caretLine, _caretColumn);

        // Sprawdź czy karet stoi na dopasowaniu — jeśli tak, zamień in-place
        var atMatch = content.Length >= caretOffset + _searchQuery.Length &&
                      content.Substring(caretOffset, _searchQuery.Length)
                             .Equals(_searchQuery, comparison);

        int replaceAt;
        if (atMatch)
        {
            replaceAt = caretOffset;
        }
        else
        {
            // Szukaj od bieżącej pozycji naprzód (z wrap-around)
            replaceAt = content.IndexOf(_searchQuery, caretOffset, comparison);
            if (replaceAt < 0)
                replaceAt = content.IndexOf(_searchQuery, 0, comparison);
        }

        if (replaceAt < 0) return 0;

        var newContent = content[..replaceAt] + _replaceQuery + content[(replaceAt + _searchQuery.Length)..];
        _suppressDirty = false;
        Content = newContent;

        // Przesuń karet za wstawiony tekst i idź do następnego
        var (line, col) = GetLineColumn(newContent, replaceAt + _replaceQuery.Length);
        CaretLine = line;
        CaretColumn = col;
        FindNext();

        return 1;
    }

    /// <summary>
    /// Zastępuje WSZYSTKIE wystąpienia <see cref="SearchQuery"/> wartością <see cref="ReplaceQuery"/>.
    /// </summary>
    /// <returns>Liczba wykonanych zamian.</returns>
    public int ReplaceAll()
    {
        if (string.IsNullOrEmpty(_searchQuery)) return 0;

        var comparison = _searchMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var content = _content;
        var result = new System.Text.StringBuilder(content.Length);
        int count = 0, pos = 0;

        while (pos <= content.Length)
        {
            var idx = content.IndexOf(_searchQuery, pos, comparison);
            if (idx < 0)
            {
                result.Append(content[pos..]);
                break;
            }
            result.Append(content[pos..idx]);
            result.Append(_replaceQuery);
            count++;
            pos = idx + _searchQuery.Length;
        }

        if (count > 0)
        {
            _suppressDirty = false;
            Content = result.ToString();
        }

        return count;
    }

    /// <summary>
    /// Zapisuje bieżącą treść do pliku.
    /// </summary>
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            return;
        }

        try
        {
            _workspace.WriteFile(FilePath, Content);
            IsDirty = false;

            _eventBus.Publish(new DocumentSaved(FilePath));
        }
        catch (Exception ex)
        {
            _output.Write("Editor", $"Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ustawia kursor na wskazanej linii i kolumnie (1-based).
    /// </summary>
    /// <param name="line">Numer linii (1-based).</param>
    /// <param name="column">Numer kolumny (1-based).</param>
    public void NavigateTo(int line, int column)
    {
        RequestedLine = Math.Max(1, line);
        RequestedColumn = Math.Max(1, column);
        NavigationRequestVersion++;
    }

    /// <summary>
    /// Żąda zamknięcia tej zakładki (emituje <see cref="CloseRequested"/>).
    /// </summary>
    public void RequestClose()
    {
        CloseRequested?.Invoke(this);
    }

    /// <summary>
    /// Reaguje na zewnętrzną zmianę pliku na dysku.
    /// Jeśli dokument jest dirty, oznacza go jako out-of-date. W przeciwnym razie przeładowuje z dysku.
    /// </summary>
    internal void HandleExternalChange()
    {
        if (IsDirty)
        {
            IsOutOfDate = true;
            _output.Write("Editor", $"File changed on disk while you have unsaved changes: {FilePath}");
            return;
        }

        TryReloadFromDisk(FilePath, "changed on disk");
    }

    /// <summary>
    /// Reaguje na zewnętrzne usunięcie pliku z dysku.
    /// </summary>
    internal void HandleExternalDelete()
    {
        IsOutOfDate = true;
        _output.Write("Editor", $"File deleted on disk: {FilePath}");
    }

    /// <summary>
    /// Reaguje na zewnętrzną zmianę nazwy pliku na dysku.
    /// </summary>
    /// <param name="newPath">Nowa ścieżka pliku.</param>
    internal void HandleExternalRename(string newPath)
    {
        if (IsDirty)
        {
            IsOutOfDate = true;
            _output.Write("Editor", $"File renamed on disk while you have unsaved changes: {FilePath} -> {newPath}");
            return;
        }

        _output.Write("Editor", $"File renamed on disk: {FilePath} -> {newPath}");
        TryReloadFromDisk(newPath, "renamed on disk", newPath);
    }

    private void TryReloadFromDisk(string path, string reason, string? newPath = null)
    {
        try
        {
            var content = _workspace.ReadFile(path);

            _suppressDirty = true;
            if (!string.IsNullOrWhiteSpace(newPath))
            {
                FilePath = newPath;
            }

            Content = content;
            IsDirty = false;
            IsOutOfDate = false;

            _output.Write("Editor", $"Reloaded ({reason}): {path}");
        }
        catch (Exception ex)
        {
            IsOutOfDate = true;
            _output.Write("Editor", $"Reload failed ({reason}): {ex.Message}");
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    // ─── Helpery wyszukiwania ────────────────────────────────────────────────

    /// <summary>
    /// Konwertuje pozycję (linia, kolumna, 1-based) na offset znakowy w tekście.
    /// </summary>
    private static int GetOffset(string content, int line, int column)
    {
        int currentLine = 1;
        int i = 0;
        while (i < content.Length && currentLine < line)
        {
            if (content[i] == '\n') currentLine++;
            i++;
        }
        return Math.Min(i + Math.Max(0, column - 1), content.Length);
    }

    /// <summary>
    /// Konwertuje offset znakowy na pozycję (linia, kolumna, 1-based).
    /// </summary>
    private static (int Line, int Column) GetLineColumn(string content, int offset)
    {
        int line = 1, lastLineStart = 0;
        for (int i = 0; i < Math.Min(offset, content.Length); i++)
        {
            if (content[i] == '\n')
            {
                line++;
                lastLineStart = i + 1;
            }
        }
        return (line, offset - lastLineStart + 1);
    }

    /// <summary>
    /// Zlicza wystąpienia <paramref name="query"/> w <paramref name="content"/>.
    /// </summary>
    private static int CountMatches(string content, string query, bool matchCase)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int count = 0, idx = 0;
        while ((idx = content.IndexOf(query, idx, comparison)) >= 0)
        {
            count++;
            idx += query.Length;
        }
        return count;
    }

    /// <summary>
    /// Szuka ostatniego wystąpienia <paramref name="query"/> kończącego się w pozycji &lt;= <paramref name="startIndex"/>.
    /// </summary>
    private static int LastIndexOf(string content, string query, int startIndex, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(query)) return -1;
        // .NET LastIndexOf(value, startIndex, count) szuka dopasowań KOŃCZĄCYCH SIĘ na pozycji ≤ startIndex.
        // Nasz parametr startIndex oznacza pozycję POCZĄTKU szukanego dopasowania.
        // Przeliczamy: end ≤ netStartIndex ⟹ netStartIndex = startIndex + query.Length - 1.
        int netStartIndex = Math.Min(startIndex + query.Length - 1, content.Length - 1);
        if (netStartIndex < query.Length - 1) return -1;
        int count = netStartIndex + 1;
        return content.LastIndexOf(query, netStartIndex, count, comparison);
    }
}
