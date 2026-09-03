using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

/// <summary>
/// License enforcement never reaches the zero-dependency core. Rather
/// than diff a triple build (flaky, slow), this asserts structurally that no core project can
/// even reference <c>Extrode.JauntyQ.Licensing</c> — so the generator cannot read license state and
/// generated output/diagnostics are necessarily identical regardless of license presence.
/// </summary>
public class CoreUnaffectedTests
{
    private static readonly string[] CoreProjects =
    {
        "Extrode.JauntyQ.Generator",
        "Extrode.JauntyQ.Runtime",
        "Extrode.JauntyQ.Schema",
        "Extrode.JauntyQ.SqlParser",
        "Extrode.JauntyQ.Analysis",
        "Extrode.JauntyQ.Schema.Extraction",
        "Extrode.JauntyQ.Cli.Core",
        "Extrode.JauntyQ.Cli",
    };

    private static readonly string[] PremiumProjects =
    {
        "Extrode.JauntyQ.Licensing",
        "Extrode.JauntyQ.Registry",
        "Extrode.JauntyQ.Explain",
        "Extrode.JauntyQ.Schema.Contract",
        "Extrode.JauntyQ.Cli.Premium",
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
        Assert.DoesNotContain("Extrode.JauntyQ.Licensing", text);
    }

    [Theory]
    [MemberData(nameof(Core))]
    public void CoreProject_DoesNotReferenceAnyPremiumProject(string project)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "src", project, project + ".csproj"));
        foreach (var premium in PremiumProjects)
            Assert.DoesNotContain($"{premium}.csproj", text);
    }
}
