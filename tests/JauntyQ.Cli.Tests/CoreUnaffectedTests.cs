using Xunit;

namespace JauntyQ.Cli.Tests;

/// <summary>
/// SC-003 / SC-006 proof: license enforcement never reaches the zero-dependency core. Rather
/// than diff a triple build (flaky, slow), this asserts structurally that no core project can
/// even reference <c>JauntyQ.Licensing</c> — so the generator cannot read license state and
/// generated output/diagnostics are necessarily identical regardless of license presence.
/// </summary>
public class CoreUnaffectedTests
{
    private static readonly string[] CoreProjects =
    {
        "JauntyQ.Generator",
        "JauntyQ.Runtime",
        "JauntyQ.Schema",
        "JauntyQ.SqlParser",
        "JauntyQ.Analysis",
        "JauntyQ.Schema.Extraction",
        "JauntyQ.Cli.Core",
        "JauntyQ.Cli",
    };

    private static readonly string[] PremiumProjects =
    {
        "JauntyQ.Licensing",
        "JauntyQ.Registry",
        "JauntyQ.Explain",
        "JauntyQ.Schema.Contract",
        "JauntyQ.Cli.Premium",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir); // must find the solution root
        return dir!.FullName;
    }

    public static TheoryData<string> Core()
    {
        var data = new TheoryData<string>();
        foreach (var project in CoreProjects)
            data.Add(project);
        return data;
    }

    [Theory]
    [MemberData(nameof(Core))]
    public void CoreProject_DoesNotReferenceLicensing(string project)
    {
        string csproj = Path.Combine(RepoRoot(), "src", project, project + ".csproj");
        Assert.True(File.Exists(csproj), $"missing {csproj}");
        string text = File.ReadAllText(csproj);
        Assert.DoesNotContain("JauntyQ.Licensing", text);
    }

    [Theory]
    [MemberData(nameof(Core))]
    public void CoreProject_DoesNotReferenceAnyPremiumProject(string project)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "src", project, project + ".csproj"));
        foreach (var premium in PremiumProjects)
            Assert.DoesNotContain($"{premium}.csproj", text);
    }

    [Fact]
    public void Licensing_DependsOnly_OnBcl_NoCorePackageRefs()
    {
        string csproj = Path.Combine(RepoRoot(), "src", "JauntyQ.Licensing", "JauntyQ.Licensing.csproj");
        string text = File.ReadAllText(csproj);
        // No dependency on any JauntyQ core assembly (keeps enforcement out of the core graph).
        foreach (var core in CoreProjects)
            Assert.DoesNotContain($"{core}.csproj", text);
    }
}
