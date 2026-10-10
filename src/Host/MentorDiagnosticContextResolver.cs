using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Creates evidence-bounded Mentor context without changing compiler diagnostics.</summary>
public static class MentorDiagnosticContextResolver
{
    /// <summary>Resolves context only for a current build and an exact declaration location.</summary>
    public static MentorDiagnosticContext? Resolve(
        Diagnostic diagnostic,
        AnalysisSnapshot? snapshot,
        MentorAnalysisEnvelope? mentor,
        string? diagnosticsBuildId)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (snapshot is null
            || mentor is null
            || mentor.Freshness != MentorAnalysisFreshness.Current
            || string.IsNullOrWhiteSpace(diagnosticsBuildId)
            || !string.Equals(snapshot.BuildId, diagnosticsBuildId, StringComparison.Ordinal)
            || !string.Equals(snapshot.BuildId, mentor.Result.BuildId, StringComparison.Ordinal)
            || !string.Equals(diagnostic.Source, "junexis-lang", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(diagnostic.Code)
            || string.IsNullOrWhiteSpace(diagnostic.FilePath)
            || diagnostic.Line is null)
            return null;

        var symbol = snapshot.Symbols
            .Where(candidate => candidate.Source is not null
                && candidate.Source.Line == diagnostic.Line
                && SamePath(candidate.Source.File, diagnostic.FilePath))
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (symbol is null)
            return null;

        var findings = mentor.Result.Findings
            .Where(finding => string.Equals(
                finding.RelatedSymbolId,
                symbol.Id,
                StringComparison.Ordinal))
            .OrderBy(finding => finding.Code, StringComparer.Ordinal)
            .ToArray();
        if (findings.Length == 0)
            return null;

        var references = string.Join(
            ", ",
            findings.Select(finding => finding.Code)
                .Distinct(StringComparer.Ordinal)
                .Select(code => $"Mentor {code}"));
        var nextStep = findings
            .Select(finding => finding.RecommendedAction)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return new MentorDiagnosticContext(
            MentorDiagnosticContext.CurrentSchemaVersion,
            snapshot.BuildId,
            diagnostic.Code,
            symbol.Id,
            MentorDiagnosticMappingKind.ExactDeclaration,
            findings,
            references,
            nextStep);
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
