using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>
/// Zarządza procesami LSP, synchronizuje dokumenty i udostępnia funkcje językowe Studio.
/// </summary>
public sealed class LanguageService : ILanguageService
{
    private static readonly TimeSpan ChangeDelay = TimeSpan.FromMilliseconds(120);
    private readonly IEventBus _eventBus;
    private readonly IWorkspace _workspace;
    private readonly IDiagnosticService _diagnostics;
    private readonly IOutput _output;
    private readonly IReadOnlyList<LanguageServerProfile> _profiles;
    private readonly ConcurrentDictionary<string, LanguageServerSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, OpenLanguageDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Action<DocumentOpened> _openedHandler;
    private readonly Action<DocumentChanged> _changedHandler;
    private readonly Action<DocumentSaved> _savedHandler;
    private readonly Action<DocumentClosed> _closedHandler;
    private bool _disposed;

    /// <summary>
    /// Tworzy usługę z domyślnymi profilami Junexis, C/C++, Go oraz TypeScript/JavaScript.
    /// </summary>
    public LanguageService(
        IEventBus eventBus,
        IWorkspace workspace,
        IDiagnosticService diagnostics,
        IOutput output)
        : this(eventBus, workspace, diagnostics, output, LanguageServerProfileResolver.CreateDefaults())
    {
    }

    internal LanguageService(
        IEventBus eventBus,
        IWorkspace workspace,
        IDiagnosticService diagnostics,
        IOutput output,
        IReadOnlyList<LanguageServerProfile> profiles)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

        _openedHandler = OnDocumentOpened;
        _changedHandler = OnDocumentChanged;
        _savedHandler = OnDocumentSaved;
        _closedHandler = OnDocumentClosed;
        _eventBus.Subscribe(_openedHandler);
        _eventBus.Subscribe(_changedHandler);
        _eventBus.Subscribe(_savedHandler);
        _eventBus.Subscribe(_closedHandler);
    }

    /// <inheritdoc />
    public event Action<LanguageServerStatus>? StatusChanged;

    /// <inheritdoc />
    public event Action<LanguageDiagnosticsPublished>? DiagnosticsPublished;

    /// <inheritdoc />
    public event Action<LanguageReferencesPublished>? ReferencesPublished;

    /// <inheritdoc />
    public IReadOnlyList<LanguageServerStatus> GetStatuses() =>
        _profiles.Select(profile =>
                _sessions.TryGetValue(profile.Id, out var session)
                    ? session.Status
                    : new LanguageServerStatus(profile.Id, profile.DisplayName, LanguageServerState.Stopped, "Not started."))
            .ToArray();

    /// <inheritdoc />
    public bool Supports(string path) => FindProfile(path) is not null;

    /// <inheritdoc />
    public async Task<LanguageHover?> HoverAsync(
        string path,
        LanguagePosition position,
        CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync(path, "textDocument/hover", PositionParameters(path, position), cancellationToken)
            .ConfigureAwait(false);
        if (result is null || result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        var content = ReadHoverContent(result.Value);
        return string.IsNullOrWhiteSpace(content) ? null : new LanguageHover(content);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LanguageCompletionItem>> CompletionAsync(
        string path,
        LanguagePosition position,
        CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync(path, "textDocument/completion", PositionParameters(path, position), cancellationToken)
            .ConfigureAwait(false);
        if (result is null || result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];

        var items = result.Value.ValueKind == JsonValueKind.Array
            ? result.Value
            : result.Value.TryGetProperty("items", out var itemArray) ? itemArray : default;
        if (items.ValueKind != JsonValueKind.Array)
            return [];

        return items.EnumerateArray()
            .Select(item =>
            {
                var label = GetString(item, "label") ?? string.Empty;
                var insertText = GetString(item, "insertText")
                    ?? (item.TryGetProperty("textEdit", out var edit) ? GetString(edit, "newText") : null)
                    ?? label;
                return new LanguageCompletionItem(
                    label,
                    insertText,
                    GetString(item, "detail"),
                    item.TryGetProperty("kind", out var kind) ? kind.ToString() : null);
            })
            .Where(item => item.Label.Length > 0)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<LanguageLocation?> DefinitionAsync(
        string path,
        LanguagePosition position,
        CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync(path, "textDocument/definition", PositionParameters(path, position), cancellationToken)
            .ConfigureAwait(false);
        return result is null ? null : ReadLocations(result.Value).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LanguageLocation>> ReferencesAsync(
        string path,
        LanguagePosition position,
        CancellationToken cancellationToken = default)
    {
        var parameters = new
        {
            textDocument = new { uri = ToUri(path) },
            position = new { line = position.Line, character = position.Character },
            context = new { includeDeclaration = true },
        };
        var result = await RequestAsync(path, "textDocument/references", parameters, cancellationToken)
            .ConfigureAwait(false);
        var locations = result is null ? [] : ReadLocations(result.Value);
        var profile = FindProfile(path);
        if (profile is not null)
        {
            ReferencesPublished?.Invoke(new LanguageReferencesPublished(
                profile.Id,
                path,
                ReadSymbol(path, position),
                locations));
        }
        return locations;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LanguageSemanticToken>> SemanticTokensAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var profile = FindProfile(path);
        if (profile is null)
            return [];
        var session = await GetSessionAsync(profile, cancellationToken).ConfigureAwait(false);
        var result = await session.RequestAsync(
            "textDocument/semanticTokens/full",
            new { textDocument = new { uri = ToUri(path) } },
            cancellationToken).ConfigureAwait(false);
        if (result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            || !result.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
            return [];
        return DecodeSemanticTokens(data, session.SemanticTokenTypes);
    }

    /// <inheritdoc />
    public async Task<int> RenameAsync(
        string path,
        LanguagePosition position,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var result = await RequestAsync(path, "textDocument/rename", new
        {
            textDocument = new { uri = ToUri(path) },
            position = new { line = position.Line, character = position.Character },
            newName,
        }, cancellationToken).ConfigureAwait(false);
        var edits = result is null ? [] : ReadWorkspaceEdits(result.Value);
        return await ApplyEditsAsync(edits, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LanguageCodeAction>> CodeActionsAsync(
        string path,
        LanguagePosition position,
        CancellationToken cancellationToken = default)
    {
        var lspDiagnostics = _diagnostics.GetDiagnostics()
            .Where(item => item.FilePath is not null
                && Path.GetFullPath(item.FilePath).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
                && item.Line == position.Line + 1)
            .Select(item => new
            {
                range = new
                {
                    start = new { line = Math.Max(0, (item.Line ?? 1) - 1), character = Math.Max(0, (item.Column ?? 1) - 1) },
                    end = new { line = Math.Max(0, (item.Line ?? 1) - 1), character = Math.Max(0, item.Column ?? 1) },
                },
                severity = item.Severity switch
                {
                    DiagnosticSeverity.Error => 1,
                    DiagnosticSeverity.Warning => 2,
                    _ => 3,
                },
                message = item.Message,
                source = item.Source,
            }).ToArray();
        var result = await RequestAsync(path, "textDocument/codeAction", new
        {
            textDocument = new { uri = ToUri(path) },
            range = new
            {
                start = new { line = position.Line, character = position.Character },
                end = new { line = position.Line, character = position.Character },
            },
            context = new { diagnostics = lspDiagnostics },
        }, cancellationToken).ConfigureAwait(false);
        if (result is null || result.Value.ValueKind != JsonValueKind.Array)
            return [];
        return result.Value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("title", out _))
            .Select(item => new LanguageCodeAction(
                GetString(item, "title") ?? "Code action",
                GetString(item, "kind"),
                item.TryGetProperty("isPreferred", out var preferred) && preferred.ValueKind == JsonValueKind.True,
                item.TryGetProperty("edit", out var edit) ? ReadWorkspaceEdits(edit) : []))
            .Where(action => action.Edits.Count > 0)
            .ToArray();
    }

    /// <inheritdoc />
    public Task<int> ApplyCodeActionAsync(
        LanguageCodeAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ApplyEditsAsync(action.Edits, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RestartAsync(string path, CancellationToken cancellationToken = default)
    {
        var profile = FindProfile(path)
            ?? throw new NotSupportedException($"No language server profile for '{Path.GetExtension(path)}'.");
        await RestartProfileCoreAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task RestartProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var profile = _profiles.FirstOrDefault(item =>
            item.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"Unknown language server profile '{profileId}'.");
        return RestartProfileCoreAsync(profile, cancellationToken);
    }

    private async Task RestartProfileCoreAsync(
        LanguageServerProfile profile,
        CancellationToken cancellationToken)
    {
        if (_sessions.TryRemove(profile.Id, out var existing))
            await existing.DisposeAsync().ConfigureAwait(false);
        var session = await GetSessionAsync(profile, cancellationToken).ConfigureAwait(false);
        foreach (var document in _documents.Values.Where(item =>
                     item.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
            await session.OpenAsync(document.Path, document.Content, document.Version, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _eventBus.Unsubscribe(_openedHandler);
        _eventBus.Unsubscribe(_changedHandler);
        _eventBus.Unsubscribe(_savedHandler);
        _eventBus.Unsubscribe(_closedHandler);
        _lifetime.Cancel();

        foreach (var change in _pendingChanges.Values)
        {
            change.Cancel();
            change.Dispose();
        }
        _pendingChanges.Clear();

        foreach (var session in _sessions.Values)
            await session.DisposeAsync().ConfigureAwait(false);
        _sessions.Clear();
        _lifetime.Dispose();
    }

    private void OnDocumentOpened(DocumentOpened evt)
    {
        var profile = FindProfile(evt.Path);
        if (profile is null)
            return;
        _documents[evt.Path] = new OpenLanguageDocument(evt.Path, evt.Content ?? string.Empty, profile.Id, 1);
        _ = ExecuteSafelyAsync("open", evt.Path, async token =>
        {
            var session = await GetSessionAsync(profile, token).ConfigureAwait(false);
            await session.OpenAsync(evt.Path, evt.Content ?? string.Empty, 1, token).ConfigureAwait(false);
        });
    }

    private void OnDocumentChanged(DocumentChanged evt)
    {
        var profile = FindProfile(evt.Path);
        if (profile is null)
            return;
        var document = _documents.AddOrUpdate(
            evt.Path,
            _ => new OpenLanguageDocument(evt.Path, evt.Content ?? string.Empty, profile.Id, 1),
            (_, current) => current with { Content = evt.Content ?? string.Empty, Version = current.Version + 1 });

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        if (_pendingChanges.TryGetValue(evt.Path, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }
        _pendingChanges[evt.Path] = cancellation;
        _ = SendDelayedChangeAsync(document, profile, cancellation);
    }

    private async Task SendDelayedChangeAsync(
        OpenLanguageDocument document,
        LanguageServerProfile profile,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(ChangeDelay, cancellation.Token).ConfigureAwait(false);
            var session = await GetSessionAsync(profile, cancellation.Token).ConfigureAwait(false);
            await session.ChangeAsync(document.Path, document.Content, document.Version, cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ReportFailure("change", document.Path, ex);
        }
        finally
        {
            if (_pendingChanges.TryGetValue(document.Path, out var current) && ReferenceEquals(current, cancellation))
                _pendingChanges.TryRemove(document.Path, out _);
            cancellation.Dispose();
        }
    }

    private void OnDocumentSaved(DocumentSaved evt)
    {
        var profile = FindProfile(evt.Path);
        if (profile is null)
            return;
        _ = ExecuteSafelyAsync("save", evt.Path, async token =>
        {
            var session = await GetSessionAsync(profile, token).ConfigureAwait(false);
            await session.SaveAsync(evt.Path, token).ConfigureAwait(false);
        });
    }

    private void OnDocumentClosed(DocumentClosed evt)
    {
        _documents.TryRemove(evt.Path, out var document);
        if (_pendingChanges.TryRemove(evt.Path, out var pending))
        {
            pending.Cancel();
            pending.Dispose();
        }
        if (document is null || !_sessions.TryGetValue(document.ProfileId, out var session))
            return;
        _ = ExecuteSafelyAsync("close", evt.Path, token => session.CloseAsync(evt.Path, token));
        ClearDiagnostics(document.ProfileId, evt.Path);
    }

    private async Task<JsonElement?> RequestAsync(
        string path,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var profile = FindProfile(path);
        if (profile is null)
            return null;
        var session = await GetSessionAsync(profile, cancellationToken).ConfigureAwait(false);
        return await session.RequestAsync(method, parameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LanguageServerSession> GetSessionAsync(
        LanguageServerProfile profile,
        CancellationToken cancellationToken)
    {
        var session = _sessions.GetOrAdd(profile.Id, _ => CreateSession(profile));
        await session.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    private LanguageServerSession CreateSession(LanguageServerProfile profile)
    {
        var session = new LanguageServerSession(profile, _workspace, _output);
        session.StatusChanged += status => StatusChanged?.Invoke(status);
        session.DiagnosticsPublished += OnDiagnosticsPublished;
        session.RestartDocumentsProvider = () =>
            _documents.Values.Where(document => document.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        return session;
    }

    private void OnDiagnosticsPublished(string profileId, string uri, JsonElement diagnostics)
    {
        var path = FromUri(uri);
        var source = DiagnosticSource(profileId, path);
        _diagnostics.ClearBySource(source);
        var mapped = diagnostics.ValueKind == JsonValueKind.Array
            ? diagnostics.EnumerateArray().Select(item => MapDiagnostic(item, path, source)).ToArray()
            : [];
        foreach (var diagnostic in mapped)
            _diagnostics.Add(diagnostic);
        DiagnosticsPublished?.Invoke(new LanguageDiagnosticsPublished(profileId, path, mapped));
    }

    private void ClearDiagnostics(string profileId, string path) =>
        _diagnostics.ClearBySource(DiagnosticSource(profileId, path));

    private static Diagnostic MapDiagnostic(JsonElement item, string path, string source)
    {
        var severity = item.TryGetProperty("severity", out var severityElement) && severityElement.TryGetInt32(out var value)
            ? value switch
            {
                1 => DiagnosticSeverity.Error,
                2 => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Info,
            }
            : DiagnosticSeverity.Info;
        int? line = null;
        int? column = null;
        if (item.TryGetProperty("range", out var range)
            && range.TryGetProperty("start", out var start))
        {
            if (start.TryGetProperty("line", out var lineElement))
                line = lineElement.GetInt32() + 1;
            if (start.TryGetProperty("character", out var characterElement))
                column = characterElement.GetInt32() + 1;
        }
        return new Diagnostic(
            severity,
            GetString(item, "message") ?? "Language server diagnostic.",
            path,
            line,
            column,
            source);
    }

    private async Task ExecuteSafelyAsync(
        string operation,
        string path,
        Func<CancellationToken, Task> action)
    {
        try
        {
            await action(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ReportFailure(operation, path, ex);
        }
    }

    private void ReportFailure(string operation, string path, Exception ex) =>
        _output.Write(
            "Language Services",
            $"{operation} failed for {Path.GetFileName(path)}: {ex.Message}",
            OutputSeverity.Warning,
            "LSP",
            ex.ToString());

    private LanguageServerProfile? FindProfile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var extension = Path.GetExtension(path);
        return _profiles.FirstOrDefault(profile =>
            profile.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    private string? ReadSymbol(string path, LanguagePosition position)
    {
        if (!_documents.TryGetValue(path, out var document))
            return null;
        var lines = document.Content.ReplaceLineEndings("\n").Split('\n');
        if (position.Line < 0 || position.Line >= lines.Length)
            return null;
        var line = lines[position.Line];
        var character = Math.Clamp(position.Character, 0, line.Length);
        var start = character;
        while (start > 0 && IsSymbolCharacter(line[start - 1]))
            start--;
        var end = character;
        while (end < line.Length && IsSymbolCharacter(line[end]))
            end++;
        return start < end ? line[start..end] : null;
    }

    private static bool IsSymbolCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '$';

    private static object PositionParameters(string path, LanguagePosition position) => new
    {
        textDocument = new { uri = ToUri(path) },
        position = new { line = Math.Max(0, position.Line), character = Math.Max(0, position.Character) },
    };

    private static string ReadHoverContent(JsonElement hover)
    {
        if (!hover.TryGetProperty("contents", out var contents))
            return string.Empty;
        if (contents.ValueKind == JsonValueKind.String)
            return contents.GetString() ?? string.Empty;
        if (contents.ValueKind == JsonValueKind.Object)
            return GetString(contents, "value") ?? GetString(contents, "language") ?? string.Empty;
        if (contents.ValueKind == JsonValueKind.Array)
            return string.Join(Environment.NewLine, contents.EnumerateArray().Select(item =>
                item.ValueKind == JsonValueKind.String ? item.GetString() : GetString(item, "value"))
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.Empty;
    }

    private static IReadOnlyList<LanguageLocation> ReadLocations(JsonElement result)
    {
        if (result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        IEnumerable<JsonElement> values = result.ValueKind == JsonValueKind.Array
            ? result.EnumerateArray().ToArray()
            : [result];
        var locations = new List<LanguageLocation>();
        foreach (var item in values)
        {
            var uri = GetString(item, "uri") ?? GetString(item, "targetUri");
            var rangeName = item.TryGetProperty("range", out var range) ? "range" : "targetSelectionRange";
            if (uri is null || !item.TryGetProperty(rangeName, out range)
                || !range.TryGetProperty("start", out var start))
                continue;
            locations.Add(new LanguageLocation(
                FromUri(uri),
                new LanguagePosition(
                    start.TryGetProperty("line", out var line) ? line.GetInt32() : 0,
                    start.TryGetProperty("character", out var character) ? character.GetInt32() : 0)));
        }
        return locations;
    }

    private static IReadOnlyList<LanguageSemanticToken> DecodeSemanticTokens(
        JsonElement data,
        IReadOnlyList<string> legend)
    {
        var values = data.EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var tokens = new List<LanguageSemanticToken>(values.Length / 5);
        var line = 0;
        var character = 0;
        for (var index = 0; index + 4 < values.Length; index += 5)
        {
            line += values[index];
            character = values[index] == 0 ? character + values[index + 1] : values[index + 1];
            var typeIndex = values[index + 3];
            var type = typeIndex >= 0 && typeIndex < legend.Count ? legend[typeIndex] : $"type{typeIndex}";
            tokens.Add(new LanguageSemanticToken(line, character, values[index + 2], type));
        }
        return tokens;
    }

    private static IReadOnlyList<LanguageTextEdit> ReadWorkspaceEdits(JsonElement workspaceEdit)
    {
        var edits = new List<LanguageTextEdit>();
        if (workspaceEdit.ValueKind != JsonValueKind.Object)
            return edits;
        if (workspaceEdit.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in changes.EnumerateObject())
                ReadTextEdits(property.Name, property.Value, edits);
        }
        if (workspaceEdit.TryGetProperty("documentChanges", out var documentChanges)
            && documentChanges.ValueKind == JsonValueKind.Array)
        {
            foreach (var change in documentChanges.EnumerateArray())
            {
                if (!change.TryGetProperty("textDocument", out var document)
                    || GetString(document, "uri") is not { } uri
                    || !change.TryGetProperty("edits", out var documentEdits))
                    continue;
                ReadTextEdits(uri, documentEdits, edits);
            }
        }
        return edits;
    }

    private static void ReadTextEdits(string uri, JsonElement values, List<LanguageTextEdit> target)
    {
        if (values.ValueKind != JsonValueKind.Array)
            return;
        foreach (var item in values.EnumerateArray())
        {
            if (!item.TryGetProperty("range", out var range)
                || !range.TryGetProperty("start", out var start)
                || !range.TryGetProperty("end", out var end))
                continue;
            target.Add(new LanguageTextEdit(
                FromUri(uri),
                ReadPosition(start),
                ReadPosition(end),
                GetString(item, "newText") ?? string.Empty));
        }
    }

    private async Task<int> ApplyEditsAsync(
        IReadOnlyList<LanguageTextEdit> edits,
        CancellationToken cancellationToken)
    {
        var applied = 0;
        foreach (var group in edits.GroupBy(edit => Path.GetFullPath(edit.Path), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = _documents.TryGetValue(group.Key, out var open)
                ? open.Content
                : _workspace.ReadFile(group.Key);
            var updated = ApplyTextEdits(content, group);
            if (open is not null)
                _eventBus.Publish(new ReplaceOpenDocumentContentRequested(group.Key, updated));
            else
                _workspace.WriteFile(group.Key, updated);
            applied += group.Count();
        }
        await Task.CompletedTask;
        return applied;
    }

    internal static string ApplyTextEdits(string content, IEnumerable<LanguageTextEdit> edits)
    {
        var resolved = edits.Select(edit => (
                Edit: edit,
                Start: PositionToOffset(content, edit.Start),
                End: PositionToOffset(content, edit.End)))
            .OrderByDescending(item => item.Start)
            .ToArray();
        var result = content;
        foreach (var item in resolved)
            result = result.Remove(item.Start, Math.Max(0, item.End - item.Start))
                .Insert(item.Start, item.Edit.NewText);
        return result;
    }

    private static int PositionToOffset(string content, LanguagePosition position)
    {
        var offset = 0;
        var line = 0;
        while (line < Math.Max(0, position.Line) && offset < content.Length)
        {
            var newline = content.IndexOf('\n', offset);
            if (newline < 0)
            {
                offset = content.Length;
                break;
            }
            offset = newline + 1;
            line++;
        }

        var lineEnd = content.IndexOf('\n', offset);
        if (lineEnd < 0)
            lineEnd = content.Length;
        else if (lineEnd > offset && content[lineEnd - 1] == '\r')
            lineEnd--;
        return offset + Math.Clamp(position.Character, 0, lineEnd - offset);
    }

    private static LanguagePosition ReadPosition(JsonElement position) =>
        new(
            position.TryGetProperty("line", out var line) ? line.GetInt32() : 0,
            position.TryGetProperty("character", out var character) ? character.GetInt32() : 0);

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static string ToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    internal static string FromUri(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile
            ? parsed.LocalPath
            : uri;

    private static string DiagnosticSource(string profileId, string path) =>
        $"LSP:{profileId}:{Path.GetFullPath(path)}";
}

internal sealed record LanguageServerProfile(
    string Id,
    string DisplayName,
    string LanguageId,
    IReadOnlyList<string> Extensions,
    string FileName,
    IReadOnlyList<string> Arguments);

internal sealed record OpenLanguageDocument(
    string Path,
    string Content,
    string ProfileId,
    int Version);

internal static class LanguageServerProfileResolver
{
    public static IReadOnlyList<LanguageServerProfile> CreateDefaults()
    {
        var junexis = ResolveJunexis();
        return
        [
            new("junexis", "Junexis Language Server", "junexis", [".jnx"],
                junexis.FileName, junexis.Arguments),
            FromEnvironment("cpp", "clangd", "C/C++ Language Server", "cpp",
                [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"],
                "JUNEXIS_CLANGD", "clangd", ["--background-index"]),
            FromEnvironment("go", "gopls", "Go Language Server", "go",
                [".go"], "JUNEXIS_GOPLS", "gopls", []),
            FromEnvironment("typescript", "typescript-language-server", "TypeScript Language Server", "typescript",
                [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs"],
                "JUNEXIS_TYPESCRIPT_LSP", "typescript-language-server", ["--stdio"]),
        ];
    }

    private static LanguageServerProfile FromEnvironment(
        string id,
        string fallback,
        string displayName,
        string languageId,
        IReadOnlyList<string> extensions,
        string environmentVariable,
        string defaultCommand,
        IReadOnlyList<string> arguments)
    {
        var command = Environment.GetEnvironmentVariable(environmentVariable);
        return new LanguageServerProfile(
            id,
            displayName,
            languageId,
            extensions,
            string.IsNullOrWhiteSpace(command) ? defaultCommand : command,
            arguments);
    }

    private static (string FileName, IReadOnlyList<string> Arguments) ResolveJunexis()
    {
        var configured = Environment.GetEnvironmentVariable("JUNEXIS_LSP");
        if (!string.IsNullOrWhiteSpace(configured))
            return (configured, []);

        var installed = InstalledJunexisLspPath(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "linux",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (File.Exists(installed))
            return (installed, []);

        foreach (var root in CandidateRoots())
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var candidate = Path.Combine(
                root,
                "junexis-lang",
                "compiler",
                "JunexisLang.Compiler.LspHost",
                "bin",
                configuration,
                "net10.0",
                "JunexisLang.Compiler.LspHost.dll");
            if (File.Exists(candidate))
                return ("dotnet", [candidate]);
        }
        return ("junexis", ["lsp"]);
    }

    internal static string InstalledJunexisLspPath(
        string platform,
        string userProfile,
        string? localApplicationData)
    {
        var directory = platform switch
        {
            "windows" when !string.IsNullOrWhiteSpace(localApplicationData) =>
                Path.Combine(localApplicationData, "Junexis", "lsp"),
            "macos" => Path.Combine(userProfile, "Library", "Application Support", "Junexis", "lsp"),
            _ => Path.Combine(userProfile, ".local", "share", "junexis", "lsp"),
        };
        var executable = platform == "windows"
            ? "JunexisLang.Compiler.LspHost.exe"
            : "JunexisLang.Compiler.LspHost";
        return Path.Combine(directory, executable);
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        for (var depth = 0; current is not null && depth < 7; depth++, current = current.Parent)
            yield return current.FullName;
    }
}

internal sealed class LanguageServerSession : IAsyncDisposable
{
    private const int MaximumAutomaticRestarts = 2;
    private readonly LanguageServerProfile _profile;
    private readonly IWorkspace _workspace;
    private readonly IOutput _output;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Process? _process;
    private LspProtocolClient? _client;
    private bool _stopping;
    private int _restartCount;
    private int _recovering;

    public LanguageServerSession(LanguageServerProfile profile, IWorkspace workspace, IOutput output)
    {
        _profile = profile;
        _workspace = workspace;
        _output = output;
        Status = new LanguageServerStatus(profile.Id, profile.DisplayName, LanguageServerState.Stopped, "Not started.");
    }

    public event Action<LanguageServerStatus>? StatusChanged;

    public event Action<string, string, JsonElement>? DiagnosticsPublished;

    public Func<IReadOnlyList<OpenLanguageDocument>>? RestartDocumentsProvider { get; set; }

    public LanguageServerStatus Status { get; private set; }

    public IReadOnlyList<string> SemanticTokenTypes { get; private set; } = [];

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_client is not null && Status.State == LanguageServerState.Running)
            return;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null && Status.State == LanguageServerState.Running)
                return;
            await StartCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken cancellationToken) =>
        (_client ?? throw new InvalidOperationException($"{_profile.DisplayName} is not running."))
            .SendRequestAsync(method, parameters, cancellationToken);

    public Task OpenAsync(string path, string content, int version, CancellationToken cancellationToken) =>
        NotifyAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri = LanguageService.ToUri(path),
                languageId = LanguageIdForPath(path),
                version,
                text = content,
            },
        }, cancellationToken);

    public Task ChangeAsync(string path, string content, int version, CancellationToken cancellationToken) =>
        NotifyAsync("textDocument/didChange", new
        {
            textDocument = new { uri = LanguageService.ToUri(path), version },
            contentChanges = new[] { new { text = content } },
        }, cancellationToken);

    public Task SaveAsync(string path, CancellationToken cancellationToken) =>
        NotifyAsync("textDocument/didSave", new { textDocument = new { uri = LanguageService.ToUri(path) } }, cancellationToken);

    public Task CloseAsync(string path, CancellationToken cancellationToken) =>
        NotifyAsync("textDocument/didClose", new { textDocument = new { uri = LanguageService.ToUri(path) } }, cancellationToken);

    private string LanguageIdForPath(string path)
    {
        if (!_profile.Id.Equals("typescript", StringComparison.OrdinalIgnoreCase))
            return _profile.LanguageId;
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".js" or ".mjs" or ".cjs" => "javascript",
            ".jsx" => "javascriptreact",
            ".tsx" => "typescriptreact",
            _ => "typescript",
        };
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        var client = _client;
        _client = null;
        if (client is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.SendRequestAsync("shutdown", null, timeout.Token).ConfigureAwait(false);
                await client.SendNotificationAsync("exit", null, timeout.Token).ConfigureAwait(false);
            }
            catch
            {
            }
            await client.DisposeAsync().ConfigureAwait(false);
        }
        StopProcess();
        SetStatus(LanguageServerState.Stopped, "Stopped.");
        _lifecycle.Dispose();
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        SetStatus(LanguageServerState.Starting, $"Starting {_profile.FileName}...");
        _stopping = false;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _profile.FileName,
                WorkingDirectory = _workspace.RootPath ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            };
            foreach (var argument in _profile.Arguments)
                startInfo.ArgumentList.Add(argument);

            _process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start {_profile.FileName}.");
            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;
            _ = ReadStderrAsync(_process);

            _process.StandardInput.AutoFlush = true;
            _client = new LspProtocolClient(
                new TextReaderInputStream(_process.StandardOutput),
                new TextWriterOutputStream(_process.StandardInput));
            _client.NotificationReceived += OnNotification;
            _client.Closed += OnClientClosed;

            var rootPath = _workspace.RootPath ?? Environment.CurrentDirectory;
            var initializeResult = await _client.SendRequestAsync("initialize", new
            {
                processId = Environment.ProcessId,
                clientInfo = new { name = "Junexis Studio", version = "0.1" },
                rootUri = LanguageService.ToUri(rootPath),
                capabilities = new
                {
                    textDocument = new
                    {
                        semanticTokens = new
                        {
                            requests = new { range = false, full = true },
                            tokenTypes = new[]
                            {
                                "namespace", "type", "class", "enum", "interface", "struct",
                                "typeParameter", "parameter", "variable", "property", "enumMember",
                                "event", "function", "method", "macro", "keyword", "modifier",
                                "comment", "string", "number", "regexp", "operator", "decorator",
                            },
                            tokenModifiers = Array.Empty<string>(),
                            formats = new[] { "relative" },
                            overlappingTokenSupport = false,
                            multilineTokenSupport = true,
                        },
                    },
                },
            }, cancellationToken).ConfigureAwait(false);
            SemanticTokenTypes = ReadSemanticTokenLegend(initializeResult);
            await _client.SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
            SetStatus(LanguageServerState.Running, "Ready.");

        }
        catch (Exception ex)
        {
            SetStatus(LanguageServerState.Failed, ex.Message);
            StopProcess();
            throw;
        }
    }

    private Task NotifyAsync(string method, object parameters, CancellationToken cancellationToken) =>
        (_client ?? throw new InvalidOperationException($"{_profile.DisplayName} is not running."))
            .SendNotificationAsync(method, parameters, cancellationToken);

    private void OnNotification(LspNotification notification)
    {
        if (notification.Method == "textDocument/publishDiagnostics"
            && notification.Parameters.TryGetProperty("uri", out var uri)
            && notification.Parameters.TryGetProperty("diagnostics", out var diagnostics))
        {
            DiagnosticsPublished?.Invoke(_profile.Id, uri.GetString() ?? string.Empty, diagnostics.Clone());
            return;
        }

        if (notification.Method is "window/logMessage" or "window/showMessage"
            && notification.Parameters.TryGetProperty("message", out var message))
            _output.Write("Language Services", $"[{_profile.Id}] {message.GetString()}");
    }

    private void OnClientClosed(Exception? error)
    {
        if (_stopping)
            return;
        if (Status.State == LanguageServerState.Starting)
        {
            SetStatus(LanguageServerState.Failed, error?.Message ?? "Protocol stream closed during initialization.");
            StopProcess();
            return;
        }
        _ = RestartAfterFailureAsync(error);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_stopping)
            return;
        _ = RestartAfterFailureAsync(new EndOfStreamException(
            $"{_profile.DisplayName} exited with code {_process?.ExitCode}."));
    }

    private async Task RestartAfterFailureAsync(Exception? error)
    {
        if (Interlocked.Exchange(ref _recovering, 1) != 0)
            return;
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopping || Status.State == LanguageServerState.Starting)
                return;
            _client = null;
            StopProcess();
            if (_restartCount >= MaximumAutomaticRestarts)
            {
                SetStatus(LanguageServerState.Failed, error?.Message ?? "Server stopped unexpectedly.");
                return;
            }
            _restartCount++;
            SetStatus(LanguageServerState.Starting, $"Restarting after failure ({_restartCount}/{MaximumAutomaticRestarts})...");
            await Task.Delay(TimeSpan.FromMilliseconds(250 * _restartCount)).ConfigureAwait(false);
            await StartCoreAsync(CancellationToken.None).ConfigureAwait(false);
            var documents = RestartDocumentsProvider?.Invoke() ?? [];
            foreach (var document in documents)
                await OpenAsync(document.Path, document.Content, document.Version, CancellationToken.None)
                    .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetStatus(LanguageServerState.Failed, ex.Message);
        }
        finally
        {
            _lifecycle.Release();
            Volatile.Write(ref _recovering, 0);
        }
    }

    private async Task ReadStderrAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    _output.Write("Language Services", $"[{_profile.Id}] {line}", OutputSeverity.Info, "LSP");
            }
        }
        catch (Exception) when (_stopping)
        {
        }
    }

    private void StopProcess()
    {
        var process = _process;
        _process = null;
        if (process is null)
            return;
        process.Exited -= OnProcessExited;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        process.Dispose();
    }

    private void SetStatus(LanguageServerState state, string message)
    {
        Status = new LanguageServerStatus(_profile.Id, _profile.DisplayName, state, message, _restartCount);
        StatusChanged?.Invoke(Status);
        _output.Write("Language Services", $"[{_profile.Id}] {state}: {message}");
    }

    private static IReadOnlyList<string> ReadSemanticTokenLegend(JsonElement initializeResult)
    {
        if (!initializeResult.TryGetProperty("capabilities", out var capabilities)
            || !capabilities.TryGetProperty("semanticTokensProvider", out var provider)
            || !provider.TryGetProperty("legend", out var legend)
            || !legend.TryGetProperty("tokenTypes", out var tokenTypes)
            || tokenTypes.ValueKind != JsonValueKind.Array)
            return [];
        return tokenTypes.EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }
}
