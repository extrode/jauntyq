using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class CoverageScopeTests
{
    private static readonly string[] SharedProjects = { "SqlParser", "Schema", "Analysis" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void TheGeneratorsCopyOfEachSharedProject_IsLeftOutOfCoverage()
    {
        string settings = File.ReadAllText(Path.Combine(RepoRoot(), "coverage.runsettings"));
        string exclude = Regex.Match(settings, "<Exclude>([^<]*)</Exclude>").Groups[1].Value;

        Assert.Equal(
            SharedProjects.Select(p => "[Extrode.JauntyQ.Generator]Extrode.JauntyQ." + p + ".*"),
            exclude.Split(','));
    }

    [Fact]
    public void NoGeneratorOwnedFile_DeclaresASharedProjectsNamespace()
    {
        string generatorDir = Path.Combine(RepoRoot(), "src", "Extrode.JauntyQ.Generator");
        string objDir = Path.Combine(generatorDir, "obj") + Path.DirectorySeparatorChar;
        var pattern = new Regex(@"^namespace Extrode\.JauntyQ\.(" + string.Join("|", SharedProjects) + @")\b", RegexOptions.Multiline);

        string[] offenders = Directory.GetFiles(generatorDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(objDir, StringComparison.Ordinal))
            .Where(f => pattern.IsMatch(File.ReadAllText(f)))
            .Select(f => f.Substring(generatorDir.Length + 1))
            .ToArray();

        Assert.Empty(offenders);
    }
}
