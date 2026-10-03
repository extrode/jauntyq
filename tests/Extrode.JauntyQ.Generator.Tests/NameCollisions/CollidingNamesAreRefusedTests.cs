using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Extrode.JauntyQ.Generator.Tests.NameCollisions.NameCollisionHarness;
using static Extrode.JauntyQ.Generator.Tests.Scoping.ScopeHarness;

namespace Extrode.JauntyQ.Generator.Tests.NameCollisions;

public class CollidingNamesAreRefusedTests
{
    private const string MemberOfJauntyDb = "a member JauntyDb already declares";
    private const string MethodOfItsOwnName = "a C# class cannot have a member named like itself";

    [Theory]
    [InlineData("connection", "sqlite")]
    [InlineData("_connection", "postgres")]
    [InlineData("transaction", "mysql")]
    [InlineData("current_transaction", "sqlite")]
    [InlineData("begin_transaction", "postgres")]
    [InlineData("conn", "sqlite")]
    [InlineData("tx", "mysql")]
    [InlineData("_tx", "sqlite")]
    [InlineData("__conn", "sqlserver")]
    public void ATableNamedLikeAJauntyDbMember_IsReported(string table, string dialect)
    {
        Assert.Contains(Errors(Generate("table", table, dialect)), m => m.StartsWith("JNT2006") && m.Contains(MemberOfJauntyDb));
    }

    [Theory]
    [InlineData("get_all", "sqlite")]
    [InlineData("get_by_id", "postgres")]
    [InlineData("upsert", "mysql")]
    [InlineData("bulk_insert", "sqlite")]
    [InlineData("insert_async", "postgres")]
    public void ATableNamedLikeOneOfItsOwnMethods_IsReported(string table, string dialect)
    {
        Assert.Contains(Errors(Generate("table", table, dialect)), m => m.StartsWith("JNT2006") && m.Contains(MethodOfItsOwnName));
    }

    [Theory]
    [InlineData("transactions")]
    [InlineData("connections")]
    [InlineData("sessions")]
    public void ARealisticPluralTable_IsNotReported(string table)
    {
        var result = Generate("table", table, "sqlite");

        Assert.Empty(Errors(result));
        Assert.Empty(CompileErrors(result));
    }

    [Fact]
    public void ATableNamedSequences_IsReportedOnlyWhenTheSchemaHasSequences()
    {
        string sequences = @"""sequences"": { ""order_seq"": { ""name"": ""order_seq"", ""startValue"": 1, ""increment"": 1 } },";
        var withSequences = Run(Array.Empty<(string, string)>(),
            NameCollisionHarness.Schema("postgres", Table("sequences", "id", ("note", "varchar", true)), sequences), null, true);
        var without = Generate("table", "sequences", "postgres");

        Assert.Contains(Errors(withSequences), m => m.Contains(MemberOfJauntyDb) && m.Contains("db.Sequences"));
        Assert.DoesNotContain(Errors(without), m => m.Contains(MemberOfJauntyDb));
        Assert.Empty(CompileErrors(without));
    }

    [Fact]
    public void ATableNamedFunctions_IsReportedOnlyWhenTheSchemaHasFunctions()
    {
        string functions = @"""functions"": { ""calc_tax(decimal)"": { ""name"": ""calc_tax"", ""schema"": ""public"", ""params"": [ { ""name"": ""amount"", ""dbType"": ""numeric"", ""isNullable"": false } ], ""returnType"": { ""dbType"": ""numeric"", ""isNullable"": true } } },";
        var withFunctions = Run(Array.Empty<(string, string)>(),
            NameCollisionHarness.Schema("postgres", Table("functions", "id", ("note", "varchar", true)), functions), null, true);
        var without = Generate("table", "functions", "postgres");

        Assert.Contains(Errors(withFunctions), m => m.Contains(MemberOfJauntyDb) && m.Contains("db.Functions"));
        Assert.DoesNotContain(Errors(without), m => m.Contains(MemberOfJauntyDb));
        Assert.Empty(CompileErrors(without));
    }

    [Fact]
    public void ASqlFileNamedLikeItsFolder_IsReported()
    {
        var result = HandWritten(("db/Orders/Orders.sql", "SELECT id, note FROM orders"));

        Assert.Contains(Errors(result), m => m.StartsWith("JNT2006") && m.Contains("method 'Orders'"));
    }

    [Theory]
    [InlineData("column", "__cmd")]
    [InlineData("pk", "__reader")]
    [InlineData("scope", "__ScopeRows")]
    public void ADoubleUnderscoreColumn_GetsNoAutoCrud(string site, string name)
    {
        var result = Generate(site, name, "sqlite");

        Assert.Contains(Warnings(result), m => m.StartsWith("JNT2015") && m.Contains($"'{name}' starts with '__'"));
        Assert.Empty(CompileErrors(result));
    }

    [Fact]
    public void ASingleUnderscoreColumn_StillGetsAutoCrud()
    {
        var result = Generate("column", "_cmd", "sqlite");

        Assert.DoesNotContain(Warnings(result), m => m.StartsWith("JNT2015"));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("Orders.Insert.auto.g.cs"));
    }

    [Fact]
    public void AnAliasNamedLikeItsQuery_IsReported()
    {
        var result = Generate("alias", "find", "sqlite");

        Assert.Contains(Errors(result), m => m.StartsWith("JNT3009") && m.Contains("the name of its own generated type Result.Find"));
        Assert.Empty(CompileErrors(result));
    }

    [Fact]
    public void AFullRowQueryNamedLikeAColumn_IsNotReported()
    {
        var result = HandWritten(("db/Orders/Note.sql", "SELECT id, note FROM orders"));

        Assert.Empty(Errors(result));
        Assert.Empty(CompileErrors(result));
    }

    [Fact]
    public void AReturningAliasNamedLikeItsQuery_IsReported()
    {
        var result = HandWritten(("db/Orders/Add.sql", "INSERT INTO orders (note) VALUES (@note) RETURNING id AS add"));

        Assert.Contains(Errors(result), m => m.StartsWith("JNT3009") && m.Contains("Result.Add"));
    }

    [Fact]
    public void AProcedureResultColumnNamedLikeItsMethod_IsReported()
    {
        string proc = @"""procedures"": { ""find_orders"": { ""name"": ""find_orders"", ""params"": [], ""results"": [ { ""name"": ""call"", ""dbType"": ""int"", ""isNullable"": false } ] } },";
        var result = Run(Array.Empty<(string, string)>(), NameCollisionHarness.Schema("sqlserver", Table("orders", "id", ("note", "varchar", true)), proc), null, false,
            ("db/Orders/Call.sql", "-- @call find_orders\n"));

        Assert.Contains(Errors(result), m => m.StartsWith("JNT2011") && m.Contains("Result.Call"));
    }

    [Fact]
    public void AResultQueryNextToAQueryWithItsOwnResultType_IsReported()
    {
        var result = HandWritten(
            ("db/Orders/Result.sql", "SELECT id, note FROM orders"),
            ("db/Orders/Ids.sql", "SELECT id FROM orders"));

        Assert.Contains(Errors(result), m => m.StartsWith("JNT2006") && m.Contains("method 'Result' and the nested type Result"));
    }

    [Fact]
    public void AResultQueryNextToAReturningQuery_IsReported()
    {
        var result = HandWritten(
            ("db/Orders/Result.sql", "SELECT id, note FROM orders"),
            ("db/Orders/Add.sql", "INSERT INTO orders (note) VALUES (@note) RETURNING id"));

        Assert.Contains(Errors(result), m => m.StartsWith("JNT2006") && m.Contains("method 'Result' and the nested type Result"));
    }

    [Fact]
    public void AResultQueryNextToAProcedureCallWithResults_IsReported()
    {
        string proc = @"""procedures"": { ""find_orders"": { ""name"": ""find_orders"", ""params"": [], ""results"": [ { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false } ] } },";
        var result = Run(Array.Empty<(string, string)>(), NameCollisionHarness.Schema("sqlserver", Table("orders", "id", ("note", "varchar", true)), proc), null, false,
            ("db/Orders/Result.sql", "SELECT id, note FROM orders"),
            ("db/Orders/Call.sql", "-- @call find_orders\n"));

        Assert.Contains(Errors(result), m => m.StartsWith("JNT2006") && m.Contains("method 'Result' and the nested type Result"));
    }

    [Fact]
    public void AResultQueryWithNoResultTypeBesideIt_IsNotReported()
    {
        var result = HandWritten(("db/Orders/Result.sql", "SELECT id, note FROM orders"));

        Assert.Empty(Errors(result));
        Assert.Empty(CompileErrors(result));
    }

    [Fact]
    public void AResultEntityWithItsOwnResultType_IsReported()
    {
        var result = Run(Array.Empty<(string, string)>(), NameCollisionHarness.Schema("sqlite", Table("result", "id", ("note", "varchar", true))), null, false,
            ("db/Result/Ids.sql", "SELECT id FROM result"));

        Assert.Contains(Errors(result), m => m.StartsWith("JNT2006") && m.Contains("Entity 'Result' would contain a nested type"));
    }

    [Theory]
    [InlineData("db/Orders/Proc.sql", "-- @proc\nSELECT id, note FROM orders WHERE id = @id")]
    [InlineData("db/Orders/Proc.sql", "SELECT id, note FROM orders WHERE id = @id", "db/Orders/Find.sql", "-- @proc\nSELECT id FROM orders")]
    public void AProcQueryBesideAProcScaffold_IsReported(string path, string sql, string? otherPath = null, string? otherSql = null)
    {
        var files = otherPath == null ? new[] { (path, sql) } : new[] { (path, sql), (otherPath, otherSql!) };

        Assert.Contains(Errors(SqlServer(files)), m => m.StartsWith("JNT2006") && m.Contains("method 'Proc' and the nested type Proc"));
    }

    [Fact]
    public void AProcFolderWithAProcScaffold_IsReported()
    {
        var result = Run(Array.Empty<(string, string)>(), NameCollisionHarness.Schema("sqlserver", Table("proc", "id", ("note", "varchar", true))), null, false,
            ("db/Proc/Find.sql", "-- @proc\nSELECT id, note FROM proc WHERE id = @id"));

        Assert.Contains(Errors(result), m => m.StartsWith("JNT2006") && m.Contains("Entity 'Proc' would contain a nested type"));
    }

    [Fact]
    public void AProcQueryWithNoProcScaffoldBesideIt_IsNotReported()
    {
        var result = SqlServer(("db/Orders/Proc.sql", "SELECT id, note FROM orders WHERE id = @id"));

        Assert.Empty(Errors(result));
        Assert.Empty(CompileErrors(result));
    }

    [Fact]
    public void AFolderNamedLikeAFrameworkType_IsReported()
    {
        var result = HandWritten(("db/Task/Ids.sql", "SELECT id FROM orders"));

        Assert.Contains(Errors(result), m => m.StartsWith("JNT2006") && m.Contains("db/Task/ folder"));
    }

    [Fact]
    public void AFolderWithAnOrdinaryName_IsNotReported()
    {
        var result = HandWritten(("db/Reports/Ids.sql", "SELECT id FROM orders"));

        Assert.Empty(Errors(result));
        Assert.Empty(CompileErrors(result));
    }

    [Fact]
    public void AFolderNamedLikeAReservedGeneratedType_IsReportedOnce()
    {
        var result = HandWritten(("db/ConnectionState/Ids.sql", "SELECT id FROM orders"));

        Assert.Single(Errors(result), m => m.StartsWith("JNT2006"));
    }

    [Fact]
    public void AFolderNamedLikeAFrameworkTypeThatATableAlsoGenerates_IsLeftToTypeQualification()
    {
        var result = Run(Array.Empty<(string, string)>(), NameCollisionHarness.Schema("sqlite", Table("task", "id", ("note", "varchar", true))), null, false,
            ("db/Task/Ids.sql", "SELECT id FROM task"));

        Assert.DoesNotContain(Errors(result), m => m.Contains("db/Task/ folder"));
        Assert.Empty(CompileErrors(result));
    }

    [Fact]
    public void TypeRefNames_ListsEveryNameTheEmitterQualifiesOnCollision()
    {
        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir)!;
        var names = Directory.GetFiles(Path.Combine(dir, "src", "Extrode.JauntyQ.Generator"), "CodeEmitter*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"TypeRef\(schema, (?:paramInfos, )?""(\w+)""").Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.Contains(n, CodeEmitter.TypeRefNames));
    }

    private static GeneratorDriverRunResult HandWritten(params (string path, string sql)[] files) =>
        Run(Array.Empty<(string, string)>(), NameCollisionHarness.Schema("sqlite", Table("orders", "id", ("note", "varchar", true))), null, false, files);

    private static GeneratorDriverRunResult SqlServer(params (string path, string sql)[] files) =>
        Run(Array.Empty<(string, string)>(), NameCollisionHarness.Schema("sqlserver", Table("orders", "id", ("note", "varchar", true))), null, false, files);

    private static List<string> Errors(GeneratorDriverRunResult result) =>
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id}: {d.GetMessage()}").ToList();

    private static List<string> Warnings(GeneratorDriverRunResult result) =>
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).Select(d => $"{d.Id}: {d.GetMessage()}").ToList();
}
