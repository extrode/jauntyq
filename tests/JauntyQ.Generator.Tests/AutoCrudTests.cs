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
        Assert.Contains("Task<Product?> GetByIdAsync(", source);
        Assert.Contains("p0.DbType = DbType.Int32;", source);
        // AUD-R69-01: "__reader", not "reader".
        Assert.Contains("Product.Read(__reader)", source);
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
        Assert.Contains("public static Product Read(DbDataReader reader)", poco);

        // category_id is int NULL -> int?. The null arm must carry the nullable
        // type explicitly; a bare `default` infers int (from GetInt32) and
        // yields 0 instead of null for NULL columns.
        Assert.Contains("default(int?)", poco);
        Assert.DoesNotContain("? default :", poco);

        var getAll = TryGetSource(result, "Products.GetAll.auto.g.cs");
        Assert.NotNull(getAll);
        Assert.Contains("List<Product> GetAll(", getAll);
    }

    [Fact]
    public void FkLoader_Synthesized_FromForeignKeys()
    {
        var (result, _) = RunAutoCrud();

        var source = TryGetSource(result, "Products.GetByCategoryId.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("List<Product> GetByCategoryId(int? category_id)", source);
        Assert.Contains("WHERE products.category_id = @category_id", source);
    }

    [Fact]
    public void Upsert_Synthesized_ForNonIdentityKey_SkippedForIdentityKey()
    {
        var (result, compilation) = RunAutoCrud();

        // customers: string PK, not identity -> upsert exists (sqlserver MERGE)
        var upsert = TryGetSource(result, "Customers.Upsert.auto.g.cs");
        Assert.NotNull(upsert);
        Assert.Contains("MERGE INTO customers WITH (HOLDLOCK) AS target", upsert);
        Assert.Contains("WHEN MATCHED THEN UPDATE SET company_name = src.company_name", upsert);
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
        Assert.Contains("ON CONFLICT (customer_id) DO UPDATE SET company_name = EXCLUDED.company_name", pgSource);

        var (my, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"mysql\""));
        var mySource = TryGetSource(my, "Customers.Upsert.auto.g.cs");
        Assert.NotNull(mySource);
        Assert.Contains("ON DUPLICATE KEY UPDATE company_name = VALUES(company_name)", mySource);
    }

    [Fact]
    public void Upsert_IdentityOnlyKey_UsesSecondaryUniqueIndexInstead()
    {
        var (result, compilation) = RunAutoCrud();

        // mail_queue: identity-only PK ("id"), but ux_mail_queue_idempotency_key
        // is a secondary UNIQUE index -> upsert IS synthesized, keyed on it.
        var upsert = TryGetSource(result, "MailQueue.Upsert.auto.g.cs");
        Assert.NotNull(upsert);
        Assert.Contains("MERGE INTO mail_queue WITH (HOLDLOCK) AS target", upsert);
        Assert.Contains("ON target.idempotency_key = src.idempotency_key", upsert);
        Assert.Contains("WHEN MATCHED THEN UPDATE SET payload = src.payload", upsert);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT (idempotency_key, payload) VALUES (src.idempotency_key, src.payload)", upsert);

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
        Assert.Contains("INSERT INTO mail_queue (idempotency_key, payload)", pgSource);
        Assert.Contains("ON CONFLICT (idempotency_key) DO UPDATE SET payload = EXCLUDED.payload", pgSource);

        // AUD-R4-16: mail_queue has an identity-only PK AND a secondary unique
        // index, so the PK is a constraint competing with the resolved key.
        // ON DUPLICATE KEY UPDATE names no conflict target and would let the
        // engine match on `id` instead of `idempotency_key`; MySQL therefore
        // takes the key-targeted form here, the same key postgres names in its
        // ON CONFLICT above. This assertion read
        // "ON DUPLICATE KEY UPDATE payload = VALUES(payload)" until 2026-07-30.
        var (my, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"mysql\""));
        var mySource = TryGetSource(my, "MailQueue.Upsert.auto.g.cs");
        Assert.NotNull(mySource);
        Assert.Contains("UPDATE mail_queue SET payload = @payload WHERE idempotency_key = @idempotency_key;", mySource);
        Assert.Contains("INSERT INTO mail_queue (idempotency_key, payload)", mySource);
        Assert.Contains("SELECT @idempotency_key, @payload FROM DUAL", mySource);
        Assert.Contains("WHERE NOT EXISTS (SELECT 1 FROM mail_queue WHERE idempotency_key = @idempotency_key)", mySource);
        Assert.DoesNotContain("ON DUPLICATE KEY UPDATE", mySource);
    }

    /// <summary>
    /// AUD-R4-16's control, and the reason the fix is scoped to the competing
    /// case: <c>customers</c> has an ordinary non-identity PK and no other
    /// unique index, so ON DUPLICATE KEY UPDATE cannot match anything the
    /// resolved key does not, and the emitted SQL must be byte-identical to
    /// what shipped before. Every existing MySQL and MariaDB sample depends on
    /// this staying true.
    /// </summary>
    [Fact]
    public void Upsert_MySql_SingleUniqueConstraint_KeepsOnDuplicateKeyUpdate()
    {
        var (my, compilation) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"mysql\""));

        var source = TryGetSource(my, "Customers.Upsert.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("INSERT INTO customers (customer_id, company_name)", source);
        Assert.Contains("ON DUPLICATE KEY UPDATE company_name = VALUES(company_name)", source);
        Assert.DoesNotContain("NOT EXISTS", source);
        Assert.DoesNotContain("FROM DUAL", source);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// AUD-R4-16: the key-targeted form is two statements and therefore not
    /// atomic, so JNT2018 says so at generation time. It must fire for the
    /// competing-constraint table and stay silent for the single-unique one --
    /// a warning that fires everywhere is a warning nobody reads.
    /// </summary>
    [Fact]
    public void Upsert_MySql_CompetingUnique_ReportsJNT2018_OnlyForThatTable()
    {
        var (my, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"mysql\""));

        var warnings = my.Diagnostics.Where(d => d.Id == "JNT2018").ToList();

        // Exactly one: mail_queue. NOT seen_token, which has the same competing
        // shape (identity-only PK plus one unique index) but gets no Upsert at
        // all -- once the identity and key columns are dropped there is nothing
        // left to SET, so AutoCrud's gate skips synthesis. The warning is
        // reported where the Upsert is emitted rather than from a table scan,
        // precisely so it cannot describe SQL that was never generated.
        Assert.Single(warnings);
        Assert.All(warnings, w => Assert.Equal(DiagnosticSeverity.Warning, w.Severity));
        Assert.Contains(warnings, w => w.GetMessage().Contains("'MailQueue.Upsert'") && w.GetMessage().Contains("(idempotency_key)"));
        Assert.DoesNotContain(warnings, w => w.GetMessage().Contains("'Customers.Upsert'"));
        Assert.DoesNotContain(warnings, w => w.GetMessage().Contains("'SeenToken.Upsert'"));
        Assert.Null(TryGetSource(my, "SeenToken.Upsert.auto.g.cs"));
    }

    /// <summary>
    /// The other three dialects already name their conflict key, so none of
    /// them changes shape and none of them warrants JNT2018.
    /// </summary>
    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgres")]
    [InlineData("sqlite")]
    public void Upsert_NonMySqlDialects_AreUnaffectedByKeyTargeting(string dialect)
    {
        var (result, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", $"\"{dialect}\""));

        var source = TryGetSource(result, "MailQueue.Upsert.auto.g.cs");
        Assert.NotNull(source);
        Assert.DoesNotContain("FROM DUAL", source);
        Assert.Empty(result.Diagnostics.Where(d => d.Id == "JNT2018"));
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
        Assert.Contains("INSERT INTO products (product_name, category_id)", source);
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
        Assert.Contains("OUTPUT INSERTED.product_id", source);
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
        Assert.Contains("RETURNING product_id", source);
    }

    [Fact]
    public void SyntheticInsert_Sqlite_UsesReturning()
    {
        // R9 §2.5 mandate (2.5-@identity-jnt7001-per-dialect): CodeEmitter
        // .BuildIdentityInsertSql shares its "returning <col>" branch between
        // "postgres" and "sqlite" (a single switch case, two labels) -- this
        // had never actually been driven end-to-end for sqlite specifically,
        // only assumed identical to the postgres case tested above. Real
        // execution here catches a future refactor that accidentally splits
        // the two labels apart with divergent behavior.
        var (result, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"sqlite\""));

        var source = TryGetSource(result, "Products.Insert.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("RETURNING product_id", source);
        Assert.DoesNotContain("OUTPUT INSERTED", source);
        Assert.DoesNotContain("last_insert_id()", source);
    }

    [Fact]
    public void SyntheticInsert_MySql_UsesLastInsertId_WithCheckedNarrowing()
    {
        var (result, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", "\"mysql\""));

        var source = TryGetSource(result, "Products.Insert.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("SELECT last_insert_id()", source);
        // AUD-R69-01: "__reader", not "reader".
        Assert.Contains("checked((int)__reader.GetInt64(0))", source);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MYSQL")]
    public void SyntheticInsert_MySqlMixedCase_StillUsesCheckedNarrowing_NotBareGetInt32(string dialect)
    {
        // AUD-R10-03 sibling gap: CodeEmitter.Part4.cs's GetIdentityReaderCall
        // compares identity.Dialect == "mysql" with a plain, case-SENSITIVE
        // C# string equality -- unlike BuildIdentityInsertSql/EmitUpsert in
        // the very same fan-out (fixed round 10 via
        // dialect.ToLowerInvariant()), which this file's own
        // DialectCaseSensitivityTests.cs exercises but which never covered
        // GetIdentityReaderCall. schema.Dialect is a bare, unnormalized JSON
        // string and JNT7003/DialectMapper.IsKnownDialect accept any casing,
        // so "MySql"/"MYSQL" are ordinary, accepted spellings. The query text
        // still correctly appends "select last_insert_id()" (that part goes
        // through BuildIdentityInsertSql, which IS fixed), but
        // LAST_INSERT_ID() always returns BIGINT UNSIGNED regardless of the
        // key column's declared type -- reading it back via a bare
        // reader.GetInt32(0) (skipping the checked long->int narrowing cast)
        // is a real InvalidCastException risk against every ADO.NET provider
        // whose typed getters enforce an exact CLR-type match.
        var (result, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", $"\"{dialect}\""));

        var source = TryGetSource(result, "Products.Insert.auto.g.cs");
        Assert.NotNull(source);
        Assert.Contains("SELECT last_insert_id()", source);
        // AUD-R69-01: "__reader", not "reader".
        Assert.Contains("checked((int)__reader.GetInt64(0))", source);
        Assert.DoesNotContain("reader.GetInt32(0)", source);
    }

    [Fact]
    public void UserInsert_WithoutDirective_StillReturnsRowcount()
    {
        var (result, _) = RunAutoCrud(autoCrud: true,
            ("db/Products/Insert.sql", "insert into products (product_name, category_id) values (@product_name, @category_id)"));

        var source = TryGetSource(result, "Products.Insert.g.cs");
        Assert.NotNull(source);
        Assert.Contains("ExecuteNonQuery()", source);
        Assert.DoesNotContain("OUTPUT INSERTED", source);
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

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgres")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void IdentityDirective_NoIdentityColumnOnTable_ReportsJNT7001_EveryDialect(string dialect)
    {
        // R9 §2.5 mandate (2.5-@identity-jnt7001-per-dialect): the final
        // ResolveIdentityInfo==null branch of the JNT7001 gate -- "table has
        // zero (or more than one) identity columns" -- had never been driven
        // by any existing test, on any dialect. "customers" has a plain
        // (non-identity) primary key. This also confirms the gate is
        // genuinely dialect-invariant (same schema-shape check regardless of
        // the dialect string), rather than assuming it from reading the code.
        var (result, _) = RunAutoCrudWithSchema(PkSchemaJson.Replace("\"sqlserver\"", $"\"{dialect}\""),
            autoCrud: false,
            ("db/Customers/Insert.sql", "-- @identity\ninsert into customers (customer_id, company_name) values (@customer_id, @company_name)"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT7001");
        Assert.Contains("requires exactly one identity column on the target table 'customers'", diag.GetMessage());
        Assert.Null(TryGetSource(result, "Customers.Insert.g.cs"));
    }

    [Fact]
    public void IdentityDirective_WithProc_ReportsJNT7001()
    {
        // R9 §2.5 mandate (2.5-directive-combinations-jnt3003), sibling-sweep
        // of the row: @identity's own precondition gate (JauntyQGenerator
        // .Part2.cs) rejects combination with @proc via JNT7001 (not
        // JNT3003 -- a deliberately different diagnostic code for this one
        // pairing), never previously exercised.
        var (result, _) = RunAutoCrud(autoCrud: false,
            ("db/Products/InsertViaProc.sql",
             "-- @identity\n-- @proc\ninsert into products (product_name) values (@product_name)"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT7001");
        Assert.Contains("cannot be combined with -- @proc", diag.GetMessage());
        Assert.Null(TryGetSource(result, "Products.InsertViaProc.g.cs"));
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
    public void Synthesize_Postgres_SkipsTableWithMixedCaseName()
    {
        // AUD-R64-01: PostgreSQL lower-cases every unquoted identifier it
        // parses. A table stored quoted as "Widgets" (mixed case) cannot be
        // safely referenced bare -- an unquoted "FROM Widgets" resolves to
        // "widgets", a different stored name (verified live against
        // postgres:16: either "relation does not exist", or worse, silently
        // matches an unrelated same-named-but-lowercase table). AutoCrud must
        // skip rather than emit a table reference that silently folds to the
        // wrong object.
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Tables["Widgets"] = new TableSchema
        {
            Name = "Widgets",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["Id"] = new ColumnSchema { Name = "Id", DbType = "int", IsPrimaryKey = true },
                ["Name"] = new ColumnSchema { Name = "Name", DbType = "varchar" }
            }
        };

        Assert.Empty(AutoCrud.Synthesize(schema));
    }

    [Fact]
    public void Synthesize_Postgres_SkipsTableWithMixedCaseColumn()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Tables["widgets"] = new TableSchema
        {
            Name = "widgets",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true },
                ["displayName"] = new ColumnSchema { Name = "displayName", DbType = "varchar" }
            }
        };

        Assert.Empty(AutoCrud.Synthesize(schema));
    }

    [Fact]
    public void Synthesize_SqlServer_MixedCaseNames_StillSynthesizes()
    {
        // Contrast case: SQL Server's unquoted-identifier matching is
        // case-INSENSITIVE (no silent rename risk), so the postgres-only
        // case-fold guard must not overreach into other dialects.
        var schema = new DatabaseSchema { Dialect = "sqlserver" };
        schema.Tables["Widgets"] = new TableSchema
        {
            Name = "Widgets",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["Id"] = new ColumnSchema { Name = "Id", DbType = "int", IsPrimaryKey = true },
                ["DisplayName"] = new ColumnSchema { Name = "DisplayName", DbType = "varchar" }
            }
        };

        Assert.NotEmpty(AutoCrud.Synthesize(schema));
    }

    [Theory]
    [InlineData("postgres", "user")]
    [InlineData("postgres", "window")]
    [InlineData("mysql", "key")]
    [InlineData("mysql", "rank")]
    [InlineData("sqlserver", "user")]
    [InlineData("sqlserver", "transaction")]
    [InlineData("sqlserver", "tran")]
    public void Synthesize_SkipsColumnReservedInTargetDialect_ButNotInJauntyQsOwnKeywordList(string dialect, string columnName)
    {
        // AUD-R64-01: these are ordinary short nouns, not JauntyQ keywords
        // (SqlTokenizer.IsReservedKeyword returns false for all of them), but
        // each is genuinely reserved in the stated target engine. Emitted
        // bare, PostgreSQL silently parses "user" as the niladic
        // current_user() function (wrong data every row, zero diagnostic --
        // verified live against postgres:16); MySQL/SQL Server raise a
        // runtime syntax error instead (verified live against mysql:8.0).
        // Either way AutoCrud must skip the table.
        var schema = new DatabaseSchema { Dialect = dialect };
        schema.Tables["accounts"] = new TableSchema
        {
            Name = "accounts",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true },
                [columnName] = new ColumnSchema { Name = columnName, DbType = "varchar" }
            }
        };

        Assert.Empty(AutoCrud.Synthesize(schema));
    }

    [Fact]
    public void Synthesize_SkipsTableWithColumnNamesCollidingAfterPascalCase()
    {
        // AUD-R64-01 (fix 2): "order_number"/"OrderNumber" are both distinct,
        // individually-legal, non-reserved column names, but both fold to
        // the same PascalCased property name -- unlike the dialect-reserved-
        // word/case-fold gate above, this collision is between two column
        // names, not against an external grammar, so it must be checked
        // independently. A synthetic Insert/Update/Delete's POCO overload
        // and BulkInsert both unconditionally reference the table's shared
        // row type by name, so the whole table is skipped, not just the
        // colliding columns.
        var schema = new DatabaseSchema { Dialect = "sqlserver" };
        schema.Tables["widgets"] = new TableSchema
        {
            Name = "widgets",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true },
                ["order_number"] = new ColumnSchema { Name = "order_number", DbType = "int" },
                ["OrderNumber"] = new ColumnSchema { Name = "OrderNumber", DbType = "int" }
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
        Assert.Contains("order_id = @order_id AND line_no = @line_no", delete.Sql);
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

    // ── Column named after a JauntyQ-internal instance field ─────────────

    // AUD-R70-01: EmitEntityCore declares "private readonly DbConnection
    // _conn;"/"private readonly JauntyDb? _db;" on every generated entity
    // class. A bare schema column named "_conn" reaches AutoCrud's
    // synthesized Insert/Update via EmittedParam.CSharpName (no casing
    // fold) with no .sql authorship needed at all -- broader reach than
    // every other member of this defect family (AUD-R67-01 through
    // AUD-R69-01), which all required at least a hand-written .sql
    // parameter or a stored-procedure parameter name. Confirmed live
    // pre-fix: the column's own (int) type silently shadowed the
    // DbConnection field, producing CS1061 on .State/.Open/.CreateCommand/
    // .Close etc. across both instance Insert and Update overloads.
    private const string UnderscoreConnColumnSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""_conn"": { ""name"": ""_conn"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 40 }
      }
    }
  }
}";

    [Fact]
    public void ColumnNamedUnderscoreConn_AutoCrud_CompilesClean_NoSqlAuthorshipNeeded()
    {
        var (_, compilation) = RunAutoCrudWithSchema(UnderscoreConnColumnSchemaJson);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // ── Non-PK identity column ──────────────────────────────────

    // A table can have an identity/auto-increment column that isn't the
    // primary key (e.g. a natural-key PK plus a separate audit sequence).
    // Confirmed live (Testcontainers SQL Server): "update widgets set seq =
    // @seq, ... where pk_code = @pk_code" throws "Cannot update identity
    // column 'seq'." -- the synthetic Update must exclude it from SET the
    // same way Insert already excludes every identity column.
    private const string NonPkIdentitySchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""pk_code"": { ""name"": ""pk_code"", ""dbType"": ""varchar"", ""isNullable"": false, ""isPrimaryKey"": true, ""maxLength"": 20 },
        ""seq"": { ""name"": ""seq"", ""dbType"": ""int"", ""isNullable"": false, ""isIdentity"": true },
        ""name"": { ""name"": ""name"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 100 }
      }
    }
  }
}";

    [Fact]
    public void NonPkIdentityColumn_ExcludedFromSynthesizedUpdateSet()
    {
        var schema = JauntyQ.Schema.SchemaLoader.Load(NonPkIdentitySchemaJson);

        var update = AutoCrud.Synthesize(schema).Single(q => q.MethodName == "Update");
        Assert.DoesNotContain("seq = @seq", update.Sql);
        Assert.Contains("SET name = @name", update.Sql);
        Assert.Contains("WHERE pk_code = @pk_code", update.Sql);
    }

    [Fact]
    public void NonPkIdentityColumn_AutoCrud_CompilesClean_PocoUpdateOverloadOmitsIdentityArg()
    {
        var (result, compilation) = RunAutoCrudWithSchema(NonPkIdentitySchemaJson);

        // A mismatch between AutoCrud's synthesized SQL @parameter list and
        // EmitPocoOverloads' own setCols filter (CodeEmitter.Part7.cs) shows
        // up here as a compile error in the forwarding Update(row) overload.
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        string? poco = TryGetSource(result, "Widgets.Poco.auto.g.cs");
        Assert.NotNull(poco);
        Assert.Contains("public int Update(Widget row) => Update(row.Name, row.PkCode);", poco);
    }

    [Fact]
    public void NonPkIdentityColumn_ExcludedFromSynthesizedUpsert()
    {
        // Same widgets table as the Update fix above (pk_code natural PK,
        // seq non-PK identity, name plain column). UpsertKeyResolver.Resolve
        // returns pk_code directly (not all PK columns are identity, so it
        // never even reaches the secondary-unique-index branch), so
        // usesAlternateKey is false here -- EmitUpsert's old
        // "usesAlternateKey ? exclude identity : allColumns" gate let seq
        // through into both the insert column list and the update SET,
        // which SQL Server rejects at runtime ("Cannot update identity
        // column 'seq'"), same failure class as the Update fix above.
        var schema = JauntyQ.Schema.SchemaLoader.Load(NonPkIdentitySchemaJson);
        var table = schema.Tables["widgets"];

        string source = CodeEmitter.EmitUpsert("Widget", table, "sqlserver");

        Assert.DoesNotContain("@seq", source);
        Assert.DoesNotContain("seq = src.seq", source);
        Assert.Contains("@name", source);
        Assert.Contains("name = src.name", source);
        Assert.Contains("@pk_code", source);
    }

    [Fact]
    public void NonPkIdentityColumn_AutoCrud_UpsertCompilesClean_PocoOverloadOmitsIdentityArg()
    {
        var (result, compilation) = RunAutoCrudWithSchema(NonPkIdentitySchemaJson);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        string? upsert = TryGetSource(result, "Widgets.Upsert.auto.g.cs");
        Assert.NotNull(upsert);
        Assert.DoesNotContain("@seq", upsert);

        string? poco = TryGetSource(result, "Widgets.Poco.auto.g.cs");
        Assert.NotNull(poco);
        var upsertForwarder = System.Text.RegularExpressions.Regex.Match(poco, @"public int Upsert\(Widget row\) => Upsert\([^)]*\);");
        Assert.True(upsertForwarder.Success, "expected a POCO-forwarding Upsert(Widget) overload in:\n" + poco);
        Assert.DoesNotContain("row.Seq", upsertForwarder.Value);
    }

    // ── Computed/RowVersion primary-key column (AUD-R37-01) ─────

    // A sole PK column that is also IsComputed (e.g. a SQL Server PERSISTED
    // computed column that's part of a PRIMARY KEY constraint) is legitimately
    // resolved as the table's usable Upsert key by UpsertKeyResolver.Resolve
    // (AUD-R36-01: it must agree with AutoCrud's own PK detection for the
    // same table). But unlike Identity, CrudColumnRules.UpsertColumns grants
    // Computed/RowVersion no "unless part of key" escape hatch -- no dialect
    // accepts an explicit INSERT value for either. Confirmed live
    // (Testcontainers SQL Server 2022): the old code emitted a MERGE whose
    // "on target.id = src.id" clause referenced a "src" derived table that
    // never selected "id" (CrudColumnRules.UpsertColumns excludes it
    // unconditionally), throwing SqlException "Invalid column name 'id'." at
    // runtime. AutoCrud.Synthesize's gate must skip Upsert synthesis for
    // this table entirely, matching "no usable key at all".
    private const string ComputedPrimaryKeySchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""gauges"": {
      ""name"": ""gauges"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isComputed"": true },
        ""reading"": { ""name"": ""reading"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 50 }
      }
    }
  }
}";

    [Fact]
    public void ComputedPrimaryKeyColumn_NoUpsertSynthesized()
    {
        var schema = JauntyQ.Schema.SchemaLoader.Load(ComputedPrimaryKeySchemaJson);

        var synthesized = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(synthesized, q => q.MethodName == "Upsert");
        // Insert/Update/Delete are unaffected: none of them route a
        // key column through a "src"/derived-table reference the way
        // EmitUpsert's MERGE branch does, so a computed PK column is
        // referenced directly (e.g. "where id = @id") and works fine.
        Assert.Contains(synthesized, q => q.MethodName == "Insert");
        Assert.Contains(synthesized, q => q.MethodName == "Update");
        Assert.Contains(synthesized, q => q.MethodName == "Delete");
    }

    [Fact]
    public void ComputedPrimaryKeyColumn_AutoCrud_CompilesClean_NoUpsertGenerated()
    {
        var (result, compilation) = RunAutoCrudWithSchema(ComputedPrimaryKeySchemaJson);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        Assert.Null(TryGetSource(result, "Gauges.Upsert.auto.g.cs"));
        Assert.NotNull(TryGetSource(result, "Gauges.Insert.auto.g.cs"));
        Assert.NotNull(TryGetSource(result, "Gauges.Update.auto.g.cs"));
        Assert.NotNull(TryGetSource(result, "Gauges.Delete.auto.g.cs"));

        // No Upsert(row) POCO forwarder was ever emitted for this entity.
        string? poco = TryGetSource(result, "Gauges.Poco.auto.g.cs");
        Assert.NotNull(poco);
        Assert.DoesNotContain("Upsert(", poco);
    }

    [Fact]
    public void EmitUpsert_Throws_WhenPrimaryKeyIsComputedColumn()
    {
        // Direct-call backstop: a caller invoking EmitUpsert without going
        // through AutoCrud.Synthesize's gate (as this test itself does) must
        // still get a loud, clear generation-time failure instead of the
        // silently-broken MERGE SQL the pre-fix code produced.
        var schema = JauntyQ.Schema.SchemaLoader.Load(ComputedPrimaryKeySchemaJson);
        var table = schema.Tables["gauges"];

        var ex = Assert.Throws<System.InvalidOperationException>(() => CodeEmitter.EmitUpsert("Gauge", table, "sqlserver"));
        Assert.Contains("Computed or RowVersion column", ex.Message);
    }

    [Fact]
    public void EmitUpsert_Throws_WhenPrimaryKeyIsRowVersionColumn()
    {
        // Same defect shape, the RowVersion sibling (UpsertKeyResolverTests'
        // own Resolve_RowVersionPrimaryKeyColumn_StillResolvesAsKey pairs
        // with this one, matching AUD-R36-01's Computed/RowVersion pairing).
        const string schemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""tokens"": {
      ""name"": ""tokens"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""binary"", ""isNullable"": false, ""isPrimaryKey"": true, ""isRowVersion"": true },
        ""label"": { ""name"": ""label"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 50 }
      }
    }
  }
}";
        var schema = JauntyQ.Schema.SchemaLoader.Load(schemaJson);
        var table = schema.Tables["tokens"];

        var ex = Assert.Throws<System.InvalidOperationException>(() => CodeEmitter.EmitUpsert("Token", table, "sqlserver"));
        Assert.Contains("Computed or RowVersion column", ex.Message);
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

    // Two distinct, legal table names that fold to the same PascalCase entity
    // name via DialectMapper.ToPascalCase -- Dictionary<string, TableSchema>
    // uses the default (ordinal, case-sensitive) comparer, so "widgets" and
    // "Widgets" can genuinely coexist as two separate keys in the same
    // DatabaseSchema.Tables (reachable on any case-sensitive-identifier
    // engine: Postgres with quoted mixed-case names, or MySQL/MariaDB on a
    // case-sensitive-filesystem Linux host with the common
    // lower_case_table_names=0 default).
    private const string CaseCollidingTableNamesSchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""widget_id"": { ""name"": ""widget_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""widget_name"": { ""name"": ""widget_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    },
    ""Widgets"": {
      ""name"": ""Widgets"",
      ""columns"": {
        ""gadget_id"": { ""name"": ""gadget_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""gadget_name"": { ""name"": ""gadget_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": []
}";

    /// <summary>
    /// Regression test for AUD-R46-01: two distinct, legal table names
    /// ("widgets"/"Widgets") that fold to the same PascalCase entity name via
    /// DialectMapper.ToPascalCase previously caused the second table's ENTIRE
    /// auto-CRUD surface (GetAll, GetById, Insert, Update, Delete -- every
    /// column) to silently vanish with zero diagnostic: claimedMethods'
    /// HashSet.Add returning false for the second table's identically-named
    /// synthetic slots was silently treated the same as "a user SQL file
    /// already claims this," when in fact it was a second, wholly distinct
    /// table's data becoming completely invisible in the generated API.
    /// Confirmed live: pre-fix, the second table's own primary key column
    /// name never appeared in any generated source at all. Now reports
    /// JNT2010 naming both colliding table names; the first table's CRUD is
    /// still emitted (unchanged, deterministic "first wins" behavior), but
    /// the collision itself is no longer silent.
    /// </summary>
    [Fact]
    public void CaseCollidingTableNames_ReportJNT2010_FirstTablesCrudStillEmitted()
    {
        var (result, compilation) = RunAutoCrudWithSchema(CaseCollidingTableNamesSchema, autoCrud: true);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT2010" && d.Severity == DiagnosticSeverity.Error);

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        // The first-declared table ("widgets") keeps its full CRUD surface.
        bool sawWidgetIdColumn = result.GeneratedTrees.Any(t => t.ToString().Contains("widget_id"));
        Assert.True(sawWidgetIdColumn, "Expected the 'widgets' table's CRUD to still be generated.");
    }

    // AUD-R64-01 (T8 residual): three tables, each unusable for a different
    // one of the reasons AutoCrud's bare-identifier gate rejects, and one
    // ordinary table as the false-positive control. Before JNT2015 all three
    // simply vanished from the generated API with nothing said anywhere.
    private const string UnquotableIdentifierSchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""Order Details"": {
      ""name"": ""Order Details"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""qty"": { ""name"": ""qty"", ""dbType"": ""int"", ""isNullable"": false }
      }
    },
    ""territories"": {
      ""name"": ""territories"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""Group"": { ""name"": ""Group"", ""dbType"": ""nvarchar"", ""isNullable"": false }
      }
    },
    ""audits"": {
      ""name"": ""audits"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""backup"": { ""name"": ""backup"", ""dbType"": ""nvarchar"", ""isNullable"": false }
      }
    },
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""name"": { ""name"": ""name"", ""dbType"": ""nvarchar"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": []
}";

    /// <summary>
    /// AUD-R64-01 (T8 residual, JNT2015). Round 64 made AutoCrud skip any
    /// table it cannot name unquoted, which was the right call -- v1 has no
    /// quoting support, and emitting SQL the engine rejects is worse than
    /// emitting nothing. But the skip was a bare `continue`: the table was
    /// absent from `db.`, no diagnostic fired, and the only symptom was a
    /// member that a consumer expected to exist and did not.
    ///
    /// That mattered more once the T8 probe corrected the reserved-word lists
    /// in BOTH directions. An over-inclusive list entry is not a free safety
    /// margin -- it is a table deleted from the API -- and it can only be
    /// argued with if it is visible. Building this repo's own samples with
    /// JNT2015 on immediately surfaced two real cases nobody had noticed:
    /// Northwind's "Order Details" and AdventureWorks' SalesTerritory.Group.
    /// </summary>
    [Fact]
    public void UnquotableIdentifier_ReportsJNT2015_PerTableWithTheReason()
    {
        var (result, compilation) = RunAutoCrudWithSchema(UnquotableIdentifierSchema, autoCrud: true);

        var reported = result.Diagnostics.Where(d => d.Id == "JNT2015").ToList();
        Assert.Equal(3, reported.Count);
        Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));

        string spaced = Assert.Single(reported, d => d.GetMessage().Contains("'Order Details'")).GetMessage();
        Assert.Contains("cannot appear in an unquoted SQL identifier", spaced);
        Assert.Contains("db.OrderDetails", spaced);

        // "Group" is in SqlTokenizer's own keyword list, so JauntyQ's parser
        // rejects it whatever the dialect.
        string jauntyKeyword = Assert.Single(reported, d => d.GetMessage().Contains("'territories'")).GetMessage();
        Assert.Contains("'Group'", jauntyKeyword);
        Assert.Contains("JauntyQ's own SQL parser reserves", jauntyKeyword);

        // "backup" is not a JauntyQ keyword at all -- only SQL Server
        // reserves it. This is the arm that exercises DialectReservedWords.
        string dialectKeyword = Assert.Single(reported, d => d.GetMessage().Contains("'audits'")).GetMessage();
        Assert.Contains("'backup'", dialectKeyword);
        Assert.Contains("reserved by sqlserver as a column name", dialectKeyword);

        // False-positive control: the ordinary table is neither reported nor
        // skipped.
        Assert.DoesNotContain(reported, d => d.GetMessage().Contains("'widgets'"));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("Widgets"));

        // The skip stays non-breaking, exactly as round 64 made it.
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void UnquotableIdentifier_PostgresMixedCase_ReportsJNT2015()
    {
        // The fourth arm: PostgreSQL folds an unquoted reference to lower
        // case, so a table created quoted as "Widgets" can never be reached
        // bare. Reported for the same reason as the rest.
        const string schema = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""Widgets"": {
      ""name"": ""Widgets"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""qty"": { ""name"": ""qty"", ""dbType"": ""int"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": []
}";
        var (result, _) = RunAutoCrudWithSchema(schema, autoCrud: true);

        string msg = Assert.Single(result.Diagnostics.Where(d => d.Id == "JNT2015")).GetMessage();
        Assert.Contains("'Widgets'", msg);
        Assert.Contains("folds an unquoted reference to lower case", msg);
    }

    [Fact]
    public void OrdinarySchema_DoesNotReportJNT2015()
    {
        // The §2.6 false-positive half: the nearest valid input must stay
        // quiet, or the diagnostic is noise and consumers will suppress it.
        var (result, _) = RunAutoCrudWithSchema(PkSchemaJson, autoCrud: true);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2015");
    }

    // Two distinct, individually-legal column names that fold to the same
    // PascalCase property name via DialectMapper.ToPascalCase -- a real,
    // plausible shape for a table carrying both a legacy snake_case column
    // and its newer camelCase/PascalCase replacement during a migration.
    private const string CollidingColumnNamesSchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""order_number"": { ""name"": ""order_number"", ""dbType"": ""int"", ""isNullable"": false },
        ""OrderNumber"": { ""name"": ""OrderNumber"", ""dbType"": ""int"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": []
}";

    /// <summary>
    /// AUD-R64-01 (fix 2, a reviewer's finding): two distinct, individually-legal
    /// column names ("order_number"/"OrderNumber") that fold to the same
    /// PascalCased property name would make CodeEmitter.EmitRowPoco emit a
    /// duplicate property (CS0102) plus a duplicate member-initializer entry
    /// in Read() -- the same "sibling switch/guard divergence" shape
    /// JNT3009/JNT2009/JNT2010 already guard for a query's own SELECT
    /// projection, sequence accessors, and entity accessor names
    /// respectively, but nothing previously covered a table's own full
    /// column set feeding the shared canonical row POCO. AutoCrud.Synthesize
    /// now skips the WHOLE table (matching the existing, already-silent
    /// dialect-reserved-word/case-fold skip AUD-R64-01's first fix
    /// established -- v1 has no quoting/renaming support, so an
    /// unsafely-named table/column is skipped rather than emitted broken,
    /// with no diagnostic at this layer), so a colliding table's ENTIRE
    /// auto-CRUD surface is silently absent rather than referencing a row
    /// type that JauntyQGenerator's own JNT2011 guard (Part4.cs, for the
    /// row POCO; ProcCallTests for the stored-proc Result DTO sibling) would
    /// otherwise refuse to emit. The whole compilation stays clean rather
    /// than failing with an opaque CS0102/CS1912 cascade.
    /// </summary>
    [Fact]
    public void CollidingColumnNames_SkipsWholeTable_CompilationStaysClean()
    {
        var (result, compilation) = RunAutoCrudWithSchema(CollidingColumnNamesSchema, autoCrud: true);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2011");

        // AUD-R75-03 (round 75) modified this test. It previously asserted the
        // skip produced NO diagnostic at all, per the doc comment above. That
        // turned out to mean the table vanished from the generated API with
        // nothing to explain it -- and the JNT2011 guard this comment credits
        // could never fire, because Part4.cs's row-POCO loop iterates
        // neededRowTables and Part5.cs:59 is exactly what excludes such a
        // table from that set. The skip itself is unchanged and still
        // non-breaking; it is now merely explained. The JNT2011 assertion
        // above is kept as-is, since that specific guard still must not fire.
        var jnt2014 = Assert.Single(result.Diagnostics.Where(d => d.Id == "JNT2014"));
        Assert.Equal(DiagnosticSeverity.Warning, jnt2014.Severity);
        string msg = jnt2014.GetMessage();
        Assert.Contains("order_number", msg);
        Assert.Contains("OrderNumber", msg);
        Assert.Contains("db.Widgets", msg);

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        // No row POCO -- and no auto-CRUD source referencing it -- was
        // emitted for the colliding table at all.
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("Widgets.Row"));
        bool sawColumnOrderNumber = result.GeneratedTrees.Any(t => t.ToString().Contains("order_number"));
        Assert.False(sawColumnOrderNumber, "Expected the colliding 'widgets' table to be skipped entirely.");
    }

    /// <summary>
    /// AUD-R64-01 (fix 2): a hand-written query aliasing one of two
    /// otherwise-colliding raw columns distinctly (unlike AutoCrud, which
    /// always selects raw column names verbatim) has no collision in its OWN
    /// projection -- but still selects the "widgets" table's full raw column
    /// set, so ResolveCanonicalRowType's original (pre-fix) logic would have
    /// matched it to the table's shared (unsafely-emittable) canonical row
    /// type anyway, leaving this query's OWN generated source referencing a
    /// type JauntyQGenerator's row-POCO loop refuses to emit. Confirms
    /// ResolveCanonicalRowType's own collision check correctly falls back to
    /// this query's distinct, conflict-free per-query projection type
    /// instead, so it compiles clean.
    /// </summary>
    [Fact]
    public void FullRowQuery_WithDistinctAliasAroundCollidingTableColumns_FallsBackToPerQueryType_CompilesClean()
    {
        var (result, compilation) = RunAutoCrudWithSchema(CollidingColumnNamesSchema, autoCrud: false, sqlFiles:
            ("db/Widgets/GetAll.sql", "select id, order_number as order_num, OrderNumber from widgets"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT3009" || d.Id == "JNT2011");

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        // Never claimed the broken shared "WidgetsRow" type -- used its own
        // per-query projection instead.
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("Widgets.Row"));
        bool sawOwnDistinctProperties = result.GeneratedTrees.Any(t =>
            t.ToString().Contains("OrderNum") && t.ToString().Contains("OrderNumber"));
        Assert.True(sawOwnDistinctProperties, "Expected the query's own per-query type with distinct property names.");
    }
}
