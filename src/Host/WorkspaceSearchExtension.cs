using Junexis.Studio.Abstractions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Junexis.Studio.Host;

/// <summary>Pojedyncze trafienie tekstowe w pliku workspace.</summary>
public sealed record WorkspaceSearchMatch(
    string FullPath,
    string RelativePath,
    int Line,
    int Column,
    string Preview);

/// <summary>Żądanie globalnego wyszukiwania.</summary>
public sealed record WorkspaceSearchRequest(
    string WorkspaceRoot,
    string Query,
    bool MatchCase = false,
    string? IncludePattern = null,
    string? ExcludePattern = null,
    int MaxResults = 2000);

/// <summary>Podsumowanie zakończonego wyszukiwania.</summary>
public sealed record WorkspaceSearchResponse(
    IReadOnlyList<WorkspaceSearchMatch> Matches,
    int ScannedFiles,
    bool IsTruncated);

/// <summary>Grupa trafień jednego pliku prezentowana w panelu.</summary>
public sealed record WorkspaceSearchFileGroup(
    string FileName,
    string RelativePath,
    IReadOnlyList<WorkspaceSearchMatch> Matches)
{
    public int MatchCount => Matches.Count;
}

/// <summary>
/// Anulowalny, bezpieczny silnik tekstowego wyszukiwania lokalnego workspace.
/// </summary>
public sealed class WorkspaceTextSearchService
{
    private const long MaxFileSize = 2 * 1024 * 1024;
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".idea", ".junexis", "bin", "obj", "node_modules",
    };

    public async Task<WorkspaceSearchResponse> SearchAsync(
        WorkspaceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            return new WorkspaceSearchResponse([], 0, false);
        if (!Directory.Exists(request.WorkspaceRoot))
            throw new DirectoryNotFoundException($"Workspace '{request.WorkspaceRoot}' does not exist.");

        var root = Path.GetFullPath(request.WorkspaceRoot);
        var include = CompilePatterns(request.IncludePattern);
        var exclude = CompilePatterns(request.ExcludePattern);
        var comparison = request.MatchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var results = new List<WorkspaceSearchMatch>();
        var pending = new Stack<string>();
        var scannedFiles = 0;
        var truncated = false;
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            foreach (var child in SafeDirectories(directory))
            {
                var relative = Normalize(Path.GetRelativePath(root, child));
                if (!ShouldSkipDirectory(child, relative, exclude))
                    pending.Push(child);
            }

            foreach (var file in SafeFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Normalize(Path.GetRelativePath(root, file));
                if (!MatchesFilters(relative, include, exclude))
                    continue;
                if (!TryGetSearchableFile(file))
                    continue;

                scannedFiles++;
                var fileMatches = await SearchFileAsync(
                    file,
                    relative,
                    request.Query,
                    comparison,
                    request.MaxResults - results.Count,
                    cancellationToken).ConfigureAwait(false);
                results.AddRange(fileMatches);
                if (results.Count >= request.MaxResults)
                {
                    truncated = true;
                    break;
                }
            }

            if (truncated)
                break;
        }

        return new WorkspaceSearchResponse(results, scannedFiles, truncated);
    }

    private static async Task<IReadOnlyList<WorkspaceSearchMatch>> SearchFileAsync(
        string fullPath,
        string relativePath,
        string query,
        StringComparison comparison,
        int remaining,
        CancellationToken cancellationToken)
    {
        var matches = new List<WorkspaceSearchMatch>();
        try
        {
            using var reader = new StreamReader(fullPath, detectEncodingFromByteOrderMarks: true);
            var lineNumber = 0;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                    break;
                if (line.IndexOf('\0') >= 0)
                    return [];

                lineNumber++;
                var start = 0;
                while (remaining > 0)
                {
                    var index = line.IndexOf(query, start, comparison);
                    if (index < 0)
                        break;
                    matches.Add(new WorkspaceSearchMatch(
                        fullPath,
                        relativePath,
                        lineNumber,
                        index + 1,
                        CreatePreview(line)));
                    remaining--;
                    start = index + Math.Max(query.Length, 1);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return matches;
    }

    private static string CreatePreview(string line)
    {
        var preview = line.Trim();
        return preview.Length <= 240 ? preview : $"{preview[..237]}...";
    }

    private static bool TryGetSearchableFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileSize)
                return false;
            using var stream = File.OpenRead(path);
            Span<byte> buffer = stackalloc byte[4096];
            var read = stream.Read(buffer);
            return !buffer[..read].Contains((byte)0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool MatchesFilters(
        string relativePath,
        IReadOnlyList<Regex> include,
        IReadOnlyList<Regex> exclude)
    {
        if (exclude.Any(pattern => pattern.IsMatch(relativePath)))
            return false;
        if (include.Count == 0)
            return true;
        var fileName = Path.GetFileName(relativePath);
        return include.Any(pattern => pattern.IsMatch(relativePath) || pattern.IsMatch(fileName));
    }

    private static bool ShouldSkipDirectory(
        string fullPath,
        string relativePath,
        IReadOnlyList<Regex> exclude)
    {
        if (IgnoredDirectories.Contains(Path.GetFileName(fullPath)))
            return true;
        var withSlash = $"{relativePath}/";
        return exclude.Any(pattern => pattern.IsMatch(relativePath) || pattern.IsMatch(withSlash));
    }

    private static IReadOnlyList<Regex> CompilePatterns(string? patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns))
            return [];

        return patterns
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Select(pattern =>
            {
                var regex = Regex.Escape(pattern)
                    .Replace(@"\*\*", ".*", StringComparison.Ordinal)
                    .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
                    .Replace(@"\?", ".", StringComparison.Ordinal);
                if (!pattern.Contains('/'))
                    regex = $"(?:.*/)?{regex}";
                return new Regex($"^{regex}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            })
            .ToArray();
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim();

    private static IEnumerable<string> SafeFiles(string directory)
    {
        try { return Directory.EnumerateFiles(directory).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> SafeDirectories(string directory)
    {
        try { return Directory.EnumerateDirectories(directory).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}

/// <summary>View model panelu globalnego wyszukiwania.</summary>
public sealed class WorkspaceSearchViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly WorkspaceTextSearchService _search;
    private readonly IWorkspace _workspace;
    private readonly IEventBus _eventBus;
    private readonly IOutput _output;
    private CancellationTokenSource? _cancellation;
    private string _query = string.Empty;
    private string _includePattern = string.Empty;
    private string _excludePattern = ".git/**, bin/**, obj/**, node_modules/**, .junexis/**";
    private string _statusText = "Type a query to search the workspace.";
    private bool _matchCase;
    private bool _isBusy;
    private IReadOnlyList<WorkspaceSearchFileGroup> _results = [];

    public WorkspaceSearchViewModel(
        WorkspaceTextSearchService search,
        IWorkspace workspace,
        IEventBus eventBus,
        IOutput output)
    {
        _search = search;
        _workspace = workspace;
        _eventBus = eventBus;
        _output = output;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Query { get => _query; set => SetField(ref _query, value ?? string.Empty); }
    public string IncludePattern { get => _includePattern; set => SetField(ref _includePattern, value ?? string.Empty); }
    public string ExcludePattern { get => _excludePattern; set => SetField(ref _excludePattern, value ?? string.Empty); }
    public bool MatchCase { get => _matchCase; set => SetField(ref _matchCase, value); }
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public IReadOnlyList<WorkspaceSearchFileGroup> Results { get => _results; private set => SetField(ref _results, value); }

    public async Task SearchAsync()
    {
        if (IsBusy)
            return;
        Cancel();
        if (string.IsNullOrWhiteSpace(Query))
        {
            Results = [];
            StatusText = "Type a query to search the workspace.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_workspace.RootPath))
        {
            Results = [];
            StatusText = "Open a folder before searching.";
            return;
        }

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        IsBusy = true;
        StatusText = "Searching…";
        try
        {
            var response = await _search.SearchAsync(
                new WorkspaceSearchRequest(
                    _workspace.RootPath,
                    Query,
                    MatchCase,
                    IncludePattern,
                    ExcludePattern),
                token);
            Results = response.Matches
                .GroupBy(match => match.RelativePath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new WorkspaceSearchFileGroup(
                    Path.GetFileName(group.Key),
                    group.Key,
                    group.OrderBy(match => match.Line).ThenBy(match => match.Column).ToArray()))
                .ToArray();
            var suffix = response.IsTruncated ? " Result limit reached." : string.Empty;
            StatusText = $"{response.Matches.Count} matches in {Results.Count} files; scanned {response.ScannedFiles}.{suffix}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Search failed: {ex.Message}";
            _output.Write("Search", StatusText, OutputSeverity.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Cancel()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Navigate(WorkspaceSearchMatch? match)
    {
        if (match is not null)
            _eventBus.Publish(new NavigateToLocationRequested(match.FullPath, match.Line, match.Column));
    }

    public void Dispose() => Cancel();

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

internal sealed class WorkspaceSearchExtension : IJunexisExtension
{
    public const string PanelId = "junexis.workspace.search";
    private WorkspaceSearchViewModel? _viewModel;

    public string Id => PanelId;

    public void Initialize(IHostContext context)
    {
        _viewModel = new WorkspaceSearchViewModel(
            new WorkspaceTextSearchService(),
            context.Workspace,
            context.EventBus,
            context.Output);
        context.Panels.RegisterPanel(PanelId, PanelType.Tool, () => _viewModel, "Search", DockGroup.Left);
        context.Output.Write("Extensions", "WorkspaceSearchExtension loaded");
    }

    public void Shutdown()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}
