using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class GeneratorDisableAuditTests
{
    private const string Kelvin = "\u212A";

    private static string Col(string name, bool pk = false, string type = "int") =>
        $"\"{name}\": {{ \"name\": \"{name}\", \"dbType\": \"{type}\", \"isNullable\": false, \"isPrimaryKey\": {(pk ? "true" : "false")} }}";

    private static string Table(string name, params string[] columns) =>
        $"\"{name}\": {{ \"name\": \"{name}\", \"columns\": {{ {string.Join(", ", columns)} }}, \"indexes\": [] }}";

    private static string Fk(string fromTable, string fromColumn, string toTable, string toColumn) =>
        $"{{ \"fromTable\": \"{fromTable}\", \"fromColumn\": \"{fromColumn}\", \"toTable\": \"{toTable}\", \"toColumn\": \"{toColumn}\" }}";

    private static string Schema(string[] tables, params string[] fks) =>
        $"{{ \"dialect\": \"postgres\", \"tables\": {{ {string.Join(", ", tables)} }}, \"foreignKeys\": [{string.Join(", ", fks)}] }}";

    private static GeneratorDriverRunResult Run(string schemaJson, params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("GeneratorDisableAuditAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText> { new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson) };
        foreach (var (path, text) in files)
            texts.Add(new InMemoryAdditionalText(path, text));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result = driver.GetRunResult();
        Assert.Null(result.Results[0].Exception);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT0001");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return result;
    }

    [Fact]
    public void ParentTableWhoseLowercaseIsNotCaseInsensitiveEqual_StillCountsAsCollection()
    {
        var result = Run(
            Schema(new[] { Table(Kelvin + "eys", Col("id", pk: true)), Table("locks", Col("id", pk: true), Col("key_id")) },
                Fk("locks", "key_id", Kelvin + "eys", "id")),
            ("db/Keys/GetAll.sql", $"select id\nfrom \"{Kelvin}eys\""),
            ("db/Locks/GetByKeyId.sql", "select id\nfrom locks\nwhere locks.key_id = @key_id"));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Contains("Locks.GetByKeyId", diagnostic.GetMessage());
        Assert.Contains("Keys.GetAll", diagnostic.GetMessage());
    }

    [Fact]
    public void CteWhoseLowercaseCollidesWithParentTable_IsNotReadAsTheParent()
    {
        var result = Run(
            Schema(new[] { Table("keys", Col("id", pk: true)), Table("locks", Col("id", pk: true), Col("key_id")) },
                Fk("locks", "key_id", "keys", "id")),
            ("db/Keys/GetAll.sql", "select id\nfrom keys"),
            ("db/Locks/GetByKeyId.sql",
                $"with \"{Kelvin}eys\" as (select id from locks)\n" +
                $"select l.id\nfrom locks l\njoin \"{Kelvin}eys\" k on k.id = l.id\nwhere l.key_id = @key_id"));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Contains("Locks.GetByKeyId", diagnostic.GetMessage());
    }

    [Fact]
    public void WritableCteSetParameter_IsNotAnEqualityFilter()
    {
        var result = Run(
            Schema(new[] { Table("posts", Col("id", pk: true)), Table("comments", Col("id", pk: true), Col("post_id"), Col("body", type: "text")) },
                Fk("comments", "post_id", "posts", "id")),
            ("db/Posts/GetAll.sql", "select id\nfrom posts"),
            ("db/Comments/Move.sql",
                "with m as (update comments set post_id = @post_id where body = @body returning id)\n" +
                "select c.id\nfrom comments c"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void TwoTablesDifferingOnlyInCase_ResolveTheExactNameBeforeTheCaseInsensitiveOne()
    {
        var uniqueScope = "\"indexes\": [ { \"name\": \"ux_locks_key_tenant\", \"columns\": [\"key_id\", \"tenant_id\"], \"isUnique\": true } ]";
        var exactLocks = "\"locks\": { \"name\": \"locks\", \"columns\": { " + string.Join(", ", Col("id"), Col("key_id"), Col("tenant_id")) + " }, " + uniqueScope + " }";
        var result = Run(
            Schema(new[] { Table("Locks", Col("id"), Col("key_id"), Col("tenant_id")), exactLocks, Table("keys", Col("id", pk: true)) },
                Fk("locks", "key_id", "keys", "id")),
            ("db/Keys/GetAll.sql", "select id\nfrom keys"),
            ("db/Locks/GetByKeyAndTenant.sql", "select id\nfrom locks\nwhere locks.key_id = @key_id and locks.tenant_id = @tenant_id"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Theory]
    [InlineData("select id\nfrom items x\nwhere id = @id\nlimit 1")]
    [InlineData("select id\nfrom items x\nwhere x.id = @id\nlimit 1")]
    [InlineData("select id\nfrom items\nwhere id = @id\nlimit 1")]
    public void PrimaryKeyFilterWithLimit_IsNotReportedAsUnorderedPagination(string sql)
    {
        var result = Run(
            Schema(new[] { Table("items", Col("id", pk: true)) }),
            ("db/Items/GetOne.sql", sql));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8010");
    }

    [Fact]
    public void UnorderedLimitOnAliasedTable_IsStillReported()
    {
        var result = Run(
            Schema(new[] { Table("items", Col("id", pk: true), Col("name", type: "text")) }),
            ("db/Items/GetPage.sql", "select id\nfrom items x\nwhere name = @name\nlimit 10"));

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8010");
    }
}
