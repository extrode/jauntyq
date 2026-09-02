using System.Text.RegularExpressions;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class PublishWorkflowContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Normalised so the line-anchored patterns below hold on a CRLF checkout too.
    private static string Workflow() =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "publish.yml"))
            .Replace("\r\n", "\n");

    private static string RootProps() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"))
            .Replace("\r\n", "\n");

    private static string PublishJob()
    {
        var m = Regex.Match(Workflow(), @"(?ms)^  publish:\n(.*?)(?=^  \S|\z)");
        Assert.True(m.Success, "publish.yml has no publish job");
        return m.Groups[1].Value;
    }

    [Fact]
    public void PublishJob_RunsInThePackagesEnvironment()
    {
        Assert.Matches(@"(?m)^\s+environment:\s*packages\s*$", PublishJob());
    }

    [Fact]
    public void PublishJob_RequestsAnOidcToken()
    {
        Assert.Matches(@"(?m)^\s+id-token:\s*write", PublishJob());
    }

    [Fact]
    public void PublishJob_LogsInToNuGetOrgWithTrustedPublishing()
    {
        Assert.Contains("uses: NuGet/login@v1", PublishJob());
        Assert.Contains("https://api.nuget.org/v3/index.json", PublishJob());
    }

    [Fact]
    public void Workflow_KeepsNoLongLivedApiKey()
    {
        Assert.DoesNotMatch(@"secrets\.NUGET", Workflow());
        Assert.DoesNotContain("nuget.pkg.github.com", Workflow());
    }

    [Fact]
    public void Workflow_GuardsTheTagAgainstThePropsVersion()
    {
        Assert.Contains("<Version>", Workflow());
        Assert.Contains("GITHUB_REF_NAME#v", Workflow());
    }

    [Fact]
    public void Workflow_TriggersOnVersionTagsOnly()
    {
        Assert.Matches(@"(?ms)^on:\n  push:\n    tags: \['v\*'\]", Workflow());
        Assert.DoesNotMatch(@"(?m)^    branches:", Workflow());
    }

    [Fact]
    public void Packages_DeclareTheIslRLicenceFile()
    {
        Assert.Contains("<PackageLicenseFile>LICENSE.md</PackageLicenseFile>", RootProps());
        Assert.Contains("<PackageRequireLicenseAcceptance>false</PackageRequireLicenseAcceptance>", RootProps());
    }

    [Theory]
    [InlineData("LICENSE.md")]
    [InlineData("EXCEPTION.md")]
    [InlineData("NOTICE.md")]
    public void Packages_CarryTheLicenceDocument(string file)
    {
        Assert.Matches(
            $@"<None Include=""\$\(MSBuildThisFileDirectory\){Regex.Escape(file)}"" Pack=""true""",
            RootProps());
        Assert.True(File.Exists(Path.Combine(RepoRoot(), file)), file + " is missing from the repository root");
    }

    [Fact]
    public void Packages_NameNoPrivateLicenceFile()
    {
        Assert.DoesNotContain("LICENSE-EULA", RootProps());
        Assert.DoesNotContain("LICENSE-OUTPUT-EXCEPTION", RootProps());
    }
}
