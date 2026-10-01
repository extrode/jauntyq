using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class GeneratedOutputApprovalTests
{
    public static TheoryData<string> Dialects() => new() { "mysql", "postgres", "sqlite", "sqlserver" };

    [Theory]
    [MemberData(nameof(Dialects))]
    public void GeneratedSources_MatchApprovedBaseline(string dialect)
    {
        string actual = Generate(Path.Combine(ApprovedDir(), dialect));
        string approvedPath = Path.Combine(ApprovedDir(), dialect + ".approved.txt");

        if (!File.Exists(approvedPath))
        {
            File.WriteAllText(approvedPath, actual);
            Assert.Fail($"Baseline did not exist; wrote it to '{approvedPath}'. Review and commit it, then re-run.");
        }

        if (File.ReadAllText(approvedPath).Replace("\r\n", "\n") != actual)
        {
            string receivedPath = approvedPath.Replace(".approved.txt", ".received.txt");
            File.WriteAllText(receivedPath, actual);
            Assert.Fail($"Generated output for {dialect} changed. Diff '{approvedPath}' against '{receivedPath}'; "
                + "if the change is intended, delete the .approved.txt and re-run.");
        }
    }

    private static string Generate(string fixtureDir)
    {
        var texts = Directory.GetFiles(Path.Combine(fixtureDir, "db"), "*.*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (AdditionalText)new InMemoryAdditionalText(
                Path.GetRelativePath(fixtureDir, p).Replace('\\', '/'),
                File.ReadAllText(p)))
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create("ApprovalAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts)
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true))
            .RunGenerators(compilation)
            .GetRunResult();

        var sb = new StringBuilder();
        foreach (var d in result.Diagnostics.OrderBy(d => d.Id, StringComparer.Ordinal).ThenBy(d => d.GetMessage(), StringComparer.Ordinal))
            sb.Append("#### ").Append(d.Id).Append(' ').Append(d.GetMessage()).Append('\n');
        foreach (var s in result.Results.Single().GeneratedSources.OrderBy(s => s.HintName, StringComparer.Ordinal))
            sb.Append("==== ").Append(s.HintName).Append('\n').Append(s.SourceText.ToString().Replace("\r\n", "\n")).Append('\n');
        return sb.ToString();
    }

    private static string ApprovedDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, ".git")) &&
               !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        Assert.True(dir != null, "Could not locate repository root (no .git found walking up).");
        return Path.Combine(dir!.FullName, "tests", "Extrode.JauntyQ.Generator.Tests", "Approved");
    }
}
