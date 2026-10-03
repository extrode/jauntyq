using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

[Trait("Category", "AuditRegression")]
public class UpsertAssignmentTests
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
        var compilation = CSharpCompilation.Create("UpsertAssignmentAssembly",
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

    private static string[] Codes(GeneratorDriverRunResult result) =>
        result.Diagnostics.Select(d => d.Id).ToArray();

    [Theory]
    [InlineData("insert into orders (id, customer_id, region_id, status) values (@Id, @CustomerId, @RegionId, @Status) on conflict (id) do update set status = @NewStatus", "postgres")]
    [InlineData("insert into orders (id, customer_id, region_id, status) values (@Id, @CustomerId, @RegionId, @Status) on conflict (id) do update set status = @NewStatus where orders.region_id = @RegionId", "postgres")]
    [InlineData("insert into orders (id, customer_id, region_id, status) values (@Id, @CustomerId, @RegionId, @Status) on conflict (id) do update set status = @NewStatus", "sqlite")]
    [InlineData("insert into orders (id, customer_id, region_id, status) values (@Id, @CustomerId, @RegionId, @Status) on duplicate key update status = @NewStatus", "mysql")]
    [InlineData("insert into orders (id, customer_id, region_id, status) select @Id, @CustomerId, id, @Status from regions where id = @RegionId on conflict (id) do update set status = @NewStatus", "postgres")]
    public void AnUpsertAssignment_IsTypedFromTheTargetColumn(string sql, string dialect)
    {
        var result = Run(sql, dialect);

        Assert.DoesNotContain("JNT4003", Codes(result));
        Assert.Contains("string NewStatus", Source(result));
    }

    [Fact]
    public void AnUpsertAssignment_IsLengthCheckedLikeAnyWrite()
    {
        var result = Run("insert into orders (id, customer_id, region_id, status) values (@Id, @CustomerId, @RegionId, @Status) on conflict (id) do update set note = @NewNote");

        Assert.Contains("NewNote.Length > 10", Source(result));
    }

    [Fact]
    public void AnUpsertWhereComparison_IsTyped()
    {
        var result = Run("insert into orders (id, customer_id, region_id, status) values (@Id, @CustomerId, @RegionId, @Status) on conflict (id) do update set status = excluded.status where orders.customer_id = @Owner");

        Assert.DoesNotContain("JNT4003", Codes(result));
        Assert.Contains("int Owner", Source(result));
    }
}
