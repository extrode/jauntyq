using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// End-to-end generator coverage for Feature A (expression projections + @type)
/// and Feature B (data-modifying CTEs, RETURNING, INSERT...SELECT). Uses a small
/// auth-domain schema so the acceptance shapes from the task spec validate and
/// emit. Dialect is postgres so RETURNING/CTE are not gated by JNT7002.
/// </summary>
public class ExpressionAndCteTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""users"": {
      ""name"": ""users"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""username"": { ""name"": ""username"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""first_name"": { ""name"": ""first_name"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""hashed_passphrase"": { ""name"": ""hashed_passphrase"", ""dbType"": ""varchar"", ""isNullable"": true },
        ""disabled_at"": { ""name"": ""disabled_at"", ""dbType"": ""timestamp"", ""isNullable"": true },
        ""created_at"": { ""name"": ""created_at"", ""dbType"": ""timestamp"", ""isNullable"": false }
      }
    },
    ""email_addresses"": {
      ""name"": ""email_addresses"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""value"": { ""name"": ""value"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""created_at"": { ""name"": ""created_at"", ""dbType"": ""timestamp"", ""isNullable"": false }
      }
    }
  }
}";

    private static (GeneratorDriverRunResult result, Compilation compilation) Run(
        string sql, string sqlFilePath, string schemaJson = SchemaJson)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("");
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.Common.DbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
        };
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var additionalRefs = new[]
        {
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
        };

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            references.Concat(additionalRefs),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new JauntyQGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(sqlFilePath, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)
            ))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver.GetRunResult(), outputCompilation);
    }

    private static string GetSource(GeneratorDriverRunResult result, string hintSubstring)
    {
        foreach (var tree in result.GeneratedTrees)
            if (tree.FilePath.Contains(hintSubstring))
                return tree.GetText().ToString();
        throw new System.Exception("No generated tree matching '" + hintSubstring + "'. Available: " +
            string.Join(", ", result.GeneratedTrees.Select(t => t.FilePath)));
    }

    private static void AssertNoErrors(GeneratorDriverRunResult result) =>
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

    // ── Feature A: expression projections + typing ────────

    [Fact]
    public void InferredBooleanExpression_EmitsBoolProperty()
    {
        var sql = "select id, (hashed_passphrase is not null) as has_passphrase from users where id = @id";
        var (result, _) = Run(sql, "db/Users/GetFlag.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.GetFlag.g.cs");
        Assert.Contains("HasPassphrase", source);
        Assert.Contains("bool HasPassphrase", source);
    }

    [Fact]
    public void TypeDirective_OverridesInference()
    {
        var sql =
            "-- @type label varchar\n" +
            "select id, (first_name) as label from users where id = @id";
        var (result, _) = Run(sql, "db/Users/GetLabel.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.GetLabel.g.cs");
        // varchar -> string?, and @type-only expressions are nullable.
        Assert.Contains("string? Label", source);
    }

    [Fact]
    public void ExpressionWithoutInferenceOrType_JNT3005()
    {
        var sql = "select id, (first_name) as label from users where id = @id";
        var (result, _) = Run(sql, "db/Users/GetLabel.sql");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3005");
    }

    [Fact]
    public void TypeDirectiveUnknownAlias_JNT3006()
    {
        var sql =
            "-- @type wrong_alias boolean\n" +
            "select id, (hashed_passphrase is not null) as has_passphrase from users where id = @id";
        var (result, _) = Run(sql, "db/Users/GetFlag.sql");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3006");
    }

    [Fact]
    public void ExpressionMissingAlias_JNT3004()
    {
        var sql = "select id, (hashed_passphrase is not null) from users where id = @id";
        var (result, _) = Run(sql, "db/Users/GetFlag.sql");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3004");
    }

    [Fact]
    public void CountStar_NotStarSelect_NoJNT3002()
    {
        var sql = "select count(*) as n from users where id = @id";
        var (result, _) = Run(sql, "db/Users/CountUsers.sql");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT3002");
        AssertNoErrors(result);
        var source = GetSource(result, "Users.CountUsers.g.cs");
        Assert.Contains("long N", source); // bigint -> long, NOT NULL
    }

    // ── Acceptance shapes (a) and (b) ─────────────────────

    [Fact]
    public void AcceptanceA_FirstWithIsNotNullExpression()
    {
        var sql =
            "-- @first\n" +
            "select id, username, (hashed_passphrase is not null) as has_passphrase " +
            "from users where username = @username and disabled_at is null";
        var (result, _) = Run(sql, "db/Users/FindByName.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.FindByName.g.cs");
        Assert.Contains("HasPassphrase", source);
        Assert.Contains("bool HasPassphrase", source);
    }

    [Fact]
    public void AcceptanceB_FirstWithComparisonExpression()
    {
        var sql =
            "-- @first\n" +
            "select count(*) > 0 as is_in_use from users where username = @username";
        var (result, _) = Run(sql, "db/Users/NameInUse.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.NameInUse.g.cs");
        Assert.Contains("bool IsInUse", source);
    }

    // ── Feature B: RETURNING ──────────────────────────────

    [Fact]
    public void InsertReturning_IsRowReturning()
    {
        var sql = "insert into users (first_name, username, created_at) values (@firstName, @username, @createdAt) returning id";
        var (result, _) = Run(sql, "db/Users/Create.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.Create.g.cs");
        // Row-returning: exposes a reader-materialized Result with Id, not a plain int.
        Assert.Contains("ExecuteReader", source);
        Assert.Contains("Id", source);
    }

    [Fact]
    public void InsertReturningExpression_Typed()
    {
        var sql =
            "-- @first\n" +
            "insert into users (first_name, username, created_at) values (@firstName, @username, @createdAt) " +
            "returning id, (hashed_passphrase is not null) as has_passphrase";
        var (result, _) = Run(sql, "db/Users/Create.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.Create.g.cs");
        Assert.Contains("bool HasPassphrase", source);
    }

    [Fact]
    public void IdentityPlusUserReturning_JNT7001()
    {
        var sql =
            "-- @identity\n" +
            "insert into users (first_name, username, created_at) values (@firstName, @username, @createdAt) returning id";
        var (result, _) = Run(sql, "db/Users/Create.sql");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT7001");
    }

    [Fact]
    public void PlainDelete_NoReturning_StaysRowsAffected()
    {
        var sql = "delete from users where id = @id";
        var (result, _) = Run(sql, "db/Users/Delete.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.Delete.g.cs");
        Assert.Contains("ExecuteNonQuery", source);
    }

    // ── Feature B: acceptance (c) and (d) ─────────────────

    [Fact]
    public void AcceptanceC_CteInsertReturning_ThenInsertSelectReturning()
    {
        var sql =
            "-- @first\n" +
            "with new_user as (" +
            "  insert into users (first_name, username, created_at) values (@firstName, @username, @createdAt) returning id" +
            ") " +
            "insert into email_addresses (user_id, value, created_at) " +
            "select id, @value, @createdAt from new_user returning user_id";
        var (result, _) = Run(sql, "db/Users/CreateWithEmail.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.CreateWithEmail.g.cs");
        Assert.Contains("UserId", source);
        Assert.Contains("ExecuteReader", source); // row-returning via RETURNING
    }

    [Fact]
    public void AcceptanceD_ChainOfDeleteCtes_RowsAffected()
    {
        var sql =
            "with d1 as (delete from email_addresses where user_id = @userId), " +
            "d2 as (delete from email_addresses where user_id = @userId), " +
            "d3 as (delete from email_addresses where user_id = @userId), " +
            "d4 as (delete from email_addresses where user_id = @userId), " +
            "d5 as (delete from email_addresses where user_id = @userId) " +
            "delete from users where id = @userId";
        var (result, _) = Run(sql, "db/Users/PurgeUser.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.PurgeUser.g.cs");
        // No RETURNING anywhere -> rows-affected int.
        Assert.Contains("ExecuteNonQuery", source);
    }

    [Fact]
    public void InsertSelect_BindsLoneParam()
    {
        var sql =
            "insert into email_addresses (user_id, value, created_at) " +
            "select id, @value, @createdAt from users where id = @userId";
        var (result, _) = Run(sql, "db/Emails/Copy.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Emails.Copy.g.cs");
        Assert.Contains("@value", source);
    }

    // ── Dialect gating (JNT7002) ──────────────────────────

    [Fact]
    public void ReturningUnderSqlServer_JNT7002()
    {
        var sqlServerSchema = SchemaJson.Replace("\"dialect\": \"postgres\"", "\"dialect\": \"sqlserver\"");
        var sql = "insert into users (first_name, username, created_at) values (@firstName, @username, @createdAt) returning id";
        var (result, _) = Run(sql, "db/Users/Create.sql", sqlServerSchema);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT7002");
    }

    [Fact]
    public void ReturningUnderMySql_JNT7002()
    {
        // Stock MySQL has no RETURNING clause on any DML statement (MariaDB's
        // partial support, sharing the "mysql" dialect string, is a deliberate
        // over-flag documented on ValidateDialectConstructs).
        var mySqlSchema = SchemaJson.Replace("\"dialect\": \"postgres\"", "\"dialect\": \"mysql\"");
        var sql = "insert into users (first_name, username, created_at) values (@firstName, @username, @createdAt) returning id";
        var (result, _) = Run(sql, "db/Users/Create.sql", mySqlSchema);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT7002");
    }

    [Fact]
    public void DataModifyingCteUnderMySql_JNT7002()
    {
        // MySQL/MariaDB CTE bodies can only be a SELECT; a modifying CTE body
        // (INSERT/UPDATE/DELETE) is a postgres/sqlite-only construct.
        var mySqlSchema = SchemaJson.Replace("\"dialect\": \"postgres\"", "\"dialect\": \"mysql\"");
        var sql =
            "with d1 as (delete from email_addresses where user_id = @userId) " +
            "delete from users where id = @userId";
        var (result, _) = Run(sql, "db/Users/PurgeUser.sql", mySqlSchema);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT7002");
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("mysql")]
    public void ReadOnlyCteUnderSqlServerOrMySql_NotFlaggedAsJNT7002(string dialect)
    {
        // Only a CTE whose own body is INSERT/UPDATE/DELETE (see
        // DataModifyingCteUnderMySql_JNT7002 above) is the unsupported
        // "writable CTE" construct JNT7002 describes. A plain read-only
        // "WITH cte AS (SELECT ...) SELECT ..." is ordinary SQL every
        // dialect here supports -- ValidateDialectConstructs used to gate
        // on "any CTE present at all", which flagged this extremely common,
        // fully-supported form as unsupported under sqlserver/mysql.
        var dialectSchema = SchemaJson.Replace("\"dialect\": \"postgres\"", $"\"dialect\": \"{dialect}\"");
        var sql =
            "with active_users as (select id, username from users where disabled_at is null) " +
            "select id, username from active_users";
        var (result, _) = Run(sql, "db/Users/ListActive.sql", dialectSchema);

        AssertNoErrors(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT7002");
    }

    // ── Unrecognized dialect string (JNT7003) ──────────────

    [Fact]
    public void UnrecognizedDialectString_JNT7003()
    {
        // A schema.json typo (or a hand-authored snapshot for an engine the
        // generator has no case for) must fail the build, not silently fall
        // through every dialect-specific switch's default case.
        var badDialectSchema = SchemaJson.Replace("\"dialect\": \"postgres\"", "\"dialect\": \"mariadb\"");
        var sql = "select id from users where id = @id";
        var (result, _) = Run(sql, "db/Users/GetById.sql", badDialectSchema);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT7003");
        Assert.Contains("mariadb", diag.GetMessage());
        Assert.Contains("mysql", diag.GetMessage());
    }

    [Fact]
    public void RecognizedDialectStrings_NoJNT7003()
    {
        foreach (var dialect in new[] { "sqlserver", "postgres", "sqlite", "mysql" })
        {
            var schema = SchemaJson.Replace("\"dialect\": \"postgres\"", $"\"dialect\": \"{dialect}\"");
            var sql = "select id from users where id = @id";
            var (result, _) = Run(sql, "db/Users/GetById.sql", schema);

            Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT7003");
        }
    }

    // ── Unterminated comment/bracket identifier (JNT1002) ──

    [Fact]
    public void UnterminatedBlockComment_JNT1002()
    {
        var sql = "select id from users /* where id = @id";
        var (result, _) = Run(sql, "db/Users/GetById.sql");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT1002");
    }

    // ── CTE-sourced projection columns must resolve to real types ──
    // Before ResolveThroughCtes they silently typed as object/GetValue.

    [Fact]
    public void CteColumns_ResolveToRealTypes_NotObject()
    {
        var sql =
            "with c as (select id, first_name from users) " +
            "select c.id, c.first_name from c where c.id = @id";
        var (result, _) = Run(sql, "db/Users/GetViaCte.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.GetViaCte.g.cs");
        Assert.Contains("int Id", source);
        Assert.Contains("string FirstName", source);
        Assert.DoesNotContain("object Id", source);
        Assert.DoesNotContain("object FirstName", source);
    }

    [Fact]
    public void CteColumn_OuterLeftJoinOptionalSide_IsNullable()
    {
        // ResolveColumn used to report resolvedTableKey=null whenever
        // resolution went through a CTE, so the outer-join nullability
        // check (forceNullable = resolvedTableKey != null &&
        // outerJoinedKeys.Contains(resolvedTableKey)) could never see a
        // CTE reference sitting on the optional side of a LEFT/RIGHT/FULL
        // JOIN. A schema-declared-NOT-NULL column projected through the CTE
        // then stayed non-nullable even though the join can legitimately
        // produce an all-NULL row for it -- the generated reader's
        // non-null GetString/GetInt32 call would throw at runtime on
        // exactly the row the join exists to allow. The control (a real
        // table on the optional side, no CTE involved) already widens
        // correctly; this CTE case now must match it.
        var sql =
            "with c as (select id, first_name from users) " +
            "select u.id, c.first_name from users u left join c on c.id = u.id";
        var (result, _) = Run(sql, "db/Users/GetWithOptionalCte.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.GetWithOptionalCte.g.cs");
        Assert.Contains("string? FirstName", source);
        Assert.DoesNotContain("string FirstName", source);
    }

    [Fact]
    public void CteDeclaredColumnList_MapsPositionally()
    {
        var sql =
            "with c (uid, uname) as (select id, first_name from users) " +
            "select uid, uname from c where uid = @id";
        var (result, _) = Run(sql, "db/Users/GetDeclared.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.GetDeclared.g.cs");
        Assert.Contains("int Uid", source);
        Assert.Contains("string Uname", source);
    }

    [Fact]
    public void CteExpressionOutput_TypesFromShapeInference()
    {
        var sql =
            "-- @first\n" +
            "with c as (select count(*) as n from users) " +
            "select c.n from c";
        var (result, _) = Run(sql, "db/Users/CountViaCte.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.CountViaCte.g.cs");
        Assert.Contains("long N", source); // count(*) -> bigint NOT NULL
    }

    [Fact]
    public void NestedCtes_ResolveThroughTheChain()
    {
        var sql =
            "with a as (select id from users), " +
            "b as (select id from a) " +
            "select b.id from b where b.id = @id";
        var (result, _) = Run(sql, "db/Users/GetNested.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.GetNested.g.cs");
        Assert.Contains("int Id", source);
        Assert.DoesNotContain("object Id", source);
    }

    // ── Operator characters must not corrupt the projection ──
    // Before the tokenizer emitted them, '||' vanished from the token stream
    // and 'first_name || username as full_name' parsed as TWO plain columns —
    // a silently wrong row shape that only failed at runtime.

    [Fact]
    public void ConcatExpression_IsOneExpressionItem_NotTwoColumns()
    {
        var sql = "select first_name || username as full_name from users where id = @id";
        var (result, _) = Run(sql, "db/Users/GetFullName.sql");

        // Expression with no inferable type: must demand -- @type, never emit
        // a two-property row type.
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3005");
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("Users.GetFullName.g.cs"));
    }

    [Fact]
    public void ConcatExpression_WithTypeDirective_EmitsSingleProperty()
    {
        var sql =
            "-- @type full_name varchar\n" +
            "select first_name || username as full_name from users where id = @id";
        var (result, _) = Run(sql, "db/Users/GetFullName.sql");

        AssertNoErrors(result);
        var source = GetSource(result, "Users.GetFullName.g.cs");
        Assert.Contains("string? FullName", source);
        // The silently-wrong old shape had a FirstName property.
        Assert.DoesNotContain("FirstName", source);
    }

    [Fact]
    public void PostgresCast_IsExpression_NotPhantomColumnLookup()
    {
        // 'id::text' once mis-tokenized as the two identifiers 'id' and 'text',
        // producing a misleading JNT2002 "Column 'text' does not exist".
        var sql = "select id::text as id_text from users where id = @id";
        var (result, _) = Run(sql, "db/Users/GetIdText.sql");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2002");
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3005");
    }

    // ── Unknown character (JNT1004) ──

    [Fact]
    public void UnknownCharacter_JNT1004_NoEmit()
    {
        var sql = "select id from users where id = $1";
        var (result, _) = Run(sql, "db/Users/GetById.sql");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT1004");
        Assert.Contains("$", diag.GetMessage());
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("Users.GetById.g.cs"));
    }

    // ── Oversized input (JNT1003) ──

    [Fact]
    public void OversizedInput_JNT1003_NoEmit()
    {
        var sql = "select id from users where id = @id\n" +
            new string(' ', JauntyQ.SqlParser.SqlTokenizer.MaxInputLength);
        var (result, _) = Run(sql, "db/Users/GetById.sql");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT1003");
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("Users.GetById.g.cs"));
    }
}
