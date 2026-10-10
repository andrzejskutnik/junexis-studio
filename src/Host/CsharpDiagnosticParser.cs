using Junexis.Studio.Abstractions;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Junexis.Studio.Host;

/// <summary>
/// Minimalny parser diagnostyki kompilatora C# (Roslyn / dotnet build) z linii stdout/stderr.
/// Format: <c>file.cs(line,col): error/warning CSXXX: message</c>
/// </summary>
internal static partial class CsharpDiagnosticParser
{
    /// <summary>
    /// Próbuje sparsować linię diagnostyczną dotnet build.
    /// Format: <c>file.cs(line,col): error/warning CSXXX: message</c>
    /// </summary>
    /// <param name="line">Linia stdout lub stderr.</param>
    /// <param name="workspaceRoot">Root workspace używany do rozwiązywania ścieżek względnych.</param>
    /// <param name="diagnostic">Sparsowana diagnostyka lub <see langword="null"/>.</param>
    /// <returns><see langword="true"/> jeśli udało się sparsować.</returns>
    public static bool TryParse(string line, string workspaceRoot, out Diagnostic diagnostic)
    {
        diagnostic = null!;

        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return false;
        }

        var match = CsharpLineRegex().Match(line);
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
            : DiagnosticSeverity.Error;
    }

    /// <summary>
    /// Wyrażenie regularne dopasowujące linie diagnostyczne Roslyn/MSBuild.
    /// Przykład: <c>src\Main.cs(10,5): error CS0001: message here</c>
    /// </summary>
    [GeneratedRegex(
        @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\)\s*:\s*(?<sev>error|warning)\s+[A-Za-z]{0,4}\d+\s*:\s*(?<msg>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CsharpLineRegex();
}
