using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Build-time JNT9004 (Migration Risky Impact): a migration that changes a
/// referenced column without invalidating the query surfaces as a non-fatal
/// warning; the build still succeeds. BREAKING changes continue to fail via the
/// existing JNT2xxx errors and do not also emit JNT9004.
/// </summary>
public class MigrationImpactWarningTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40, ""isUnicode"": true },
        ""reorder_level"": { ""name"": ""reorder_level"", ""dbType"": ""smallint"", ""isNullable"": true }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("MigrationImpactTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText> { new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson) };
        foreach (var (path, text) in files)
            texts.Add(new InMemoryAdditionalText(path, text));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Fact]
    public void WidenedReferencedColumn_EmitsJNT9004_BuildStillSucceeds()
    {
        var result = Run(
            ("db/Products/GetName.sql", "select product_id, product_name\nfrom products"),
            ("db/migrations/0001_widen_name.sql", "alter table products alter column product_name nvarchar(80) not null"));

        var warning = Assert.Single(result.Diagnostics, d => d.Id == "JNT9004");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("product_name", warning.GetMessage());

        // build still succeeds: no errors, and the query still emits
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Products.GetName.g.cs");
    }

    [Fact]
    public void DroppedReferencedColumn_IsBreaking_NoJNT9004()
    {
        var result = Run(
            ("db/Products/GetReorder.sql", "select product_id, reorder_level\nfrom products"),
            ("db/migrations/0001_drop_reorder.sql", "alter table products drop column reorder_level"));

        // BREAKING keeps failing via the existing validation error, not JNT9004
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT2002");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT9004");
    }

    [Fact]
    public void UnreferencedChange_IsSafe_NoWarning()
    {
        var result = Run(
            ("db/Products/GetId.sql", "select product_id\nfrom products"),
            ("db/migrations/0001_widen_name.sql", "alter table products alter column product_name nvarchar(80) not null"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT9004");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NoMigrations_NoImpactWarnings()
    {
        var result = Run(("db/Products/GetName.sql", "select product_id, product_name\nfrom products"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT9004");
    }
}
