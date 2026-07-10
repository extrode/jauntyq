using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// More than one *.schema.json in AdditionalFiles: the ordinal-lowest path
/// wins deterministically (AdditionalTexts order is not guaranteed by
/// Roslyn), and JNT6002 names every candidate so the ambiguity is visible
/// instead of a silently-wrong schema.
/// </summary>
public class MultipleSchemaSnapshotTests
{
    private const string AlphaSchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""alpha"": {
      ""name"": ""alpha"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true }
      }
    }
  }
}";

    private const string ZetaSchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""zeta"": {
      ""name"": ""zeta"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(params (string path, string text)[] additionalFiles)
    {
        var compilation = CSharpCompilation.Create("MultiSchemaTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(additionalFiles
                .Select(f => (AdditionalText)new InMemoryAdditionalText(f.path, f.text))
                .ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Fact]
    public void TwoSnapshots_ReportsJNT6002_NamingBoth()
    {
        var result = Run(
            ("db/Alpha/GetAll.sql", "select id from alpha"),
            ("schema/a.schema.json", AlphaSchemaJson),
            ("schema/z.schema.json", ZetaSchemaJson));

        var warning = Assert.Single(result.Diagnostics, d => d.Id == "JNT6002");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        string message = warning.GetMessage();
        Assert.Contains("schema/a.schema.json", message);
        Assert.Contains("schema/z.schema.json", message);
    }

    [Fact]
    public void TwoSnapshots_OrdinalLowestPathWins_RegardlessOfRegistrationOrder()
    {
        // Register the ordinal-HIGHER path first: the winner must still be
        // the ordinal-lowest ('schema/a.schema.json' → table alpha), so a
        // query against alpha validates and one against zeta fails JNT2001.
        var result = Run(
            ("db/Alpha/GetAll.sql", "select id from alpha"),
            ("schema/z.schema.json", ZetaSchemaJson),
            ("schema/a.schema.json", AlphaSchemaJson));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2001");
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Alpha.GetAll.g.cs");
    }

    [Fact]
    public void TwoSnapshots_LosingSchemaIsNotConsulted()
    {
        var result = Run(
            ("db/Zeta/GetAll.sql", "select id from zeta"),
            ("schema/z.schema.json", ZetaSchemaJson),
            ("schema/a.schema.json", AlphaSchemaJson));

        // zeta only exists in the losing snapshot → table-not-found.
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT2001");
    }

    [Fact]
    public void SingleSnapshot_NoJNT6002()
    {
        var result = Run(
            ("db/Alpha/GetAll.sql", "select id from alpha"),
            ("schema/a.schema.json", AlphaSchemaJson));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT6002");
    }
}
