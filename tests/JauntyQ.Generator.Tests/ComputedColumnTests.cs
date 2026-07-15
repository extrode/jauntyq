using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// A computed/generated column (SQL Server <c>AS ...</c>, PostgreSQL
/// <c>GENERATED ALWAYS AS (...) STORED</c>, MySQL <c>GENERATED ALWAYS AS
/// (...)</c>) is database-assigned: the database rejects an INSERT/UPDATE
/// that targets one. Auto-CRUD, BulkInsert, and Upsert must all exclude it
/// from their column/value lists the same way they already exclude identity
/// and rowversion columns.
/// </summary>
public class ComputedColumnTests
{
    // Identity PK, so Upsert is skipped here (mirrors RowVersionTests' gadgets
    // table) — Insert/Update/BulkInsert coverage.
    private const string IdentityKeySchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""Widgets"": {
      ""name"": ""Widgets"",
      ""columns"": {
        ""WidgetId"": { ""name"": ""WidgetId"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""Name"": { ""name"": ""Name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 20, ""isUnicode"": true },
        ""FullName"": { ""name"": ""FullName"", ""dbType"": ""nvarchar"", ""isNullable"": true, ""maxLength"": 50, ""isUnicode"": true, ""isComputed"": true }
      }
    }
  }
}";

    // Natural (non-identity) PK, so Upsert IS synthesized — covers EmitUpsert's
    // own column filtering as well.
    private const string NaturalKeySchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""Widgets"": {
      ""name"": ""Widgets"",
      ""columns"": {
        ""Code"": { ""name"": ""Code"", ""dbType"": ""varchar"", ""isNullable"": false, ""isPrimaryKey"": true, ""maxLength"": 10 },
        ""Name"": { ""name"": ""Name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 20, ""isUnicode"": true },
        ""FullName"": { ""name"": ""FullName"", ""dbType"": ""nvarchar"", ""isNullable"": true, ""maxLength"": 50, ""isUnicode"": true, ""isComputed"": true }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(string schemaJson)
    {
        var compilation = CSharpCompilation.Create("ComputedColumnTestAssembly",
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

    private static string Source(GeneratorDriverRunResult result, string hintName) =>
        result.Results[0].GeneratedSources.Single(s => s.HintName == hintName).SourceText.ToString();

    private static string AllSources(GeneratorDriverRunResult result) =>
        string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

    [Fact]
    public void Insert_ExcludesComputedColumn()
    {
        var result = Run(IdentityKeySchema);
        string source = Source(result, "Widgets.Insert.auto.g.cs");

        // WidgetId (identity) and FullName (computed) are both database-
        // assigned: only Name is a caller-supplied argument.
        Assert.Contains("public int Insert(string Name)", source);
        Assert.Contains("insert into Widgets (Name)", source);
        Assert.DoesNotContain("insert into Widgets (Name, FullName", source);
    }

    [Fact]
    public void Update_ExcludesComputedColumnFromSet()
    {
        var result = Run(IdentityKeySchema);
        string source = Source(result, "Widgets.Update.auto.g.cs");

        Assert.Contains("set Name = @Name", source);
        Assert.DoesNotContain("FullName = @FullName", source);
    }

    [Fact]
    public void PocoInsert_DoesNotForwardTheComputedColumn()
    {
        var result = Run(IdentityKeySchema);
        string source = Source(result, "Widgets.Poco.auto.g.cs");

        // The identity id is written back, Name is the only bound argument —
        // FullName never appears in the forwarding call.
        Assert.Contains("row.Name", source);
        Assert.DoesNotContain("row.FullName", source);
    }

    [Fact]
    public void BulkInsert_ExcludesComputedColumn()
    {
        var result = Run(IdentityKeySchema);
        var src = AllSources(result);

        Assert.Contains("insert into Widgets (Name)", src);
        Assert.DoesNotContain("insert into Widgets (Name, FullName", src);
    }

    [Fact]
    public void Upsert_ExcludesComputedColumn_FromInsertAndUpdateSet()
    {
        var result = Run(NaturalKeySchema);
        string source = Source(result, "Widgets.Upsert.auto.g.cs");

        Assert.Contains("when not matched then insert (Code, Name) values (src.Code, src.Name)", source);
        Assert.Contains("when matched then update set Name = src.Name", source);
        Assert.DoesNotContain("FullName", source);
    }

    [Fact]
    public void GeneratedCode_ParsesClean_WithComputedColumnPresent()
    {
        foreach (var schema in new[] { IdentityKeySchema, NaturalKeySchema })
        {
            var result = Run(schema);
            foreach (var gen in result.Results[0].GeneratedSources)
            {
                var tree = CSharpSyntaxTree.ParseText(gen.SourceText.ToString());
                var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                Assert.True(errors.Count == 0,
                    $"generated code broke in {gen.HintName}: {string.Join("; ", errors.Select(e => e.GetMessage()))}");
            }
        }
    }
}
