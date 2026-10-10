namespace Junexis.Studio.Abstractions;

/// <summary>Visual state of a source breakpoint shown in the editor margin.</summary>
public enum BreakpointGlyphState
{
    /// <summary>The breakpoint has not yet been verified by an active adapter.</summary>
    Pending,
    /// <summary>Every runtime location was verified at the requested position.</summary>
    Verified,
    /// <summary>Every runtime location was verified, but at least one was moved by the adapter.</summary>
    Adjusted,
    /// <summary>Some runtime locations were verified and some were rejected.</summary>
    PartiallyVerified,
    /// <summary>Every runtime location was rejected by the adapter.</summary>
    Rejected,
}

/// <summary>Presentation data for one source breakpoint glyph.</summary>
/// <param name="Path">Absolute source path.</param>
/// <param name="Line">One-based source line.</param>
/// <param name="Column">Optional one-based source column.</param>
/// <param name="State">Aggregated adapter verification state.</param>
/// <param name="ToolTip">Human-readable verification details.</param>
/// <param name="IsLogpoint">Whether the breakpoint emits a message instead of stopping.</param>
/// <param name="HasCondition">Whether a condition or hit condition limits activation.</param>
public sealed record BreakpointGlyph(
    string Path,
    int Line,
    int? Column,
    BreakpointGlyphState State,
    string ToolTip,
    bool IsLogpoint = false,
    bool HasCondition = false);

/// <summary>Projects shared breakpoints and DAP verification results into editor glyphs.</summary>
public interface IBreakpointGlyphService
{
    /// <summary>Returns glyphs for one source file in line order.</summary>
    /// <param name="path">Absolute source path.</param>
    /// <returns>Current breakpoint glyphs.</returns>
    IReadOnlyList<BreakpointGlyph> GetGlyphs(string path);

    /// <summary>Toggles a source breakpoint and synchronizes an active DAP session.</summary>
    /// <param name="path">Absolute source path.</param>
    /// <param name="line">One-based source line.</param>
    /// <param name="column">Optional one-based source column.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ToggleAsync(string path, int line, int? column = null, CancellationToken cancellationToken = default);

    /// <summary>Raised when breakpoint glyphs should be rendered again.</summary>
    event Action? GlyphsChanged;
}
