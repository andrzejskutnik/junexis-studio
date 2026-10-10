using Junexis.Studio.Abstractions;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Junexis.Studio.Host;

/// <summary>
/// Minimalny parser diagnostyki g++ z linii stderr.
/// </summary>
internal static partial class GppDiagnosticParser
{
    /// <summary>
    /// Próbuje sparsować linię g++ w formacie: <c>file:line:col: error: message</c>.
    /// </summary>
    /// <param name="line">Linia stderr.</param>
    /// <param name="workspaceRoot">Root workspace używany do rozwiązywania ścieżek względnych.</param>
    /// <param name="diagnostic">Sparsowana diagnostyka.</param>
    /// <returns><see langword="true"/> jeśli udało się sparsować.</returns>
    public static bool TryParse(string line, string workspaceRoot, out Diagnostic diagnostic)
    {
        diagnostic = null!;

        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return false;
        }

        var match = GppLineRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var filePart = match.Groups["file"].Value;
        var linePart = match.Groups["line"].Value;
        var colPart = match.Groups["col"].Value;
        var sevPart = match.Groups["sev"].Value;
        var msgPart = match.Groups["msg"].Value;

        if (!int.TryParse(linePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNo) || lineNo < 1)
        {
            return false;
        }

        if (!int.TryParse(colPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var colNo) || colNo < 1)
        {
            return false;
        }

        var severity = MapSeverity(sevPart);
        var resolvedPath = ResolveFilePath(workspaceRoot, filePart);

        diagnostic = new Diagnostic(severity, msgPart.Trim(), resolvedPath, lineNo, colNo);
        return true;
    }

    private static string ResolveFilePath(string workspaceRoot, string filePart)
    {
        var fileTrimmed = filePart.Trim();

        if (Path.IsPathRooted(fileTrimmed))
        {
            return Path.GetFullPath(fileTrimmed);
        }

        return Path.GetFullPath(Path.Combine(workspaceRoot, fileTrimmed));
    }

    private static DiagnosticSeverity MapSeverity(string sevPart)
    {
        var s = (sevPart ?? string.Empty).Trim();

        return s.Equals("warning", StringComparison.OrdinalIgnoreCase)
            ? DiagnosticSeverity.Warning
            : s.Equals("note", StringComparison.OrdinalIgnoreCase)
                ? DiagnosticSeverity.Info
                : DiagnosticSeverity.Error;
    }

    [GeneratedRegex(@"^(?<file>.+?):(?<line>\d+):(?<col>\d+):\s*(?<sev>fatal error|error|warning|note):\s*(?<msg>.+)$", RegexOptions.Compiled)]
    private static partial Regex GppLineRegex();
}
