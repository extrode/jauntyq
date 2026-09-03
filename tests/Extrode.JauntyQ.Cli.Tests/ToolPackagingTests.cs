using System.Text.RegularExpressions;
using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

/// <summary>
/// Guards the CLI's distribution channel. Before 2026-08-29 the CLI was a plain Exe with no
/// IsPackable, so publish.yml never shipped it and every license-gated verb lived in a binary
/// nobody could install. These assert the packaging identity, that the packed command name
/// agrees with the command name the CLI prints at users, and that the publish workflow packs
/// the solution rather than a project list a new package could fall out of.
/// </summary>
public class ToolPackagingTests
{
    private const string ToolCommand = "jauntyq";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string CliCsproj() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Extrode.JauntyQ.Cli", "Extrode.JauntyQ.Cli.csproj"));

    private static string? Property(string xml, string name)
    {
        var match = Regex.Match(xml, $@"<{name}>(?<value>[^<]*)</{name}>");
        return match.Success ? match.Groups["value"].Value : null;
    }

    [Theory]
    [InlineData("IsPackable", "true")]
    [InlineData("PackAsTool", "true")]
    [InlineData("ToolCommandName", ToolCommand)]
    [InlineData("PackageId", "Extrode.JauntyQ.Cli")]
    public void Cli_DeclaresToolPackaging(string property, string expected)
    {
        Assert.Equal(expected, Property(CliCsproj(), property));
    }

    [Fact]
    public void Cli_HasAPackageDescription()
    {
        string? description = Property(CliCsproj(), "Description");
        Assert.False(string.IsNullOrWhiteSpace(description));
    }

    [Fact]
    public void Cli_ReferencesOnlyTheCoreLibrary()
    {
        var references = Regex.Matches(CliCsproj(), @"<ProjectReference Include=""([^""]+)""")
            .Select(m => Path.GetFileName(m.Groups[1].Value))
            .ToArray();

        Assert.Equal(["Extrode.JauntyQ.Cli.Core.csproj"], references);
    }

    [Fact]
    public void ToolCommandName_IsTheOneCliHostPrints()
    {
        Assert.Equal(ToolCommand, CliHost.CommandName);
    }

    /// <summary>
    /// The command name shipped in DotnetToolSettings.xml has to be the one the CLI tells users
    /// to type. Renaming ToolCommandName alone would leave every usage line naming a command
    /// that does not exist.
    /// </summary>
    [Fact]
    public void PrintedUsage_UsesTheInstalledCommandName()
    {
        string program = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Extrode.JauntyQ.Cli.Core", "SchemaPullVerb.cs"));

        Assert.Contains("{CliHost.CommandName} schema pull", program);
    }

    /// <summary>
    /// publish.yml packs the whole solution, so opting a project into IsPackable is all that is
    /// needed to ship it. If that ever becomes an explicit project list, a newly packable project
    /// would be silently omitted — which is the failure this CLI already suffered once.
    /// </summary>
    [Fact]
    public void PublishWorkflow_PacksTheSolution()
    {
        string workflow = File.ReadAllText(Path.Combine(
            RepoRoot(), ".github", "workflows", "publish.yml"));

        Assert.Contains("dotnet pack JauntyQ.slnx", workflow);
    }
}
