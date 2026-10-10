using System.Globalization;
using System.Text.RegularExpressions;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Parses diagnostics emitted by the TypeScript compiler.</summary>
internal static partial class TypeScriptDiagnosticParser
{
    /// <summary>Attempts to parse a standard tsc diagnostic line.</summary>
    /// <param name="line">Compiler output line.</param>
    /// <param name="workspaceRoot">Root used to resolve relative file paths.</param>
    /// <param name="diagnostic">Parsed diagnostic when successful.</param>
    /// <returns>True when the line contains a supported tsc diagnostic.</returns>
    public static bool TryParse(string line, string workspaceRoot, out Diagnostic diagnostic)
    {
        diagnostic = null!;
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(workspaceRoot))
            return false;
        var match = DiagnosticRegex().Match(line);
        if (!match.Success
            || !int.TryParse(match.Groups["line"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var lineNumber)
            || !int.TryParse(match.Groups["column"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var columnNumber))
            return false;

        var file = match.Groups["file"].Value.Trim();
        var fullPath = Path.IsPathRooted(file)
            ? Path.GetFullPath(file)
            : Path.GetFullPath(Path.Combine(workspaceRoot, file));
        var severity = match.Groups["severity"].Value.Equals("warning", StringComparison.OrdinalIgnoreCase)
            ? DiagnosticSeverity.Warning
            : DiagnosticSeverity.Error;
        diagnostic = new Diagnostic(
            severity,
            $"TS{match.Groups["code"].Value}: {match.Groups["message"].Value.Trim()}",
            fullPath,
            lineNumber,
            columnNumber,
            "typescript");
        return true;
    }

    [GeneratedRegex(
        @"^(?<file>.+)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning)\s+TS(?<code>\d+):\s*(?<message>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticRegex();
}
