using System.Text.RegularExpressions;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class DocumentationLinkTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static readonly Regex Link = new(@"\]\(([^)\s#]+)(?:#[^)]*)?\)", RegexOptions.Compiled);

    private static IEnumerable<string> ReaderFacingMarkdown()
    {
        string root = RepoRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly))
            yield return file;

        string docs = Path.Combine(root, "docs");
        if (Directory.Exists(docs))
            foreach (var file in Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories))
                yield return file;
    }

    [Fact]
    public void EveryRelativeLinkInTheReaderFacingDocsResolvesToAFileInTheRepository()
    {
        var broken = new List<string>();

        foreach (var file in ReaderFacingMarkdown())
        {
            string dir = Path.GetDirectoryName(file)!;
            foreach (Match m in Link.Matches(File.ReadAllText(file)))
            {
                string target = m.Groups[1].Value.Trim();
                if (target.Length == 0
                    || target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith('<'))
                    continue;

                string resolved = Path.GetFullPath(Path.Combine(dir, target));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                    broken.Add($"{Path.GetRelativePath(RepoRoot(), file)} -> {target}");
            }
        }

        Assert.True(broken.Count == 0,
            $"documentation links that point at nothing:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", broken)}");
    }

    [Fact]
    public void TheDocsFindTheirImages()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "docs", "assets", "build-not-prod.svg")));
    }
}
