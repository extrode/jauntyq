using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Round 83 (M-83): the generator reads its MSBuild knobs as
/// <c>build_property.&lt;Name&gt;</c> string literals, and the packaged
/// <c>build/</c> and <c>buildTransitive/</c> props files declare the matching
/// <c>&lt;CompilerVisibleProperty&gt;</c> entries. The two ends are in different
/// languages, so no compiler sees both, and no in-repo consumer imports the
/// packaged files at all — every sample, benchmark and test uses a
/// ProjectReference, which NuGet never wires <c>build/</c> assets into, so their
/// registration comes from the repo's own never-packed Directory.Build.props.
///
/// A typo in either packaged file therefore keeps CI green while silently
/// breaking every external NuGet consumer: JauntyQAutoCrud defaults to on, so an
/// ignored registration turns a consumer's opt-out into no opt-out at all, with
/// no diagnostic. The props file's own comment already says as much — "Without
/// this, those properties are silently invisible to the analyzer."
///
/// These tests compare the two independently hand-maintained lists. They do not
/// verify NuGet's import mechanics; only a pack-and-restore integration test
/// could, which is not warranted at this severity.
///
/// Reading repo files from disk is established practice in this suite — see
/// RegistryParityTests, CoreUnaffectedTests and PublicApiSurfaceTests, which all
/// walk up to the JauntyQ.slnx marker the same way.
/// </summary>
[Trait("Category", "AuditRegression")]
public class BuildPropertyParityTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadRepoFile(params string[] relative)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(relative)));

    /// <summary>Property names the generator actually reads, from its source.</summary>
    private static SortedSet<string> PropertiesRead()
    {
        string source = ReadRepoFile("src", "JauntyQ.Generator", "JauntyQGenerator.cs");
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(source, @"""build_property\.(\w+)"""))
            names.Add(m.Groups[1].Value);
        return names;
    }

    private static SortedSet<string> PropertiesDeclared(string folder)
    {
        string xml = ReadRepoFile("src", "JauntyQ.Generator", folder, "JauntyQ.Generator.props");
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(xml, @"<CompilerVisibleProperty\s+Include=""([^""]+)"""))
            names.Add(m.Groups[1].Value);
        return names;
    }

    [Fact]
    public void TheGeneratorReadsAtLeastOneBuildProperty_AndBothPropsFilesExist()
    {
        Assert.NotEmpty(PropertiesRead());
        Assert.NotEmpty(PropertiesDeclared("build"));
        Assert.NotEmpty(PropertiesDeclared("buildTransitive"));
    }

    [Theory]
    [InlineData("build")]
    [InlineData("buildTransitive")]
    public void EveryPropertyTheGeneratorReads_IsDeclaredCompilerVisible(string folder)
    {
        Assert.Equal(PropertiesRead(), PropertiesDeclared(folder));
    }

    [Fact]
    public void TheTwoPackagedPropsFiles_DeclareTheSameProperties()
    {
        Assert.Equal(PropertiesDeclared("build"), PropertiesDeclared("buildTransitive"));
    }

    [Fact]
    public void NoDeclarationCarriesTheBuildPropertyPrefix()
    {
        // Roslyn prepends "build_property." itself. A props file that spells the
        // prefix produces build_property.build_property.X, which the generator
        // never asks for -- and, being a registration rather than a read, fails
        // by silently registering nothing rather than by erroring.
        foreach (string folder in new[] { "build", "buildTransitive" })
            foreach (string name in PropertiesDeclared(folder))
                Assert.DoesNotContain("build_property", name, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePackagedPropsFileName_MatchesThePackageId()
    {
        // NuGet auto-imports build/{PackageId}.props by name. A PackageId change
        // that leaves the file named for the old id ships a props file nothing
        // imports, which is the same silent outcome as a misspelled property.
        string csproj = ReadRepoFile("src", "JauntyQ.Generator", "JauntyQ.Generator.csproj");
        var m = Regex.Match(csproj, @"<PackageId>([^<]+)</PackageId>");
        string packageId = m.Success ? m.Groups[1].Value.Trim() : "JauntyQ.Generator";

        foreach (string folder in new[] { "build", "buildTransitive" })
            Assert.True(
                File.Exists(Path.Combine(RepoRoot(), "src", "JauntyQ.Generator", folder, packageId + ".props")),
                $"{folder}/{packageId}.props does not exist, so NuGet will not auto-import it.");
    }
}
