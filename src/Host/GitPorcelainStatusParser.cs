using Junexis.Studio.Abstractions;
using System;
using System.Collections.Generic;

namespace Junexis.Studio.Host;

/// <summary>
/// Parser wyjścia <c>git status --porcelain</c> do listy statusów plików.
/// </summary>
public static class GitPorcelainStatusParser
{
    /// <summary>
    /// Parsuje wyjście <c>git status --porcelain</c> (v1) do wpisów statusu.
    /// </summary>
    /// <param name="porcelain">Tekst z stdout.</param>
    /// <returns>Lista wpisów statusu.</returns>
    public static IReadOnlyList<VersionControlFileEntry> Parse(string porcelain)
    {
        if (porcelain is null)
        {
            throw new ArgumentNullException(nameof(porcelain));
        }

        var results = new List<VersionControlFileEntry>();

        var lines = porcelain.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw;
            if (line.Length < 3)
            {
                continue;
            }

            var x = line[0];
            var y = line[1];

            // Format: XY<space>path
            var path = line.Length > 3 ? line.Substring(3).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            // Rename format: "R  old -> new"
            var arrowIndex = path.LastIndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                path = path.Substring(arrowIndex + 4).Trim();
            }

            VersionControlFileStatus status;

            if (x == '?' && y == '?')
            {
                status = VersionControlFileStatus.Untracked;
            }
            else if (x == '!' && y == '!')
            {
                status = VersionControlFileStatus.Ignored;
            }
            else if (x != ' ')
            {
                status = VersionControlFileStatus.Staged;
            }
            else if (y != ' ')
            {
                status = VersionControlFileStatus.Modified;
            }
            else
            {
                status = VersionControlFileStatus.Committed;
            }

            results.Add(new VersionControlFileEntry(path, status));
        }

        return results;
    }
}
