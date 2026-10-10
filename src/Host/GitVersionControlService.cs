using Junexis.Studio.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Junexis.Studio.Host;

/// <summary>
/// Implementacja <see cref="IVersionControlService"/> oparta o Git CLI.
/// </summary>
public sealed class GitVersionControlService : IVersionControlService
{
    private readonly IWorkspace _workspace;
    private readonly IOutput _output;

    /// <summary>
    /// Tworzy serwis VCS oparty o Git CLI.
    /// </summary>
    /// <param name="workspace">Workspace dostarczający root ścieżki.</param>
    /// <param name="output">Output do logowania błędów.</param>
    public GitVersionControlService(IWorkspace workspace, IOutput output)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <inheritdoc />
    public bool IsRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            return false;
        }

        var result = RunGit(fullPath, "rev-parse --is-inside-work-tree");
        return result.ExitCode == 0 && result.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<VersionControlFileEntry> GetRepositoryStatus()
    {
        var root = _workspace.RootPath;
        if (string.IsNullOrWhiteSpace(root) || !IsRepository(root))
        {
            return Array.Empty<VersionControlFileEntry>();
        }

        var result = RunGit(root, "status --porcelain");
        if (result.ExitCode != 0)
        {
            _output.Write("VCS", $"git status failed: {result.StdErr.Trim()}");
            return Array.Empty<VersionControlFileEntry>();
        }

        return GitPorcelainStatusParser.Parse(result.StdOut);
    }

    /// <inheritdoc />
    public VersionControlFileStatus GetFileStatus(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return VersionControlFileStatus.Committed;
        }

        var root = _workspace.RootPath;
        if (string.IsNullOrWhiteSpace(root) || !IsRepository(root))
        {
            return VersionControlFileStatus.Committed;
        }

        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        var relative = Path.GetRelativePath(root, full);

        var status = GetRepositoryStatus();
        var match = status.FirstOrDefault(e =>
            string.Equals(e.Path, relative, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Path.Replace('/', Path.DirectorySeparatorChar), relative, StringComparison.OrdinalIgnoreCase));

        return match?.Status ?? VersionControlFileStatus.Committed;
    }

    /// <inheritdoc />
    public void Commit(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Commit message cannot be empty.", nameof(message));
        }

        var root = _workspace.RootPath;
        if (string.IsNullOrWhiteSpace(root) || !IsRepository(root))
        {
            _output.Write("VCS", "Commit skipped (not a git repository)");
            return;
        }

        var staged = RunGit(root, "diff --cached --quiet --exit-code");
        if (staged.ExitCode == 0)
        {
            _output.Write("VCS", "Commit skipped (nothing staged)");
            return;
        }
        if (staged.ExitCode != 1)
        {
            _output.Write("VCS", $"Cannot inspect staged changes: {staged.StdErr.Trim()}");
            return;
        }

        var commit = RunGit(root, $"commit -m \"{message.Replace("\"", "\\\"")}\"");
        if (commit.ExitCode != 0)
        {
            var err = string.IsNullOrWhiteSpace(commit.StdErr) ? commit.StdOut : commit.StdErr;
            _output.Write("VCS", $"git commit failed: {err.Trim()}");
            return;
        }

        _output.Write("VCS", "Commit created");
    }

    private (int ExitCode, string StdOut, string StdErr) RunGit(string workingDirectory, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"-C \"{workingDirectory}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var p = Process.Start(psi);
            if (p is null)
            {
                return (1, string.Empty, "Failed to start git process.");
            }

            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (1, string.Empty, ex.Message);
        }
    }
}
