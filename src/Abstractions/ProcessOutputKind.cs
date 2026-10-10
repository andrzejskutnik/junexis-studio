namespace Junexis.Studio.Abstractions;

/// <summary>
/// Rodzaj strumienia, z którego pochodzi linia outputu procesu.
/// </summary>
public enum ProcessOutputKind
{
    /// <summary>
    /// Standard output.
    /// </summary>
    StdOut = 0,

    /// <summary>
    /// Standard error.
    /// </summary>
    StdErr = 1,
}
