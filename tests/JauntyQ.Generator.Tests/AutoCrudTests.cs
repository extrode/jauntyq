using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Analysis;
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
    },
    ""mail_queue"": {
      ""name"": ""mail_queue"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""idempotency_key"": { ""name"": ""idempotency_key"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""payload"": { ""name"": ""payload"", ""dbType"": ""varchar"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""ux_mail_queue_idempotency_key"", ""columns"": [""idempotency_key""], ""isUnique"": true }
      ]
    },
    ""seen_token"": {
      ""name"": ""seen_token"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""token"": { ""name"": ""token"", ""dbType"": ""varchar"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""ux_seen_token_token"", ""columns"": [""token""], ""isUnique"": true }
      ]
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
            // sqlserver-dialect schemas can synthesize BulkInsert via
            // Microsoft.Data.SqlClient.SqlBulkCopy (whose ColumnMappings
            // collection derives from the non-generic CollectionBase); a
            // mysql-dialect schema would similarly need MySqlConnector.MySqlBulkCopy.
            MetadataReference.CreateFromFile(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnector.MySqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.NonGeneric.dll")),
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
        Assert.Contains("public int ProductId { get; set; }", poco); // value types: no `required`
        Assert.Contains("public required string ProductName { get; set; }", poco);
        Assert.Contains("public static Product Read(System.Data.Common.DbDataReader reader)", poco);

        // category_id is int NULL -> int?. The null arm must carry the nullable
        // type explicitly; a bare `default` infers int (from GetInt32) and
        // yields 0 instead of null for NULL columns.
        Assert.Contains("default(int?)", poco);
        Assert.DoesNotContain("? default :", poco);

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
    public void Upsert_IdentityOnlyKey_UsesSecondaryUniqueIndexInstead()
    {
        var (result, compilation) = RunAutoCrud();

        // mail_queue: identity-only PK ("id"), but ux_mail_queue_idempotency_key
        // is a secondary UNIQUE index -> upsert IS synthesized, keyed on it.
        var upsert = TryGetSource(result, "MailQueue.Upsert.auto.g.cs");
        Assert.NotNull(upsert);
        Assert.Contains("merge into mail_queue with (holdlock) as target", upsert);
        Assert.Contains("on target.idempotency_key = src.idempotency_key", upsert);
        Assert.Contains("when matched then update set payload = src.payload", upsert);
        Assert.Contains("when not matched then insert (idempotency_key, payload) values (src.idempotency_key, src.payload)", upsert);

        // the database-assigned identity column is never a caller-supplied arg
        Assert.Contains("public int Upsert(string idempotency_key, string payload)", upsert);
        Assert.DoesNotContain("int id", upsert);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Upsert_IdentityOnlyKey_PostgresAndMySql_KeyOnSecondaryUniqueIndex()
    {
        var (pg, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"postgres\""));
        var pgSource = TryGetSource(pg, "MailQueue.Upsert.auto.g.cs");
        Assert.NotNull(pgSource);
        Assert.Contains("insert into mail_queue (idempotency_key, payload)", pgSource);
        Assert.Contains("on conflict (idempotency_key) do update set payload = excluded.payload", pgSource);

        var (my, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"mysql\""));
        var mySource = TryGetSource(my, "MailQueue.Upsert.auto.g.cs");
        Assert.NotNull(mySource);
        Assert.Contains("insert into mail_queue (idempotency_key, payload)", mySource);
        Assert.Contains("on duplicate key update payload = values(payload)", mySource);
    }

    [Fact]
    public void Upsert_IdentityOnlyKey_PocoOverload_OmitsIdentityColumn()
    {
        var (result, _) = RunAutoCrud();

        var poco = TryGetSource(result, "MailQueue.Poco.auto.g.cs");
        Assert.NotNull(poco);
        Assert.Contains("public int Upsert(MailQueueRow row) => Upsert(row.IdempotencyKey, row.Payload);", poco);
    }

    [Fact]
    public void Upsert_IdentityOnlyKey_WithNoColumnsBesidesTheKey_SkipsUpsertEntirely()
    {
        // seen_token: identity-only PK ("id") + a secondary UNIQUE index
        // ("token") that is the ONLY other column. Once EmitUpsert drops the
        // identity column (it's database-assigned, never caller-supplied) and
        // the key column itself (it's matched on, not updated), there is
        // nothing left to SET -- AutoCrud's own gate must recognize this and
        // skip synthesizing Upsert, instead of emitting a call to EmitUpsert
        // that produces a dangling "update set" / "do update set" with no
        // columns after it.
        var (result, compilation) = RunAutoCrud();

        Assert.Null(TryGetSource(result, "SeenToken.Upsert.auto.g.cs"));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
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

    [Fact]
    public void PocoOverloads_ForwardToScalars_WithIdentityWriteback()
    {
        var (result, compilation) = RunAutoCrud();

        var source = TryGetSource(result, "Products.Poco.auto.g.cs");
        Assert.NotNull(source);
        // identity write-back on Insert
        Assert.Contains("public int Insert(Product row)", source);
        Assert.Contains("row.ProductId = id;", source);
        // Update/Delete forward in scalar order
        Assert.Contains("public int Update(Product row) => Update(row.ProductName, row.CategoryId, row.ProductId);", source);
        Assert.Contains("public int Delete(Product row) => Delete(row.ProductId);", source);
        // Customers (non-identity key): Upsert overload
        var cust = TryGetSource(result, "Customers.Poco.auto.g.cs");
        Assert.NotNull(cust);
        Assert.Contains("public int Upsert(Customer row) => Upsert(row.CustomerId, row.CompanyName);", cust);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void PocoOverloads_Suppressed_WhenUserOverridesScalar()
    {
        var (result, _) = RunAutoCrud(autoCrud: true,
            ("db/Products/Update.sql", "update products set product_name = @product_name where product_id = @product_id"));

        var source = TryGetSource(result, "Products.Poco.auto.g.cs");
        Assert.NotNull(source);
        Assert.DoesNotContain("Update(Product row)", source); // user owns Update's signature
        Assert.Contains("public int Delete(Product row)", source);
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
    public void Synthesize_SkipsTablesWithSqlReservedKeywordColumn()
    {
        // "Group" is lexically a bare identifier (letters only, no spaces) but
        // a SQL reserved word. Emitted unquoted into synthetic SQL, it
        // tokenizes as a Keyword rather than an Identifier, which silently
        // desyncs ParseInsert's positional column-list binding for every
        // column after it — the Insert overload used to come out as
        // Insert(string Name, Guid Group, DateTime rowguid, object
        // ModifiedDate), each parameter one column off from its real type,
        // instead of failing loudly. AutoCrud must skip the table rather than
        // emit it broken (real-world case: AdventureWorks SalesTerritory).
        var schema = new DatabaseSchema();
        schema.Tables["SalesTerritory"] = new TableSchema
        {
            Name = "SalesTerritory",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["TerritoryID"] = new ColumnSchema { Name = "TerritoryID", DbType = "int", IsPrimaryKey = true, IsIdentity = true },
                ["Name"] = new ColumnSchema { Name = "Name", DbType = "varchar" },
                ["Group"] = new ColumnSchema { Name = "Group", DbType = "varchar" },
                ["rowguid"] = new ColumnSchema { Name = "rowguid", DbType = "uniqueidentifier" },
                ["ModifiedDate"] = new ColumnSchema { Name = "ModifiedDate", DbType = "datetime" }
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

    // ── Columns named after C# keywords ──────────────────────────────────

    // 'ref' and 'operator' are lexically valid identifiers (so JNT2004 lets
    // them through) but C# keywords: emitted bare as parameter names they
    // produce CS1988/CS1001 in the generated code. Reported by a downstream
    // consumer (segments.ref / import_runs.operator).
    private const string KeywordColumnsSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""segments"": {
      ""name"": ""segments"",
      ""columns"": {
        ""segment_id"": { ""name"": ""segment_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""ref"": { ""name"": ""ref"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 40 },
        ""operator"": { ""name"": ""operator"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 40 }
      }
    }
  }
}";

    [Fact]
    public void KeywordColumns_AutoCrud_CompilesClean()
    {
        var (result, compilation) = RunAutoCrudWithSchema(KeywordColumnsSchemaJson);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        // Parameters are '@'-escaped in code positions...
        string? insert = TryGetSource(result, "Segments.Insert.auto.g.cs");
        Assert.NotNull(insert);
        Assert.Contains("string @ref", insert);
        // ...but the SQL-side DbParameter name stays the raw column name.
        Assert.Contains("ParameterName = \"@ref\"", insert);
        Assert.DoesNotContain("ParameterName = \"@@ref\"", insert);
    }

    [Fact]
    public void KeywordColumns_HandWrittenQueryParam_CompilesClean()
    {
        var (_, compilation) = RunAutoCrudWithSchema(KeywordColumnsSchemaJson, autoCrud: false,
            ("db/Segments/GetByRef.sql",
                "select segment_id, operator\nfrom segments\nwhere segments.ref = @ref"));

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void TypoDirective_ReportsJNT3008_AndStillEmits()
    {
        var (result, _) = RunAutoCrud(autoCrud: false, sqlFiles:
            ("db/Products/GetAll.sql", "-- @frist\nselect product_id, product_name from products"));

        var warning = Assert.Single(result.Diagnostics, d => d.Id == "JNT3008");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("did you mean '-- @first'", warning.GetMessage());
        // Non-fatal: the file still generates (as a plain multi-row SELECT).
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("Products.GetAll.g.cs"));
    }
}
