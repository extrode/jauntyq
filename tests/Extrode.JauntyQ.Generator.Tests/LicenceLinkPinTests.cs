using System.Text.RegularExpressions;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class LicenceLinkPinTests
{
    private static readonly Regex PinnedLink =
        new(@"^https://islamiclicense\.org/isl-[a-z-]+/\d+\.\d+/[A-Z]+\.md$", RegexOptions.Compiled);

    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git", "tmp", "TestResults", "artifacts", "dist", "out"];

    private static readonly string[] TextExtensions = [".md", ".cs", ".props", ".targets", ".csproj", ".yml", ".json", ".txt", ".sh"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> TextFiles(string dir)
    {
        foreach (var sub in Directory.GetDirectories(dir))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(sub)))
                continue;
            foreach (var f in TextFiles(sub))
                yield return f;
        }
        foreach (var f in Directory.GetFiles(dir))
            if (TextExtensions.Contains(Path.GetExtension(f)))
                yield return f;
    }

    private static IEnumerable<(string file, string link)> LicenceLinks() =>
        from file in TextFiles(RepoRoot())
        from m in Regex.Matches(File.ReadAllText(file), @"https://islamiclicense\.org[^\s)""'`>\]]*")
        select (file, m.Value);

    [Fact]
    public void TheTreeLinksToTheLicenceSite()
    {
        Assert.NotEmpty(LicenceLinks());
    }

    [Fact]
    public void EveryLicenceLink_IsPinnedToAVersionAndAFile()
    {
        var loose = LicenceLinks().Where(l => !PinnedLink.IsMatch(l.link)).ToList();

        Assert.True(loose.Count == 0,
            "unpinned license links:\n" + string.Join("\n", loose.Select(l => $"{Path.GetRelativePath(RepoRoot(), l.file)}: {l.link}")));
    }

    [Fact]
    public void LicenceFile_IsIslR12()
    {
        var first = File.ReadLines(Path.Combine(RepoRoot(), "LICENSE.md")).First();

        Assert.Equal("# Islamic Software License - Restricted (ISL-R), Version 1.2", first);
    }

    [Fact]
    public void LicenceFile_NamesTheLicensor()
    {
        Assert.Contains("**1.2 \"Licensor\"** means **`Extrode LLC`**", File.ReadAllText(Path.Combine(RepoRoot(), "LICENSE.md")));
    }

    [Fact]
    public void ExceptionFile_IsIslOe12()
    {
        var first = File.ReadLines(Path.Combine(RepoRoot(), "EXCEPTION.md")).First();

        Assert.Equal("# Islamic Software License - Output Exception (ISL-OE), Version 1.2", first);
    }

    [Fact]
    public void Notice_AdoptsBothDocumentsWithPinnedUrls()
    {
        var notice = File.ReadAllText(Path.Combine(RepoRoot(), "NOTICE.md"));

        Assert.Contains("(ISL-R), Version 1.2, WITH the Islamic Software License - Output Exception", notice);
        Assert.Contains("https://islamiclicense.org/isl-r/1.2/LICENSE.md", notice);
        Assert.Contains("https://islamiclicense.org/isl-oe/1.2/EXCEPTION.md", notice);
        Assert.Contains("LicenseRef-ISL-R-1.2 WITH AdditionRef-ISL-OE-1.2", notice);
    }

    [Fact]
    public void NoContributorAgreementIsAdopted()
    {
        Assert.Empty(Directory.GetFiles(RepoRoot(), "*CLA*", SearchOption.TopDirectoryOnly));
        Assert.DoesNotContain("ISL-CLA", File.ReadAllText(Path.Combine(RepoRoot(), "NOTICE.md")));
    }
}
