using Junexis.Studio.Abstractions;
using System.Text.RegularExpressions;

namespace Junexis.Studio.Host;

/// <summary>
/// Parses unified diff output (from <c>git diff</c>) into a list of <see cref="DiffHunk"/> objects.
/// </summary>
public static class UnifiedDiffParser
{
    // Matches hunk headers like: @@ -1,4 +1,6 @@ optional context
    private static readonly Regex HunkHeader = new(
        @"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses unified diff text into a list of hunks.
    /// Lines that don't belong to hunks (file headers, index lines) are skipped.
    /// </summary>
    /// <param name="diffText">Raw unified diff output.</param>
    /// <returns>Parsed list of <see cref="DiffHunk"/> instances (empty if no hunks).</returns>
    public static IReadOnlyList<DiffHunk> Parse(string diffText)
    {
        if (string.IsNullOrWhiteSpace(diffText))
            return Array.Empty<DiffHunk>();

        var hunks = new List<DiffHunk>();
        var lines = diffText.Split('\n');

        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var match = HunkHeader.Match(line);
            if (!match.Success)
            {
                i++;
                continue;
            }

            var oldStart = int.Parse(match.Groups[1].Value);
            var oldCount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
            var newStart = int.Parse(match.Groups[3].Value);
            var newCount = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1;

            i++;
            var hunkLines = new List<DiffLine>();

            while (i < lines.Length)
            {
                var hunkLine = lines[i];

                // Next hunk header — stop collecting lines for this hunk
                if (HunkHeader.IsMatch(hunkLine))
                    break;

                // Skip "\ No newline at end of file" notices
                if (hunkLine.StartsWith('\\'))
                {
                    i++;
                    continue;
                }

                // Skip empty trailing lines introduced by the final \n split
                if (hunkLine.Length == 0 && i == lines.Length - 1)
                {
                    i++;
                    break;
                }

                var kind = hunkLine.Length > 0 && hunkLine[0] switch
                {
                    '+' => true,
                    '-' => true,
                    _ => false
                }
                    ? (hunkLine[0] == '+' ? DiffLineKind.Added : DiffLineKind.Removed)
                    : DiffLineKind.Context;

                var content = hunkLine.Length > 0 ? hunkLine[1..] : string.Empty;
                hunkLines.Add(new DiffLine(kind, content));
                i++;
            }

            hunks.Add(new DiffHunk(oldStart, oldCount, newStart, newCount, hunkLines));
        }

        return hunks;
    }
}
