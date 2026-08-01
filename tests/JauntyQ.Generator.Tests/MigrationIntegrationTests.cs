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

    private static GeneratorDriverRunResult Run(bool autoCrud, params (string Path, string Text)[] files) =>
        Run(autoCrud, SchemaJson, files);

    private static GeneratorDriverRunResult Run(bool autoCrud, string schemaJson, params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("MigrationTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText> { new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson) };
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
        Assert.Contains("AND row_version = @row_version", Source(result, "Gadgets.Update.auto.g.cs"));
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

    // ── A pending migration must not erase snapshot parts the simulator ──
    // ── doesn't mutate (SchemaSimulator.Clone once dropped all three)   ──

    private const string RichSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40 },
        ""category_id"": { ""name"": ""category_id"", ""dbType"": ""int"", ""isNullable"": true },
        ""search_label"": { ""name"": ""search_label"", ""dbType"": ""nvarchar"", ""isNullable"": true, ""maxLength"": 80, ""isComputed"": true }
      },
      ""indexes"": [
        { ""name"": ""ix_products_category"", ""columns"": [""category_id""], ""isUnique"": false }
      ]
    }
  },
  ""procedures"": {
    ""GetTopProducts"": {
      ""name"": ""GetTopProducts"",
      ""params"": [
        { ""name"": ""HowMany"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""TotalValue"", ""dbType"": ""decimal"", ""direction"": ""Out"", ""isNullable"": false, ""precision"": 12, ""scale"": 4 }
      ],
      ""results"": [ { ""name"": ""ProductId"", ""dbType"": ""int"", ""isNullable"": false } ]
    }
  },
  ""sequences"": {
    ""order_number"": { ""name"": ""order_number"", ""startValue"": 100, ""increment"": 5 }
  }
}";

    private const string UnrelatedMigration = "create table gadgets (id int not null primary key)";

    [Fact]
    public void PendingMigration_DoesNotErase_Procedures()
    {
        var result = Run(autoCrud: false, RichSchemaJson,
            ("db/Products/CallTop.sql", "-- @call GetTopProducts"),
            ("db/migrations/0001_unrelated.sql", UnrelatedMigration));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2005");
        Assert.True(HasSource(result, "Products.CallTop.g.cs"));
    }

    [Fact]
    public void PendingMigration_DoesNotErase_ProcedureParamPrecisionAndScale()
    {
        // SchemaSimulator.Clone once copied ProcedureParam without its
        // Precision/Scale fields: a decimal OUT param, cloned only because
        // an unrelated migration exists elsewhere, would come back out of
        // the simulator with both null, and the emitted DbParameter would
        // carry no Precision/Scale -- the exact silent truncation/rounding
        // hazard those fields exist to prevent (see ProcCallTests'
        // no-migration counterpart, which stays green either way since it
        // never routes through the simulator).
        var result = Run(autoCrud: false, RichSchemaJson,
            ("db/Products/CallTop.sql", "-- @call GetTopProducts"),
            ("db/migrations/0001_unrelated.sql", UnrelatedMigration));

        string src = Source(result, "Products.CallTop.g.cs");
        Assert.Contains(".Precision = 12;", src);
        Assert.Contains(".Scale = 4;", src);
    }

    [Fact]
    public void PendingMigration_DoesNotErase_Sequences()
    {
        var result = Run(autoCrud: false, RichSchemaJson,
            ("db/migrations/0001_unrelated.sql", UnrelatedMigration));

        var db = Source(result, "JauntyDb.g.cs");
        Assert.Contains("SequenceAccessor Sequences", db);
        Assert.Contains("NextOrderNumber()", db);
    }

    [Fact]
    public void PendingMigration_DoesNotErase_Indexes()
    {
        // category_id is indexed: filtering on it must not warn JNT8004, with
        // or without a pending migration.
        var result = Run(autoCrud: false, RichSchemaJson,
            ("db/Products/GetByCategory.sql", "select product_id\nfrom products\nwhere category_id = @categoryId"),
            ("db/migrations/0001_unrelated.sql", UnrelatedMigration));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004");
        Assert.True(HasSource(result, "Products.GetByCategory.g.cs"));
    }

    [Fact]
    public void PendingMigration_DoesNotErase_ComputedColumnFlag()
    {
        // SchemaSimulator.Clone once dropped IsComputed too: a table with a
        // computed column, cloned only because an unrelated migration exists
        // elsewhere, would come back out of the simulator with that column
        // looking like an ordinary writable one -- AutoCrud would then target
        // it in Insert/Update, which the database rejects at runtime.
        var result = Run(autoCrud: true, RichSchemaJson,
            ("db/migrations/0001_unrelated.sql", UnrelatedMigration));

        Assert.Contains("string product_name", Source(result, "Products.Insert.auto.g.cs"));
        Assert.DoesNotContain("search_label", Source(result, "Products.Insert.auto.g.cs"));
        Assert.DoesNotContain("search_label", Source(result, "Products.Update.auto.g.cs"));
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

    [Fact]
    public void Migrations_UnpaddedFlywayStyleNumbering_AppliesInNumericOrder_NotOrdinal()
    {
        // A plain ordinal sort misorders unpadded Flyway-style names: "V10"
        // and "V11" sort ordinally BEFORE "V2".."V9" (V1, V10, V11, V2, V3,
        // ...). Here V2 adds a column that V10 then widens -- ordinally,
        // V10 (widen) would run before V2 (add) even exists, which the
        // simulator would have to reject as an invalid operation on a
        // nonexistent column. Numeric order (V1, V2, ..., V9, V10, V11)
        // applies them correctly and the widened column survives to V11.
        var result = Run(autoCrud: true,
            ("db/migrations/V1__create_widgets.sql", "create table widgets (id int not null primary key, name nvarchar(10) not null)"),
            ("db/migrations/V10__widen_name.sql", "alter table widgets alter column name nvarchar(80) not null"),
            ("db/migrations/V11__noop.sql", "-- no-op, just to occupy a slot after V10"),
            ("db/migrations/V2__add_flag.sql", "alter table widgets add column active bit not null"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("public required string Name { get; set; }", Source(result, "Widgets.Row.g.cs"));
        Assert.Contains("public bool Active { get; set; }", Source(result, "Widgets.Row.g.cs"));
        // Widened by V10 to 80: the value-safety guard reflects the final width.
        Assert.Contains("if (name.Length > 80)", Source(result, "Widgets.Insert.auto.g.cs"));
    }

    private const string PostgresEnumSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""enums"": {
    ""order_status"": {
      ""name"": ""order_status"",
      ""members"": [
        { ""value"": ""pending"", ""csharpName"": ""Pending"" },
        { ""value"": ""shipped"", ""csharpName"": ""Shipped"" }
      ]
    }
  },
  ""tables"": {
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""status"": { ""name"": ""status"", ""dbType"": ""order_status"", ""isNullable"": false, ""enumName"": ""order_status"" }
      }
    }
  }
}";

    private const string MySqlEnumSchemaJson = @"{
  ""dialect"": ""mysql"",
  ""enums"": {
    ""OrdersStatus"": {
      ""name"": ""OrdersStatus"",
      ""members"": [
        { ""value"": ""pending"", ""csharpName"": ""Pending"" },
        { ""value"": ""shipped"", ""csharpName"": ""Shipped"" }
      ]
    }
  },
  ""tables"": {
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""status"": { ""name"": ""status"", ""dbType"": ""enum"", ""isNullable"": false, ""enumName"": ""OrdersStatus"" }
      }
    }
  }
}";

    [Fact]
    public void PendingMigration_DoesNotErase_Enums_Postgres()
    {
        // SchemaSimulator.Clone must carry DatabaseSchema.Enums and each
        // column's EnumName: cloned only because an unrelated migration exists,
        // a Postgres enum column would otherwise degrade to the unmapped-type
        // fallback -- a FALSE JNT2007 (the type exists and was captured) and an
        // object-typed property where the previous build emitted the enum.
        var result = Run(autoCrud: true, PostgresEnumSchemaJson,
            ("db/migrations/0001_unrelated.sql", "create table audit_log (event_id int not null primary key)"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2007");
        Assert.True(HasSource(result, "Enums.g.cs"));
        Assert.Contains("public OrderStatus Status", Source(result, "Orders.Row.g.cs"));
    }

    [Fact]
    public void PendingMigration_DoesNotErase_Enums_MySql()
    {
        // The MySQL shape is the silent one: DbType is the bare word "enum",
        // which has its own string arm in DialectMapper -- with the linkage
        // lost in the clone the column maps to string with NO diagnostic at
        // all, the Enums.g.cs file vanishes, and every write site quietly
        // stops binding the wire value.
        var result = Run(autoCrud: true, MySqlEnumSchemaJson,
            ("db/migrations/0001_unrelated.sql", "create table audit_log (event_id int not null primary key)"));

        Assert.True(HasSource(result, "Enums.g.cs"));
        Assert.Contains("public OrdersStatus Status", Source(result, "Orders.Row.g.cs"));
        Assert.DoesNotContain("public required string Status", Source(result, "Orders.Row.g.cs"));
    }
}
