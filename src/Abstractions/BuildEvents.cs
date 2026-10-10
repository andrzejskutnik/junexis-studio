namespace Junexis.Studio.Abstractions;

/// <summary>
/// Published on <see cref="IEventBus"/> when a build is requested (before compiler selection).
/// </summary>
/// <param name="Context">The build context describing the workspace and build profile.</param>
/// <param name="RequestedCompilerId">
/// Optional compiler ID explicitly requested. Null means use default selection logic.
/// </param>
public record BuildRequestedEvent(BuildContext Context, string? RequestedCompilerId = null);

/// <summary>
/// Published on <see cref="IEventBus"/> when a build actually starts (compiler selected, work begins).
/// </summary>
/// <param name="Context">The build context.</param>
/// <param name="CompilerId">The compiler that was selected and is executing the build.</param>
/// <param name="StartedAtUtc">UTC timestamp when the build started.</param>
public record BuildStartedEvent(BuildContext Context, string CompilerId, DateTime StartedAtUtc);

/// <summary>
/// Published on <see cref="IEventBus"/> when a build completes (success or failure).
/// </summary>
/// <param name="Context">The build context.</param>
/// <param name="CompilerId">The compiler that executed the build (null if no compiler was found).</param>
/// <param name="Success">Whether the build succeeded.</param>
/// <param name="DiagnosticCount">Number of diagnostics reported by the compiler.</param>
/// <param name="DurationMs">Duration of the build in milliseconds.</param>
/// <param name="Error">Optional error message if the build failed or no compiler was available.</param>
public record BuildFinishedEvent(
    BuildContext Context,
    string? CompilerId,
    bool Success,
    int DiagnosticCount,
    long DurationMs,
    string? Error = null);
