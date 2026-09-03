using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// End-to-end generator coverage for the 2026-08-02 fix batch:
/// JNT1008 (a second top-level statement is an Error, not a silent merge),
/// window-COUNT inference reaching the emitted property, and JNT3006's
/// message when the named alias is a plain column re-projected from a CTE.
/// </summary>
public class MultiStatementAndWindowGeneratorTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""users"": {
      ""name"": ""users"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""username"": { ""name"": ""username"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""is_public"": { ""name"": ""is_public"", ""dbType"": ""boolean"", ""isNullable"": false }
      }
    }
  }
}";

    private const string SqlServerSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""users"": {
      ""name"": ""users"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""username"": { ""name"": ""username"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""is_public"": { ""name"": ""is_public"", ""dbType"": ""bit"", ""isNullable"": false }
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

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return (driver.GetRunResult(), compilation);
    }

    private static string GetSource(GeneratorDriverRunResult result, string hintSubstring)
    {
        foreach (var tree in result.GeneratedTrees)
            if (tree.FilePath.Contains(hintSubstring))
                return tree.GetText().ToString();
        throw new System.Exception("No generated tree matching '" + hintSubstring + "'. Available: " +
            string.Join(", ", result.GeneratedTrees.Select(t => t.FilePath)));
    }

    // ── JNT1008 ──

    [Fact]
    public void TwoStatementsInOneFile_JNT1008()
    {
        var sql =
            "select count(*) as total from users;\n" +
            "select id, username from users";
        var (result, _) = Run(sql, "db/Users/ListPage.sql");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT1008");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void TwoStatementsInOneFile_MessageNamesTheFix()
    {
        var sql = "select id from users; select username from users";
        var (result, _) = Run(sql, "db/Users/ListPage.sql");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT1008");
        Assert.Contains("its own .sql file", diag.GetMessage());
    }

    [Fact]
    public void SingleStatementWithTerminator_NoJNT1008()
    {
        var sql = "select id, username from users where id = @id;";
        var (result, _) = Run(sql, "db/Users/GetById.sql");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT1008");
    }

    // ── window COUNT reaching the emitted property ──

    [Fact]
    public void WindowCount_NoDirective_EmitsNonNullableLong()
    {
        // The whole point of the inference: no -- @type needed, and the
        // property is long, not long? — COUNT(*) OVER() cannot be NULL.
        var sql =
            "select id, username, count(*) over() as total from users where is_public = @isPublic";
        var (result, _) = Run(sql, "db/Users/ListPage.sql");

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var source = GetSource(result, "Users.ListPage.g.cs");
        Assert.Contains("long Total", source);
        Assert.DoesNotContain("long? Total", source);
    }

    [Fact]
    public void WindowCount_WithRedundantTypeDirective_StaysNonNullable()
    {
        // A directive still wins on the db type, but nullability comes from
        // the parser's shape inference either way, so an author who writes
        // both does not get long? back.
        var sql =
            "-- @type total bigint\n" +
            "select id, count(*) over() as total from users";
        var (result, _) = Run(sql, "db/Users/ListPage.sql");

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("long Total", GetSource(result, "Users.ListPage.g.cs"));
    }

    [Fact]
    public void WindowCount_OnSqlServer_EmitsInt()
    {
        // COUNT(*) OVER() returns a 32-bit int on SQL Server (COUNT_BIG is the
        // 64-bit form), so the existing bigint->int downgrade must apply to the
        // window form too or the reader throws InvalidCastException.
        var sql = "select id, count(*) over() as total from users";
        var (result, _) = Run(sql, "db/Users/ListPage.sql", SqlServerSchemaJson);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var source = GetSource(result, "Users.ListPage.g.cs");
        Assert.Contains("int Total", source);
        Assert.DoesNotContain("long Total", source);
    }

    // ── JNT3006 message ──

    [Fact]
    public void TypeDirectiveOnCteProjectedColumn_JNT3006_SaysPlainColumn()
    {
        // The reported case: the expression lives in the CTE, the outer SELECT
        // re-projects it as a plain column, and -- @type cannot reach it. The
        // old message said "check the alias spelling", which is exactly wrong.
        var sql =
            "-- @type total bigint\n" +
            "with counted as (select count(*) as total from users)\n" +
            "select total from counted";
        var (result, _) = Run(sql, "db/Users/CteTotal.sql");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3006");
        Assert.Contains("plain column", diag.GetMessage());
        Assert.DoesNotContain("spelling", diag.GetMessage());
    }

    [Fact]
    public void TypeDirectiveWithMisspelledAlias_JNT3006_StillSaysSpelling()
    {
        // The other half stays as it was: an alias matching nothing at all is
        // still most likely a typo.
        var sql =
            "-- @type totl bigint\n" +
            "select id, count(*) as total from users";
        var (result, _) = Run(sql, "db/Users/Totals.sql");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3006");
        Assert.Contains("spelling", diag.GetMessage());
        Assert.DoesNotContain("plain column", diag.GetMessage());
    }
}
