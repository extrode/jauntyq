using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// DDL-as-schema-source: with no pulled JSON snapshot, db/ddl/*.sql CREATE TABLE
/// files define the base schema (dialect from &lt;JauntyQDialect&gt;), reusing the
/// exact MigrationParser/SchemaSimulator machinery. A JSON snapshot always wins
/// when present; db/migrations/*.sql still applies on top of whichever base
/// results. Missing/unknown dialect in DDL mode is JNT9003.
/// </summary>
public class DdlSchemaSourceTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40, ""isUnicode"": true }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(
        bool autoCrud, string? dialect, bool includeJson, params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("DdlTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText>();
        if (includeJson)
            texts.Add(new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson));
        foreach (var (path, text) in files)
            texts.Add(new InMemoryAdditionalText(path, text));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud, dialect));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static bool HasSource(GeneratorDriverRunResult result, string hintName) =>
        result.Results[0].GeneratedSources.Any(s => s.HintName == hintName);

    private static string Source(GeneratorDriverRunResult result, string hintName) =>
        result.Results[0].GeneratedSources.Single(s => s.HintName == hintName).SourceText.ToString();

    [Fact]
    public void DdlOnly_NoSnapshot_ProducesFullTypedApi()
    {
        var result = Run(autoCrud: true, dialect: "sqlite", includeJson: false,
            ("db/ddl/001_tables.sql", @"
create table widgets (
    widget_id integer not null primary key,
    name text not null
);
create table gizmos (
    gizmo_id integer not null primary key,
    label text not null
);"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        // Both DDL-defined tables get full auto-CRUD from the ddl-built schema.
        Assert.True(HasSource(result, "Widgets.GetAll.auto.g.cs"));
        Assert.True(HasSource(result, "Widgets.GetById.auto.g.cs"));
        Assert.True(HasSource(result, "Gizmos.GetAll.auto.g.cs"));
        Assert.Contains("public required string Name { get; set; }", Source(result, "Widgets.Row.g.cs"));
    }

    [Fact]
    public void DdlPresent_ButMissingDialect_JNT9003_NoSchemaEmission()
    {
        var result = Run(autoCrud: true, dialect: null, includeJson: false,
            ("db/ddl/001_tables.sql", "create table widgets (widget_id integer not null primary key, name text not null)"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT9003");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("JauntyQDialect", diag.GetMessage());
        Assert.False(HasSource(result, "Widgets.GetAll.auto.g.cs"));
    }

    [Fact]
    public void DdlPresent_ButGarbageDialect_JNT9003_NoSchemaEmission()
    {
        var result = Run(autoCrud: true, dialect: "cockroach", includeJson: false,
            ("db/ddl/001_tables.sql", "create table widgets (widget_id integer not null primary key, name text not null)"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT9003");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.False(HasSource(result, "Widgets.GetAll.auto.g.cs"));
    }

    [Fact]
    public void JsonSnapshotPresent_DdlFilesSilentlyIgnored_NotMergedOrErrored()
    {
        // Both a JSON snapshot AND ddl files: the snapshot wins, the ddl files
        // (which would otherwise create a 'widgets' table) are simply not read.
        var result = Run(autoCrud: true, dialect: "sqlite", includeJson: true,
            ("db/ddl/001_tables.sql", "create table widgets (widget_id integer not null primary key, name text not null)"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT9003");

        // Snapshot table is present; ddl-only table is absent (proves no merge).
        Assert.True(HasSource(result, "Products.GetAll.auto.g.cs"));
        Assert.False(HasSource(result, "Widgets.GetAll.auto.g.cs"));
    }

    [Fact]
    public void DdlBase_PlusMigrationOnTop_MigrationAppliesToDdlBuiltSchema()
    {
        var result = Run(autoCrud: true, dialect: "sqlite", includeJson: false,
            ("db/ddl/001_tables.sql", "create table widgets (widget_id integer not null primary key, name text not null)"),
            ("db/migrations/0001_add_color.sql", "alter table widgets add column color text not null"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        // The migration's added column shows up in the DDL-built table's row POCO.
        var rowPoco = Source(result, "Widgets.Row.g.cs");
        Assert.Contains("public required string Name { get; set; }", rowPoco);
        Assert.Contains("public required string Color { get; set; }", rowPoco);
    }
}
