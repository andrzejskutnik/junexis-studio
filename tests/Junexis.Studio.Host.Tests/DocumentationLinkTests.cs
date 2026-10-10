using System.Text.RegularExpressions;

namespace Junexis.Studio.Host.Tests;

public sealed partial class DocumentationLinkTests
{
    [Fact]
    public void LocalMarkdownLinks_ResolveToExistingFiles()
    {
        var root = FindRepositoryRoot();
        var markdownFiles = Directory
            .EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)
            .Append(Path.Combine(root, "README.md"))
            .ToArray();
        var broken = new List<string>();

        foreach (var source in markdownFiles)
        {
            var text = File.ReadAllText(source);
            foreach (Match match in MarkdownLink().Matches(text))
            {
                var rawTarget = match.Groups[1].Value.Trim().Trim('<', '>');
                var fileTarget = rawTarget.Split('#', 2)[0];
                if (string.IsNullOrWhiteSpace(fileTarget)
                    || Uri.TryCreate(fileTarget, UriKind.Absolute, out _))
                    continue;

                var decoded = Uri.UnescapeDataString(fileTarget).Replace('/', Path.DirectorySeparatorChar);
                var resolved = Path.GetFullPath(decoded, Path.GetDirectoryName(source)!);
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    broken.Add(
                        $"{Path.GetRelativePath(root, source)} -> {rawTarget}");
                }
            }
        }

        Assert.True(broken.Count == 0, "Broken local Markdown links:" + Environment.NewLine
            + string.Join(Environment.NewLine, broken));
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "JunexisStudio.sln")))
                return current.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate JunexisStudio.sln.");
    }

    [GeneratedRegex(@"(?<!!)\[[^\]]+\]\(([^)]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLink();
}
