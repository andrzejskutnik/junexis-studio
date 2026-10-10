namespace Junexis.Studio.Abstractions;

/// <summary>Evidence class used to associate a compiler diagnostic with a stable symbol.</summary>
public enum MentorDiagnosticMappingKind
{
    /// <summary>The diagnostic and declaration begin on the same source line.</summary>
    ExactDeclaration
}

/// <summary>Versioned, non-authoritative Mentor context attached to a compiler diagnostic.</summary>
/// <param name="SchemaVersion">Context contract version.</param>
/// <param name="BuildId">Current compiler build used as evidence.</param>
/// <param name="DiagnosticCode">Original compiler diagnostic code.</param>
/// <param name="SymbolId">Stable symbol matched through compiler source evidence.</param>
/// <param name="MappingKind">Deterministic mapping evidence class.</param>
/// <param name="Findings">Current Mentor findings for the matched symbol.</param>
/// <param name="ExplanationReference">Stable finding references supporting the context.</param>
/// <param name="SmallestSafeNextStep">Smallest deterministic action recommended by Mentor.</param>
public sealed record MentorDiagnosticContext(
    string SchemaVersion,
    string BuildId,
    string DiagnosticCode,
    string SymbolId,
    MentorDiagnosticMappingKind MappingKind,
    IReadOnlyList<MentorFinding> Findings,
    string ExplanationReference,
    string? SmallestSafeNextStep)
{
    /// <summary>Current context schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";
}
