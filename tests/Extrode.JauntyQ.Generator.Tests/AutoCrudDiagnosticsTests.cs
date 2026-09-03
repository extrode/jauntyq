using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// Auto-CRUD synthesis validates every synthetic query through the same
/// QueryValidator as hand-written files (JauntyQGenerator.Part4.cs). Errors
/// there mean a synthesis bug, so emission is skipped rather than surfaced.
/// Warnings (JNT8xxx performance advice) are real findings about the user's
/// own schema and must still reach the build output, not be discarded.
/// </summary>
public class AutoCrudDiagnosticsTests
{
    // rowversion column is never indexed (concurrency tokens never are);
    // the "sku" index exists purely so the snapshot carries index metadata
    // at all (JNT8004 is skipped entirely for schemas with none).
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""widget_id"": { ""name"": ""widget_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""sku"": { ""name"": ""sku"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 20 },
        ""row_version"": { ""name"": ""row_version"", ""dbType"": ""timestamp"", ""isNullable"": false, ""isRowVersion"": true }
      },
      ""indexes"": [
        { ""name"": ""ix_widgets_sku"", ""columns"": [""sku""], ""isUnique"": true }
      ]
    }
  }
}";

    private static GeneratorDriverRunResult Run()
    {
        var compilation = CSharpCompilation.Create("AutoCrudDiagnosticsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Fact]
    public void SyntheticUpdate_RowVersionBesidePkSeek_NoJNT8004()
    {
        // Inverted 2026-08-01 (was ReportsJNT8004): the synthetic Update's
        // WHERE widget_id = @ AND row_version = @ seeks the primary key to at
        // most one row, so the residual row_version check scans nothing.
        // JNT8004 is now suppressed for a column when the query's equality
        // filters on the same table instance cover a complete PK or unique
        // index — this shape, reachable through auto-CRUD alone, was the
        // motivating noise class.
        var result = Run();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004" &&
            d.GetMessage().Contains("widgets.row_version"));
    }

    [Fact]
    public void SyntheticUpdate_StillEmitted_WarningDoesNotBlockGeneration()
    {
        var result = Run();

        // A warning must never suppress emission the way an Error does.
        Assert.Contains(result.Results[0].GeneratedSources,
            s => s.HintName == "Widgets.Update.auto.g.cs");
    }
}
