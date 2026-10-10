using System.Text;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>Default safety boundary for deterministic Mentor source refactorings.</summary>
public sealed class MentorRefactoringService : IMentorRefactoringService
{
    private readonly IWorkspace _workspace;
    private readonly IOpenDocumentRepository _openDocuments;
    private readonly ICompilerRegistry _compilers;
    private readonly IOutput _output;
    private readonly IEventBus _eventBus;

    /// <summary>Creates the refactoring service.</summary>
    public MentorRefactoringService(
        IWorkspace workspace,
        IOpenDocumentRepository openDocuments,
        ICompilerRegistry compilers,
        IOutput output,
        IEventBus eventBus)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _openDocuments = openDocuments ?? throw new ArgumentNullException(nameof(openDocuments));
        _compilers = compilers ?? throw new ArgumentNullException(nameof(compilers));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _output.RegisterChannel("Mentor");
    }

    /// <inheritdoc />
    public MentorRefactoringProposal CreateProposal(
        AnalysisSnapshot snapshot,
        MentorFinding finding)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(finding);

        if (string.Equals(finding.Code, "M1012", StringComparison.Ordinal))
            return CreateM1012Proposal(snapshot, finding);
        if (!string.Equals(finding.Code, "M1002", StringComparison.Ordinal))
            throw new NotSupportedException($"Mentor rule '{finding.Code}' has no safe refactoring.");
        if (string.IsNullOrWhiteSpace(finding.RelatedSymbolId))
            throw new InvalidOperationException("M1002 finding does not identify a stable pipeline symbol.");

        var pipeline = snapshot.Symbols.SingleOrDefault(symbol =>
            string.Equals(symbol.Id, finding.RelatedSymbolId, StringComparison.Ordinal)
            && string.Equals(symbol.Kind, "pipeline", StringComparison.OrdinalIgnoreCase));
        if (pipeline is null || pipeline.Source is null)
            throw new InvalidOperationException("The affected pipeline has no exact compiler source location.");

        var filePath = ResolveWorkspacePath(pipeline.Source.File);
        var original = _workspace.ReadFile(filePath);
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var escapedName = pipeline.Name.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        var block = string.Join(newline,
            $"narrative \"{escapedName} intent\" {{",
            $"    intent \"TODO: describe the system intent served by {escapedName}\"",
            $"    applies_to {{ {pipeline.Name} }}",
            "}");
        var separator = original.Length == 0
            ? string.Empty
            : original.EndsWith("\n", StringComparison.Ordinal)
                ? newline
                : newline + newline;
        var updated = original + separator + block + newline;
        var addedLines = (separator + block + newline)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        if (addedLines.Length > 0 && addedLines[^1].Length == 0)
            addedLines = addedLines[..^1];
        var oldLineCount = CountLines(original);
        var relativePath = Path.GetRelativePath(_workspace.RootPath!, filePath).Replace('\\', '/');
        var patch = new StringBuilder()
            .AppendLine($"--- a/{relativePath}")
            .AppendLine($"+++ b/{relativePath}")
            .AppendLine($"@@ -{oldLineCount},0 +{oldLineCount + 1},{addedLines.Length} @@")
            .AppendJoin(Environment.NewLine, addedLines.Select(line => $"+{line}"))
            .ToString();

        return new MentorRefactoringProposal(
            $"mentor:M1002:{pipeline.Id}",
            finding.Code,
            $"Add Narrative boundary for pipeline {pipeline.Name}",
            filePath,
            pipeline.Id,
            original,
            updated,
            patch,
            [
                "The generated intent is an explicit TODO and must be completed by the system author.",
                "The proposal only binds the selected pipeline; it does not invent stakeholders, compliance, or degraded semantics."
            ],
            $"The pipeline '{pipeline.Name}' becomes explicitly covered by Narrative applies_to.");
    }

    private MentorRefactoringProposal CreateM1012Proposal(
        AnalysisSnapshot snapshot,
        MentorFinding finding)
    {
        if (string.IsNullOrWhiteSpace(finding.RelatedSymbolId))
            throw new InvalidOperationException("M1012 finding does not identify a stable Narrative symbol.");

        var narrative = snapshot.Symbols.SingleOrDefault(symbol =>
            string.Equals(symbol.Id, finding.RelatedSymbolId, StringComparison.Ordinal)
            && string.Equals(symbol.Kind, "narrative", StringComparison.OrdinalIgnoreCase));
        if (narrative is null || narrative.Source is null)
            throw new InvalidOperationException("The affected Narrative has no exact compiler source location.");
        var pipelines = snapshot.Symbols
            .Where(symbol => string.Equals(symbol.Kind, "pipeline", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (pipelines.Length != 1)
        {
            throw new InvalidOperationException(
                $"M1012 can be refactored only when the current build contains exactly one pipeline; found {pipelines.Length}.");
        }

        var filePath = ResolveWorkspacePath(narrative.Source.File);
        var original = _workspace.ReadFile(filePath);
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var declarationOffset = OffsetAtLine(original, narrative.Source.Line);
        var declarationLineEnd = original.IndexOf('\n', declarationOffset);
        if (declarationLineEnd < 0)
            declarationLineEnd = original.Length;
        var header = original[declarationOffset..declarationLineEnd];
        var openingBrace = original.IndexOf('{', declarationOffset);
        var trimmedHeader = header.TrimStart();
        var narrativeKeywordOffset = header.IndexOf("narrative", StringComparison.Ordinal);
        var declaredName = openingBrace >= declarationOffset
            && openingBrace < declarationLineEnd
            && narrativeKeywordOffset >= 0
            && declarationOffset + narrativeKeywordOffset + "narrative".Length <= openingBrace
            ? original[
                (declarationOffset + narrativeKeywordOffset
                    + "narrative".Length)..openingBrace].Trim()
            : string.Empty;
        var escapedNarrativeName = narrative.Name
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        var quotedNarrativeName = $"\"{escapedNarrativeName}\"";
        var identifierNarrativeName =
            System.Text.RegularExpressions.Regex.IsMatch(
                narrative.Name,
                @"^[A-Za-z_][A-Za-z0-9_]*$")
                ? narrative.Name
                : null;
        if (!trimmedHeader.StartsWith("narrative", StringComparison.Ordinal)
            || (trimmedHeader.Length > "narrative".Length
                && (char.IsLetterOrDigit(trimmedHeader["narrative".Length])
                    || trimmedHeader["narrative".Length] == '_'))
            || (!string.Equals(declaredName, quotedNarrativeName, StringComparison.Ordinal)
                && !string.Equals(declaredName, identifierNarrativeName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The compiler Narrative source location no longer matches the document.");
        }

        if (openingBrace < declarationOffset || openingBrace >= declarationLineEnd)
            throw new InvalidOperationException("The Narrative declaration has no opening brace.");
        var closingBrace = FindMatchingBrace(original, openingBrace);
        var block = original[openingBrace..(closingBrace + 1)];
        if (System.Text.RegularExpressions.Regex.IsMatch(block, @"\bapplies_to\b"))
            throw new InvalidOperationException(
                "The Narrative already contains applies_to; refresh Mentor analysis.");

        var closingLineStart = original.LastIndexOf('\n', Math.Max(0, closingBrace - 1)) + 1;
        var closingIndent = original[closingLineStart..closingBrace];
        if (closingIndent.Any(character => character is not (' ' or '\t')))
        {
            throw new InvalidOperationException(
                "The Narrative closing brace must be on its own line for this safe refactoring.");
        }

        var pipeline = pipelines[0];
        var addedLine = $"{closingIndent}    applies_to {{ {pipeline.Name} }}";
        var updated = original[..closingLineStart]
            + addedLine
            + newline
            + original[closingLineStart..];
        var relativePath = Path.GetRelativePath(_workspace.RootPath!, filePath).Replace('\\', '/');
        var oldLineCount = CountLines(original[..closingLineStart]);
        var patch = new StringBuilder()
            .AppendLine($"--- a/{relativePath}")
            .AppendLine($"+++ b/{relativePath}")
            .AppendLine($"@@ -{oldLineCount},0 +{oldLineCount + 1},1 @@")
            .Append($"+{addedLine}")
            .ToString();

        return new MentorRefactoringProposal(
            $"mentor:M1012:{narrative.Id}",
            finding.Code,
            $"Connect Narrative '{narrative.Name}' to pipeline {pipeline.Name}",
            filePath,
            narrative.Id,
            original,
            updated,
            patch,
            [
                $"The current compiler snapshot contains exactly one pipeline: '{pipeline.Name}'.",
                "The system author must confirm that this existing Narrative describes that pipeline; no intent, stakeholder, compliance, or degraded semantics are generated."
            ],
            $"The existing Narrative '{narrative.Name}' becomes explicitly connected to pipeline '{pipeline.Name}' through applies_to.");
    }

    /// <inheritdoc />
    public Task<MentorRefactoringSession> ApplyAsync(
        MentorRefactoringProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveWorkspacePath(proposal.FilePath);
        if (_openDocuments.TryGetByPath(path, out var document) && document.IsDirty)
            throw new InvalidOperationException("Save the open document before applying the Mentor patch.");
        if (!string.Equals(_workspace.ReadFile(path), proposal.OriginalContent, StringComparison.Ordinal))
            throw new InvalidOperationException("The source changed after preview. Create a fresh proposal.");

        _workspace.WriteFile(path, proposal.UpdatedContent);
        _eventBus.Publish(new FileChangedExternally(path));
        _output.Write("Mentor", $"Applied reviewed refactoring '{proposal.Id}'. Compiler validation is required.");
        return Task.FromResult(new MentorRefactoringSession(
            proposal,
            MentorRefactoringState.Applied,
            "Patch applied. Run compiler validation before keeping it.",
            []));
    }

    /// <inheritdoc />
    public async Task<MentorRefactoringSession> ValidateAsync(
        MentorRefactoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State != MentorRefactoringState.Applied)
            throw new InvalidOperationException("Only an applied Mentor patch can be validated.");
        EnsureAppliedContentIsCurrent(session.Proposal);

        var extension = Path.GetExtension(session.Proposal.FilePath);
        var compiler = _compilers.GetRegistered().FirstOrDefault(candidate => candidate.SupportsFile(extension));
        if (compiler is not ICompilerPreflightService preflightCompiler)
        {
            return session with
            {
                State = MentorRefactoringState.ValidationFailed,
                Message = $"No compiler preflight is available for '{extension}'. Revert the change."
            };
        }

        try
        {
            var result = await preflightCompiler.PreflightAsync(
                new BuildContext(_workspace.RootPath!, session.Proposal.FilePath),
                cancellationToken).ConfigureAwait(false);
            var state = result.Success
                ? MentorRefactoringState.Validated
                : MentorRefactoringState.ValidationFailed;
            var message = result.Success
                ? "Compiler preflight accepted the change. Keep or revert it explicitly."
                : $"Compiler preflight rejected the change with {result.Diagnostics.Count} diagnostic(s). Revert it.";
            _output.Write(
                "Mentor",
                message,
                result.Success ? OutputSeverity.Info : OutputSeverity.Warning);
            return session with
            {
                State = state,
                Message = message,
                Diagnostics = result.Diagnostics
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Compiler preflight failed: {ex.Message}. Revert the change.";
            _output.Write("Mentor", message, OutputSeverity.Warning);
            return session with
            {
                State = MentorRefactoringState.ValidationFailed,
                Message = message
            };
        }
    }

    /// <inheritdoc />
    public MentorRefactoringSession Keep(MentorRefactoringSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State != MentorRefactoringState.Validated)
            throw new InvalidOperationException("Only a compiler-validated Mentor patch can be kept.");
        EnsureAppliedContentIsCurrent(session.Proposal);
        _output.Write("Mentor", $"Kept validated refactoring '{session.Proposal.Id}'.");
        return session with
        {
            State = MentorRefactoringState.Kept,
            Message = "Validated change kept. No VCS staging or commit was performed."
        };
    }

    /// <inheritdoc />
    public Task<MentorRefactoringSession> RevertAsync(
        MentorRefactoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (session.State is not (
            MentorRefactoringState.Applied
            or MentorRefactoringState.Validated
            or MentorRefactoringState.ValidationFailed))
        {
            throw new InvalidOperationException("This Mentor refactoring cannot be reverted in its current state.");
        }

        EnsureAppliedContentIsCurrent(session.Proposal);
        _workspace.WriteFile(session.Proposal.FilePath, session.Proposal.OriginalContent);
        _eventBus.Publish(new FileChangedExternally(session.Proposal.FilePath));
        _output.Write("Mentor", $"Reverted refactoring '{session.Proposal.Id}'.");
        return Task.FromResult(session with
        {
            State = MentorRefactoringState.Reverted,
            Message = "Original source restored.",
            Diagnostics = []
        });
    }

    private string ResolveWorkspacePath(string path)
    {
        if (string.IsNullOrWhiteSpace(_workspace.RootPath))
            throw new InvalidOperationException("Open a workspace before using Mentor refactoring.");
        var root = Path.GetFullPath(_workspace.RootPath);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        if (!fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Mentor refactoring cannot write outside the open workspace.");
        }
        return fullPath;
    }

    private void EnsureAppliedContentIsCurrent(MentorRefactoringProposal proposal)
    {
        var path = ResolveWorkspacePath(proposal.FilePath);
        if (_openDocuments.TryGetByPath(path, out var document) && document.IsDirty)
            throw new InvalidOperationException("The document has later unsaved edits; refusing to overwrite them.");
        if (!string.Equals(_workspace.ReadFile(path), proposal.UpdatedContent, StringComparison.Ordinal))
            throw new InvalidOperationException("The source changed after the Mentor patch; refusing to overwrite it.");
    }

    private static int CountLines(string content)
    {
        if (content.Length == 0)
            return 0;
        return content.Count(character => character == '\n')
            + (content.EndsWith("\n", StringComparison.Ordinal) ? 0 : 1);
    }

    private static int OffsetAtLine(string content, int oneBasedLine)
    {
        if (oneBasedLine < 1)
            throw new InvalidOperationException("The compiler source line is invalid.");
        var offset = 0;
        for (var line = 1; line < oneBasedLine; line++)
        {
            var next = content.IndexOf('\n', offset);
            if (next < 0)
            {
                throw new InvalidOperationException(
                    "The compiler source line is outside the current document.");
            }
            offset = next + 1;
        }
        return offset;
    }

    private static int FindMatchingBrace(string content, int openingBrace)
    {
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        var lineComment = false;
        var blockComment = false;
        for (var index = openingBrace; index < content.Length; index++)
        {
            var current = content[index];
            var next = index + 1 < content.Length ? content[index + 1] : '\0';
            if (lineComment)
            {
                if (current == '\n')
                    lineComment = false;
                continue;
            }
            if (blockComment)
            {
                if (current == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }
                continue;
            }
            if (quote != '\0')
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == quote)
                    quote = '\0';
                continue;
            }
            if (current == '/' && next == '/')
            {
                lineComment = true;
                index++;
            }
            else if (current == '/' && next == '*')
            {
                blockComment = true;
                index++;
            }
            else if (current is '"' or '\'')
            {
                quote = current;
            }
            else if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                    return index;
            }
        }
        throw new InvalidOperationException(
            "The Narrative declaration has no matching closing brace.");
    }
}
