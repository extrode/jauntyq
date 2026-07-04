using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class AutoCrudTests
{
    private const string PkSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""category_id"": { ""name"": ""category_id"", ""dbType"": ""int"", ""isNullable"": true }
      }
    },
    ""audit_view"": {
      ""name"": ""audit_view"",
      ""columns"": {
        ""event_id"": { ""name"": ""event_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""detail"": { ""name"": ""detail"", ""dbType"": ""varchar"", ""isNullable"": true }
      }
    },
    ""categories"": {
      ""name"": ""categories"",
      ""columns"": {
        ""category_id"": { ""name"": ""category_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""category_name"": { ""name"": ""category_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    },
    ""customers"": {
      ""name"": ""customers"",
      ""columns"": {
        ""customer_id"": { ""name"": ""customer_id"", ""dbType"": ""varchar"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""company_name"": { ""name"": ""company_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""products"", ""fromColumn"": ""category_id"", ""toTable"": ""categories"", ""toColumn"": ""category_id"" }
  ]
}";

    private static (GeneratorDriverRunResult result, Compilation compilation) RunAutoCrud(
        bool autoCrud = true, params (string path, string sql)[] sqlFiles)
        => RunAutoCrudWithSchema(PkSchemaJson, autoCrud, sqlFiles);

    private static (GeneratorDriverRunResult result, Compilation compilation) RunAutoCrudWithSchema(
        string schemaJson, bool autoCrud = true, params (string path, string sql)[] sqlFiles)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("");
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.Common.DbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
        };

        var compilation = CSharpCompilation.Create("AutoCrudTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText>();
        foreach (var (path, sql) in sqlFiles)
            texts.Add(new InMemoryAdditionalText(path, sql));
        texts.Add(new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.CreateRange(texts))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver.GetRunResult(), outputCompilation);
    }

    private static string? TryGetSource(GeneratorDriverRunResult result, string hintSuffix)
    {
        foreach (var tree in result.GeneratedTrees)
        {
            if (tree.FilePath.EndsWith(hintSuffix, StringComparison.OrdinalIgnoreCase))
                return tree.ToString();
        }
        return null;
    }

    // ── driver-level ───────────────────────────────────────

    [Fact]
    public void SchemaOnly_NoSqlFiles_GeneratesFullCrud()
    {
        var (result, compilation) = RunAutoCrud();

        Assert.NotNull(TryGetSource(result, "Products.GetAll.auto.g.cs"));
        Assert.NotNull(TryGetSource(result, "Products.GetById.auto.g.cs"));
        Assert.NotNull(TryGetSource(result, "Products.Insert.auto.g.cs"));
        Assert.NotNull(TryGetSource(result, "Products.Update.auto.g.cs"));
        Assert.NotNull(TryGetSource(result, "Products.Delete.auto.g.cs"));
        Assert.NotNull(TryGetSource(result, "JauntyDb.g.cs"));

        // Everything must actually compile
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void GetById_IsSingleRowAndTyped_ReturnsCanonicalPoco()
    {
        var (result, _) = RunAutoCrud();

        var source = TryGetSource(result, "Products.GetById.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("public Product? GetById(int product_id)", source);
        Assert.Contains("System.Threading.Tasks.Task<Product?> GetByIdAsync(", source);
        Assert.Contains("p0.DbType = System.Data.DbType.Int32;", source);
        Assert.Contains("Product.Read(reader)", source);
        Assert.DoesNotContain("Result.GetById", source);
    }

    [Fact]
    public void CanonicalRowPoco_EmittedWithSharedRead()
    {
        var (result, _) = RunAutoCrud();

        var poco = TryGetSource(result, "Products.Row.g.cs");
        Assert.NotNull(poco);
        Assert.Contains("public class Product", poco);
        Assert.Contains("public required int ProductId { get; set; }", poco);
        Assert.Contains("public static Product Read(System.Data.Common.DbDataReader reader)", poco);

        var getAll = TryGetSource(result, "Products.GetAll.auto.g.cs");
        Assert.NotNull(getAll);
        Assert.Contains("System.Collections.Generic.List<Product> GetAll(", getAll);
    }

    [Fact]
    public void FkLoader_Synthesized_FromForeignKeys()
    {
        var (result, _) = RunAutoCrud();

        var source = TryGetSource(result, "Products.GetByCategoryId.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("List<Product> GetByCategoryId(int? category_id)", source);
        Assert.Contains("where products.category_id = @category_id", source);
    }

    [Fact]
    public void Upsert_Synthesized_ForNonIdentityKey_SkippedForIdentityKey()
    {
        var (result, compilation) = RunAutoCrud();

        // customers: string PK, not identity -> upsert exists (sqlserver MERGE)
        var upsert = TryGetSource(result, "Customers.Upsert.auto.g.cs");
        Assert.NotNull(upsert);
        Assert.Contains("merge into customers with (holdlock) as target", upsert);
        Assert.Contains("when matched then update set company_name = src.company_name", upsert);
        Assert.Contains("public int Upsert(", upsert);
        Assert.Contains("UpsertAsync(", upsert);

        // products: identity PK -> no upsert
        Assert.Null(TryGetSource(result, "Products.Upsert.auto.g.cs"));

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Upsert_PostgresAndMySql_DialectSql()
    {
        var (pg, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"postgres\""));
        var pgSource = TryGetSource(pg, "Customers.Upsert.auto.g.cs");
        Assert.NotNull(pgSource);
        Assert.Contains("on conflict (customer_id) do update set company_name = excluded.company_name", pgSource);

        var (my, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"mysql\""));
        var mySource = TryGetSource(my, "Customers.Upsert.auto.g.cs");
        Assert.NotNull(mySource);
        Assert.Contains("on duplicate key update company_name = values(company_name)", mySource);
    }

    [Fact]
    public void Insert_ExcludesIdentityColumn()
    {
        var (result, _) = RunAutoCrud();

        var source = TryGetSource(result, "Products.Insert.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("insert into products (product_name, category_id)", source);
        Assert.DoesNotContain("@product_id", source);
    }

    [Fact]
    public void UserSqlFile_OverridesSynthetic()
    {
        var (result, _) = RunAutoCrud(autoCrud: true,
            ("db/Products/GetAll.sql", "select p.product_id from products p"));

        // User's projection (one column), not the synthetic full projection
        Assert.Null(TryGetSource(result, "Products.GetAll.auto.g.cs"));
        var userSource = TryGetSource(result, "Products.GetAll.g.cs");
        Assert.NotNull(userSource);
        Assert.DoesNotContain("ProductName", userSource);
    }

    [Fact]
    public void PkLessTable_GetsGetAllOnly()
    {
        var (result, _) = RunAutoCrud();

        Assert.NotNull(TryGetSource(result, "AuditView.GetAll.auto.g.cs"));
        Assert.Null(TryGetSource(result, "AuditView.GetById.auto.g.cs"));
        Assert.Null(TryGetSource(result, "AuditView.Insert.auto.g.cs"));
        Assert.Null(TryGetSource(result, "AuditView.Update.auto.g.cs"));
        Assert.Null(TryGetSource(result, "AuditView.Delete.auto.g.cs"));
    }

    [Fact]
    public void Disabled_ByMsBuildProperty_GeneratesNoSynthetics()
    {
        var (result, _) = RunAutoCrud(autoCrud: false);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains(".auto.g.cs"));
    }

    // ── -- @identity (Tier 1) ──────────────────────────────

    [Fact]
    public void SyntheticInsert_SqlServer_ReturnsIdentityViaOutputClause()
    {
        var (result, _) = RunAutoCrud();

        var source = TryGetSource(result, "Products.Insert.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("output inserted.product_id", source);
        Assert.Contains("public int Insert(", source);
        Assert.Contains("INSERT did not return an identity value.", source);
        Assert.Contains("reader.GetInt32(0)", source);
        Assert.DoesNotContain("ExecuteNonQuery", source);
    }

    [Fact]
    public void SyntheticInsert_Postgres_UsesReturning()
    {
        var (result, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"postgres\""));

        var source = TryGetSource(result, "Products.Insert.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("returning product_id", source);
    }

    [Fact]
    public void SyntheticInsert_MySql_UsesLastInsertId_WithCheckedNarrowing()
    {
        var (result, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"mysql\""));

        var source = TryGetSource(result, "Products.Insert.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("select last_insert_id()", source);
        Assert.Contains("checked((int)reader.GetInt64(0))", source);
    }

    [Fact]
    public void UserInsert_WithoutDirective_StillReturnsRowcount()
    {
        var (result, _) = RunAutoCrud(autoCrud: true,
            ("db/Products/Insert.sql", "insert into products (product_name, category_id) values (@product_name, @category_id)"));

        var source = TryGetSource(result, "Products.Insert.g.cs");
        Assert.NotNull(source);
        Assert.Contains("ExecuteNonQuery()", source);
        Assert.DoesNotContain("output inserted", source);
    }

    [Fact]
    public void IdentityDirective_OnUpdate_ReportsJNT7001()
    {
        var (result, _) = RunAutoCrud(autoCrud: false,
            ("db/Products/Rename.sql", "-- @identity\nupdate products set product_name = @product_name where product_id = @product_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT7001");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("only valid on INSERT", diag.GetMessage());
        Assert.Null(TryGetSource(result, "Products.Rename.g.cs"));
    }

    // ── pure synthesis ─────────────────────────────────────

    [Fact]
    public void Synthesize_SkipsTablesNeedingQuotedIdentifiers()
    {
        var schema = new DatabaseSchema();
        schema.Tables["Order Details"] = new TableSchema
        {
            Name = "Order Details",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["OrderId"] = new ColumnSchema { Name = "OrderId", DbType = "int", IsPrimaryKey = true }
            }
        };

        Assert.Empty(AutoCrud.Synthesize(schema));
    }

    [Fact]
    public void Synthesize_CompositePk_UsesAllKeyColumnsInWhere()
    {
        var schema = new DatabaseSchema();
        schema.Tables["order_items"] = new TableSchema
        {
            Name = "order_items",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["order_id"] = new ColumnSchema { Name = "order_id", DbType = "int", IsPrimaryKey = true },
                ["line_no"] = new ColumnSchema { Name = "line_no", DbType = "int", IsPrimaryKey = true },
                ["qty"] = new ColumnSchema { Name = "qty", DbType = "int" }
            }
        };

        var delete = AutoCrud.Synthesize(schema).Single(q => q.MethodName == "Delete");
        Assert.Contains("order_id = @order_id and line_no = @line_no", delete.Sql);
    }
}
