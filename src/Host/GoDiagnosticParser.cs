using Junexis.Studio.Abstractions;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Junexis.Studio.Host;

/// <summary>
/// Minimalny parser diagnostyki kompilatora Go z linii stderr.
/// Format: <c>file.go:line:col: message</c>
/// </summary>
internal static partial class GoDiagnosticParser
{
    /// <summary>
    /// Próbuje sparsować linię diagnostyczną go build.
    /// Format: <c>./file.go:line:col: message</c>
    /// </summary>
    /// <param name="line">Linia stderr.</param>
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

        var match = GoLineRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var filePart = match.Groups["file"].Value;
        var linePart = match.Groups["line"].Value;
        var colPart = match.Groups["col"].Value;
        var msgPart = match.Groups["msg"].Value;

        if (!int.TryParse(linePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNo) || lineNo < 1)
        {
            return false;
        }

        if (!int.TryParse(colPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var colNo) || colNo < 1)
        {
            return false;
        }

        var resolvedPath = ResolveFilePath(workspaceRoot, filePart);

        // go build zawsze wypisuje błędy; ostrzeżenia (vet) mają prefiks "warning:"
        var severity = msgPart.TrimStart().StartsWith("warning:", StringComparison.OrdinalIgnoreCase)
            ? DiagnosticSeverity.Warning
            : DiagnosticSeverity.Error;

        diagnostic = new Diagnostic(severity, msgPart.Trim(), resolvedPath, lineNo, colNo);
        return true;
    }

    private static string ResolveFilePath(string workspaceRoot, string filePart)
    {
        var fileTrimmed = filePart.Trim();

        // usuń prefiks "./" lub ".\" jeśli jest
        if (fileTrimmed.StartsWith("./", StringComparison.Ordinal)
            || fileTrimmed.StartsWith(".\\", StringComparison.Ordinal))
        {
            fileTrimmed = fileTrimmed[2..];
        }

        if (Path.IsPathRooted(fileTrimmed))
        {
            return Path.GetFullPath(fileTrimmed);
        }

        return Path.GetFullPath(Path.Combine(workspaceRoot, fileTrimmed));
    }

    /// <summary>
    /// Wyrażenie regularne dopasowujące linie diagnostyczne go build / go vet.
    /// Przykład: <c>./main.go:12:5: undefined: foo</c>
    /// </summary>
    [GeneratedRegex(
        @"^(?<file>[^:]+?\.go):(?<line>\d+):(?<col>\d+):\s*(?<msg>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoLineRegex();
}
