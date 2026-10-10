namespace Junexis.Studio.Abstractions;

/// <summary>Lifecycle state of one explicitly reviewed Mentor refactoring.</summary>
public enum MentorRefactoringState
{
    /// <summary>The exact source patch is ready for review.</summary>
    Preview,

    /// <summary>The patch was written and is waiting for compiler validation.</summary>
    Applied,

    /// <summary>The compiler accepted the changed source.</summary>
    Validated,

    /// <summary>The user explicitly kept the validated change.</summary>
    Kept,

    /// <summary>The applied change was restored to its original source.</summary>
    Reverted,

    /// <summary>The compiler rejected the changed source.</summary>
    ValidationFailed
}

/// <summary>An exact, deterministic source change proposed by Mentor.</summary>
/// <param name="Id">Stable proposal identifier derived from the rule and symbol.</param>
/// <param name="FindingCode">Mentor rule addressed by the proposal.</param>
/// <param name="Title">Concise action title.</param>
/// <param name="FilePath">Absolute source file path.</param>
/// <param name="RelatedSymbolId">Stable compiler symbol addressed by the change.</param>
/// <param name="OriginalContent">Exact source content used to create the preview.</param>
/// <param name="UpdatedContent">Exact source content after the proposed change.</param>
/// <param name="Patch">Human-readable unified patch shown before applying.</param>
/// <param name="Assumptions">Explicit assumptions that remain the author's responsibility.</param>
/// <param name="ExpectedEffect">Expected architectural effect if validation succeeds.</param>
public sealed record MentorRefactoringProposal(
    string Id,
    string FindingCode,
    string Title,
    string FilePath,
    string RelatedSymbolId,
    string OriginalContent,
    string UpdatedContent,
    string Patch,
    IReadOnlyList<string> Assumptions,
    string ExpectedEffect);

/// <summary>Result and rollback evidence for an applied Mentor refactoring.</summary>
/// <param name="Proposal">The exact reviewed proposal.</param>
/// <param name="State">Current lifecycle state.</param>
/// <param name="Message">Human-readable validation or safety message.</param>
/// <param name="Diagnostics">Compiler diagnostics from the validation step.</param>
public sealed record MentorRefactoringSession(
    MentorRefactoringProposal Proposal,
    MentorRefactoringState State,
    string Message,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Creates and executes narrow Mentor refactorings through preview, apply,
/// compiler validation, and an explicit keep-or-revert decision.
/// </summary>
public interface IMentorRefactoringService
{
    /// <summary>Creates an exact proposal for a supported current finding.</summary>
    MentorRefactoringProposal CreateProposal(
        AnalysisSnapshot snapshot,
        MentorFinding finding);

    /// <summary>Applies the previously reviewed proposal if the source is unchanged and saved.</summary>
    Task<MentorRefactoringSession> ApplyAsync(
        MentorRefactoringProposal proposal,
        CancellationToken cancellationToken = default);

    /// <summary>Runs compiler preflight against the applied source.</summary>
    Task<MentorRefactoringSession> ValidateAsync(
        MentorRefactoringSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Explicitly accepts a compiler-validated change.</summary>
    MentorRefactoringSession Keep(MentorRefactoringSession session);

    /// <summary>Restores the original source if no later edits would be overwritten.</summary>
    Task<MentorRefactoringSession> RevertAsync(
        MentorRefactoringSession session,
        CancellationToken cancellationToken = default);
}
