using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Tier 3 migration intelligence, end to end: pending migrations under
/// db/migrations/ are applied to the snapshot, and the generator validates
/// and emits against the EFFECTIVE schema. A breaking migration fails the
/// build (JNT2xxx/JNT5xxx against the future world); a CREATE TABLE yields
/// the full typed API before the table exists in any database.
/// </summary>
public class MigrationIntegrationTests
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

    private static GeneratorDriverRunResult Run(bool autoCrud, params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("MigrationTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText> { new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson) };
        foreach (var (path, text) in files)
            texts.Add(new InMemoryAdditionalText(path, text));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static bool HasSource(GeneratorDriverRunResult result, string hintName) =>
        result.Results[0].GeneratedSources.Any(s => s.HintName == hintName);

    private static string Source(GeneratorDriverRunResult result, string hintName) =>
        result.Results[0].GeneratedSources.Single(s => s.HintName == hintName).SourceText.ToString();

    [Fact]
    public void CreateTableMigration_YieldsFullTypedApi_BeforeTheTableExistsAnywhere()
    {
        var result = Run(autoCrud: true,
            ("db/migrations/0001_create_gadgets.sql", @"
create table gadgets (
    gadget_id int not null primary key identity(1,1),
    name nvarchar(20) not null,
    row_version rowversion not null
)"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        // full auto-CRUD, identity-returning insert, rowversion-guarded update
        Assert.True(HasSource(result, "Gadgets.GetAll.auto.g.cs"));
        Assert.True(HasSource(result, "Gadgets.GetById.auto.g.cs"));
        Assert.Contains("public int Insert(string name)", Source(result, "Gadgets.Insert.auto.g.cs"));
        Assert.Contains("and row_version = @row_version", Source(result, "Gadgets.Update.auto.g.cs"));
        Assert.Contains("public byte[]? RowVersion { get; set; }", Source(result, "Gadgets.Row.g.cs"));

        // value safety came along for free: name is nvarchar(20)
        Assert.Contains("if (name.Length > 20)", Source(result, "Gadgets.Update.auto.g.cs"));
    }

    [Fact]
    public void MigrationFile_IsNotTreatedAsAQueryEntity()
    {
        var result = Run(autoCrud: false,
            ("db/migrations/0001_create_gadgets.sql", "create table gadgets (id int not null primary key)"));

        Assert.DoesNotContain(result.Results[0].GeneratedSources,
            s => s.HintName.StartsWith("migrations.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT1001");
    }

    [Fact]
    public void DroppingAColumnAQueryUses_FailsTheBuild()
    {
        var result = Run(autoCrud: false,
            ("db/Products/GetReorderLevels.sql", "select product_id, reorder_level\nfrom products"),
            ("db/migrations/0001_drop_reorder.sql", "alter table products drop column reorder_level"));

        // the query is validated against the POST-migration world
        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT2002");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.False(HasSource(result, "Products.GetReorderLevels.g.cs"));
    }

    [Fact]
    public void ShrinkingAColumn_MakesOversizeLiteralsFail()
    {
        var result = Run(autoCrud: false,
            ("db/Products/GetSpecial.sql", "select product_id\nfrom products\nwhere products.product_name = 'Twenty-two characters!'"),
            ("db/migrations/0001_shrink_name.sql", "alter table products alter column product_name nvarchar(10) not null"));

        // fits nvarchar(40) today; the pending migration shrinks it to 10
        Assert.Single(result.Diagnostics, d => d.Id == "JNT5001");
    }

    [Fact]
    public void WithoutTheMigration_TheSameQueryIsFine()
    {
        var result = Run(autoCrud: false,
            ("db/Products/GetSpecial.sql", "select product_id\nfrom products\nwhere products.product_name = 'Twenty-two characters!'"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5001");
        Assert.True(HasSource(result, "Products.GetSpecial.g.cs"));
    }

    [Fact]
    public void AlreadyAppliedMigration_JNT9002_WithArchiveHint()
    {
        var result = Run(autoCrud: false,
            ("db/migrations/0001_create_products.sql", "create table products (x int not null primary key)"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT9002");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("already exists", diag.GetMessage());
        Assert.Contains("archive it", diag.GetMessage());
    }

    [Fact]
    public void UnsupportedStatement_JNT9001Warning_RestStillApplies()
    {
        var result = Run(autoCrud: true,
            ("db/migrations/0001_mixed.sql", @"
exec sp_rename 'products.a', 'b', 'COLUMN';
create table gadgets (id int not null primary key, name nvarchar(20) not null);
"));

        var warning = Assert.Single(result.Diagnostics, d => d.Id == "JNT9001");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.True(HasSource(result, "Gadgets.GetAll.auto.g.cs"));
    }

    [Fact]
    public void Migrations_ApplyInFilenameOrder()
    {
        var result = Run(autoCrud: true,
            // deliberately supplied out of order
            ("db/migrations/0002_drop_gadgets.sql", "drop table gadgets"),
            ("db/migrations/0001_create_gadgets.sql", "create table gadgets (id int not null primary key)"));

        // 0001 creates, 0002 drops: net effect is no gadgets entity, no errors
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.False(HasSource(result, "Gadgets.GetAll.auto.g.cs"));
    }
}
