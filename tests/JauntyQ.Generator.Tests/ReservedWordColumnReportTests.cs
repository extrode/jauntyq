using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Spec 015 T7: R17/R18 needed VERIFYING, not building.
///
/// The justsmtp report lists "a column named <c>count</c> silently kills a
/// table's auto-CRUD, with no diagnostic pointing at the cause" as a known
/// gotcha costing an afternoon. JNT2015 was added on 2026-07-29 (27c23e0) for
/// exactly that, but three weeks AFTER the v0.1.0 tag the consumer is pinned
/// to — so they have never had it, and v0.2.0 is the first release that
/// reports the case.
///
/// This pins their literal scenario rather than a near neighbor, so the claim
/// made back to them is one the suite actually executes.
/// </summary>
public class ReservedWordColumnReportTests
{
    // The reported shape: an ordinary table whose column is named `count`.
    private const string SchemaWithCountColumn = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""tallies"": {
      ""name"": ""tallies"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""count"": { ""name"": ""count"", ""dbType"": ""int"", ""isNullable"": false }
      },
      ""indexes"": []
    }
  }
}";

    private const string SchemaWithoutCountColumn = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""tallies"": {
      ""name"": ""tallies"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""tally"": { ""name"": ""tally"", ""dbType"": ""int"", ""isNullable"": false }
      },
      ""indexes"": []
    }
  }
}";

    private static GeneratorDriverRunResult Run(string schemaJson)
    {
        var compilation = CSharpCompilation.Create("ReservedWordTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Fact]
    public void CountColumn_IsReported_NotSilentlySkipped()
    {
        var result = Run(SchemaWithCountColumn);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT2015");
    }

    [Fact]
    public void TheDiagnostic_NamesTheTableAndTheOffendingColumn()
    {
        var result = Run(SchemaWithCountColumn);

        // The report's ask was "a JNT warning naming the column would have
        // saved an afternoon" — naming the table alone would not have.
        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT2015");
        string message = diag.GetMessage();
        Assert.Contains("tallies", message);
        Assert.Contains("count", message);
    }

    [Fact]
    public void TheDiagnostic_SaysGenerationWasSuppressed_RatherThanMerelyEmpty()
    {
        var result = Run(SchemaWithCountColumn);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT2015");
        // R18: distinguishable from "there was nothing to generate".
        Assert.Contains("No auto-CRUD is generated", diag.GetMessage());
    }

    [Fact]
    public void AnEquivalentTableWithoutTheReservedWord_IsNotReported()
    {
        var result = Run(SchemaWithoutCountColumn);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2015");
    }
}
