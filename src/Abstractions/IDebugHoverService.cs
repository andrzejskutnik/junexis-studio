namespace Junexis.Studio.Abstractions;

/// <summary>Evaluates editor hover expressions in the currently stopped debug frame.</summary>
public interface IDebugHoverService
{
    /// <summary>Evaluates an expression when a stopped stack frame is available.</summary>
    /// <param name="expression">Expression extracted from the editor document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evaluation result, or <see langword="null"/> when debugging is not stopped.</returns>
    Task<DebugEvaluation?> EvaluateAsync(
        string expression,
        CancellationToken cancellationToken = default);
}
