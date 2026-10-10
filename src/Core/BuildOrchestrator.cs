using System.Diagnostics;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Core;

/// <summary>
/// Orchestrates the build lifecycle: selection → start → compile → finish.
/// Publishes <see cref="BuildRequestedEvent"/>, <see cref="BuildStartedEvent"/>
/// and <see cref="BuildFinishedEvent"/> to <see cref="IEventBus"/>,
/// and logs the full lifecycle to the <c>Build</c> output channel.
/// </summary>
public sealed class BuildOrchestrator
{
    private readonly CompilerSelector _selector;
    private readonly IEventBus? _eventBus;
    private readonly IOutput? _output;
    private readonly IMentorBuildPreflightService? _mentorPreflight;

    /// <summary>Output channel name for build lifecycle messages.</summary>
    public const string OutputChannel = "Build";

    /// <summary>
    /// Creates a new <see cref="BuildOrchestrator"/>.
    /// </summary>
    /// <param name="selector">The compiler selector used to resolve the active compiler.</param>
    /// <param name="eventBus">Optional event bus for publishing build lifecycle events.</param>
    /// <param name="output">Optional output service for build logging.</param>
    /// <param name="mentorPreflight">Optional non-blocking Mentor phase.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is null.</exception>
    public BuildOrchestrator(
        CompilerSelector selector,
        IEventBus? eventBus = null,
        IOutput? output = null,
        IMentorBuildPreflightService? mentorPreflight = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selector = selector;
        _eventBus = eventBus;
        _output = output;
        _mentorPreflight = mentorPreflight;
    }

    /// <summary>
    /// Executes the full build lifecycle for the given context.
    /// <para>Lifecycle: BuildRequested → compiler selection → BuildStarted → build → BuildFinished.</para>
    /// If no compiler is found, publishes <see cref="BuildFinishedEvent"/> with <c>Success=false</c>.
    /// </summary>
    /// <param name="context">The build context (workspace, profile, etc.).</param>
    /// <param name="requestedCompilerId">Optional explicit compiler ID override.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The <see cref="CompilerResult"/>, or <see langword="null"/> if no compiler was available.</returns>
    public async Task<CompilerResult?> BuildAsync(
        BuildContext context,
        string? requestedCompilerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ── Phase 1: BuildRequested ──
        _output?.Write(OutputChannel, "Build requested.", OutputSeverity.Info);
        _eventBus?.Publish(new BuildRequestedEvent(context, requestedCompilerId));

        // ── Phase 2: Compiler selection ──
        if (requestedCompilerId is not null)
            _selector.SetExplicit(requestedCompilerId);

        var fileExt = context.ActiveDocumentPath is not null
            ? System.IO.Path.GetExtension(context.ActiveDocumentPath)
            : string.Empty;

        var compiler = _selector.Select(fileExt);

        if (compiler is null)
        {
            var noCompilerMsg = $"No compiler available for '{fileExt}'. Build aborted.";
            _output?.Write(OutputChannel, noCompilerMsg, OutputSeverity.Warning);
            _eventBus?.Publish(new BuildFinishedEvent(context, null, false, 0, 0, noCompilerMsg));
            return null;
        }

        // ── Phase 3: BuildStarted ──
        var startUtc = DateTime.UtcNow;
        _output?.Write(OutputChannel, $"Build started [{compiler.CompilerId}] profile={context.BuildProfile}.", OutputSeverity.Info);
        _eventBus?.Publish(new BuildStartedEvent(context, compiler.CompilerId, startUtc));

        // ── Phase 4: Build ──
        var sw = Stopwatch.StartNew();
        CompilerResult result;
        try
        {
            if (compiler is ICompilerPreflightService preflightCompiler)
            {
                _output?.Write(OutputChannel, "Compiler analysis preflight started.", OutputSeverity.Info);
                var preflight = await preflightCompiler.PreflightAsync(
                    context,
                    cancellationToken).ConfigureAwait(false);
                if (!preflight.Success)
                {
                    sw.Stop();
                    result = new CompilerResult(
                        false,
                        preflight.Diagnostics,
                        compiler.CompilerId,
                        preflight.BuildId,
                        AnalysisSnapshotPath: preflight.AnalysisSnapshotPath);
                    _output?.Write(OutputChannel,
                        $"Build stopped before code generation: {preflight.Diagnostics.Count} diagnostic(s).",
                        OutputSeverity.Warning);
                    _eventBus?.Publish(new BuildFinishedEvent(
                        context,
                        compiler.CompilerId,
                        false,
                        preflight.Diagnostics.Count,
                        sw.ElapsedMilliseconds));
                    return result;
                }

                if (_mentorPreflight is not null)
                    await _mentorPreflight.RunAsync(preflight, cancellationToken).ConfigureAwait(false);
                result = await preflightCompiler.BuildAfterPreflightAsync(
                    context,
                    preflight,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await compiler.BuildAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            var errMsg = $"Build failed with exception: {ex.Message}";
            _output?.Write(OutputChannel, errMsg, OutputSeverity.Error);
            _eventBus?.Publish(new BuildFinishedEvent(context, compiler.CompilerId, false, 0, sw.ElapsedMilliseconds, errMsg));
            return new CompilerResult(false, Array.Empty<Diagnostic>(), compiler.CompilerId);
        }

        sw.Stop();

        // ── Phase 5: BuildFinished ──
        var status = result.Success ? "succeeded" : "failed";
        _output?.Write(OutputChannel,
            $"Build {status} [{compiler.CompilerId}] {result.Diagnostics.Count} diagnostic(s), {sw.ElapsedMilliseconds} ms.",
            result.Success ? OutputSeverity.Info : OutputSeverity.Warning);
        _eventBus?.Publish(new BuildFinishedEvent(
            context,
            compiler.CompilerId,
            result.Success,
            result.Diagnostics.Count,
            sw.ElapsedMilliseconds));

        return result;
    }
}
