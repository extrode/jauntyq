using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

[Trait("Category", "AuditRegression")]
public class ParameterScopeTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""orders"": { ""name"": ""orders"", ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""bigint"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""customer_id"": { ""name"": ""customer_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""region_id"": { ""name"": ""region_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""note"": { ""name"": ""note"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 10 },
        ""email"": { ""name"": ""email"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 100 },
        ""status"": { ""name"": ""status"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 20 } },
      ""indexes"": [ { ""name"": ""ix_orders_region_note"", ""columns"": [""region_id"", ""note""], ""isUnique"": false },
                     { ""name"": ""ix_orders_customer"", ""columns"": [""customer_id""], ""isUnique"": false } ] },
    ""customers"": { ""name"": ""customers"", ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""region_id"": { ""name"": ""region_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""note"": { ""name"": ""note"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 50 },
        ""email"": { ""name"": ""email"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 100 } },
      ""indexes"": [ { ""name"": ""ix_customers_region"", ""columns"": [""region_id""], ""isUnique"": false },
                     { ""name"": ""ix_customers_email"", ""columns"": [""email""], ""isUnique"": false } ] },
    ""regions"": { ""name"": ""regions"", ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""smallint"", ""isNullable"": false, ""isPrimaryKey"": true } },
      ""indexes"": [] }
  }
}";

    private static GeneratorDriverRunResult Run(string sql, string dialect = "postgres")
    {
        var compilation = CSharpCompilation.Create("ParameterScopeAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var texts = new List<AdditionalText>
        {
            new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson.Replace("\"postgres\"", "\"" + dialect + "\"")),
            new InMemoryAdditionalText("db/Orders/Q.sql", sql),
        };
        return CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false))
            .RunGenerators(compilation).GetRunResult();
    }

    private static string Source(GeneratorDriverRunResult result) =>
        result.Results[0].GeneratedSources.Single(s => s.HintName == "Orders.Q.g.cs").SourceText.ToString();

    [Theory]
    [InlineData("delete from orders where customer_id in (select id from customers where id = @Id)", "Q(int Id)")]
    [InlineData("delete from orders where exists (select 1 from customers where id = @Id)", "Q(int Id)")]
    [InlineData("delete from orders where exists (select 1 from customers c where c.id = @Id)", "Q(int Id)")]
    [InlineData("select o.id from orders o where o.customer_id in (select id from customers where id = @Id)", "Q(int Id)")]
    [InlineData("select id from orders where customer_id in (select id from customers where region_id in (select id from regions where id = @Id))", "Q(short Id)")]
    [InlineData("with x as (select id from customers where id = @Id) delete from orders where customer_id in (select id from x)", "Q(int Id)")]
    [InlineData("with x as (select 1 as one) delete from orders where customer_id in (select id from customers where id = @Id)", "Q(int Id)")]
    [InlineData("insert into orders (customer_id) select id from customers where id = @Id", "Q(int Id)")]
    [InlineData("delete from orders where id = @Id and customer_id in (select id from customers where note = @Note)", "Q(long Id, string? Note = default)")]
    [InlineData("select o.id from orders o where exists (select 1 from customers c where c.id = o.customer_id and o.id = @Id)", "Q(long Id)")]
    [InlineData("select id from orders where customer_id in (select id from customers c where exists (select 1 from regions r where r.id = c.region_id and c.id = @Id))", "Q(int Id)")]
    [InlineData("select id from orders where exists (select 1 from customers where status = @S)", "Q(string S)")]
    [InlineData("select id from orders where exists (select 1 from customers where id = @Id and customer_id = @C)", "Q(int Id, int C)")]
    [InlineData("with recent as (select id from customers) select id from orders where customer_id in (select id from recent where id = @Id)", "Q(int Id)")]
    [InlineData("with recent as (select id from customers) select id from orders where customer_id in (select r.id from recent r where r.id = @Id)", "Q(int Id)")]
    public void AParameterIsTypedFromTheScopeThatBindsIt(string sql, string signature)
    {
        var result = Run(sql);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(signature, Source(result));
    }

    [Fact]
    public void AStringComparedInsideASubquery_IsSizedFromTheSubquerysColumn()
    {
        var source = Source(Run("update orders set note = @Note where customer_id in (select id from customers where note = @CNote)", "sqlserver"));

        Assert.Contains("__p0.Size = 10;", source);
        Assert.Contains("__p1.Size = CNote == null ? 50 : (CNote.Length > 50 ? CNote.Length : 50);", source);
    }

    [Fact]
    public void ACorrelatedStringComparison_IsSizedFromTheOuterTablesColumn()
    {
        var source = Source(Run("select o.id from orders o where exists (select 1 from customers c where c.id = o.customer_id and o.note = @Note)", "sqlserver"));

        Assert.Contains("Size = Note == null ? 10 : (Note.Length > 10 ? Note.Length : 10);", source);
    }

    [Fact]
    public void AnInsertSelectWriteSlot_KeepsTheTargetColumn()
    {
        var source = Source(Run("insert into orders (note, customer_id) select @Note, id from customers where id = @Id"));

        Assert.Contains("Q(string? Note, int Id)", source);
        Assert.Contains("exceeds orders.note max length (10).", source);
    }

    [Fact]
    public void ASubqueryEqualityOnASameNamedColumn_DoesNotPinTheOuterPage()
    {
        var result = Run("select id from orders where customer_id in (select id from customers where id = @Id) limit 10");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8010");
    }

    [Fact]
    public void ASubqueryFilterOnASameNamedColumn_DoesNotCoverTheOuterCompositeIndex()
    {
        var result = Run("select id from orders where note = @Note and exists (select 1 from customers where region_id = @Region)");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8004" && d.GetMessage().Contains("orders.note"));
    }

    [Fact]
    public void ASubqueryFilterOnASameNamedColumn_IsNotCheckedAgainstTheOuterTable()
    {
        var result = Run("select id from orders where exists (select 1 from customers where email = @Email)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004");
    }

    [Fact]
    public void ASubqueryFilterOnAColumnOnlyTheOuterTableHas_IsCheckedAgainstTheOuterTable()
    {
        var result = Run("select id from orders where exists (select 1 from customers where status = @S)");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8004" && d.GetMessage().Contains("orders.status"));
    }

    [Fact]
    public void ASubqueryFilterOnACteColumn_IsNotCheckedAgainstTheOuterTable()
    {
        var result = Run("with recent as (select id, note as status from customers) select id from orders where customer_id in (select id from recent where status = @S)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004" && d.GetMessage().Contains("orders.status"));
    }
}
