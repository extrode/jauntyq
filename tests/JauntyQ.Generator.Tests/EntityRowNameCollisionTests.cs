using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// JNT2006: a db/tables/&lt;Folder&gt; accessor name that collides with an
/// auto-generated (non-partial) row POCO name is CS0260 at compile time —
/// these tests confirm the generator surfaces it as a diagnostic instead of
/// leaving the user to decode the raw compiler error from generated code.
/// </summary>
public class EntityRowNameCollisionTests
{
    private const string OrdersSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""order_id"": { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""order_date"": { ""name"": ""order_date"", ""dbType"": ""datetime"", ""isNullable"": false }
      }
    }
  }
}";

    private const string BasketSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""basket"": {
      ""name"": ""basket"",
      ""columns"": {
        ""basket_id"": { ""name"": ""basket_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""buyer_id"": { ""name"": ""buyer_id"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  }
}";

    private static GeneratorDriverRunResult RunGenerator(string schemaJson, params (string path, string sql)[] sqlFiles)
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
        };

        var compilation = CSharpCompilation.Create("EntityRowNameCollisionTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new System.Collections.Generic.List<AdditionalText>();
        foreach (var (path, sql) in sqlFiles)
            texts.Add(new InMemoryAdditionalText(path, sql));
        texts.Add(new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.CreateRange(texts))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Fact]
    public void FolderNamedAfterSingularizedRowType_ReportsJnt2006()
    {
        // db/tables/Order/ (matching the domain name, not the table's own
        // PascalCase "Orders") makes the accessor "Order" — exactly the
        // singularized row-POCO name for the "orders" table.
        var result = RunGenerator(OrdersSchemaJson,
            ("db/tables/Order/GetRecent.sql", "select order_id, order_date from orders where order_id = @order_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT2006");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("'Order'", diag.GetMessage());
        Assert.Contains("orders", diag.GetMessage());
        Assert.Contains("db/tables/Order/", diag.GetMessage());
        Assert.Contains("'Orders'", diag.GetMessage());
    }

    [Fact]
    public void FolderNamedAfterTablePascalCase_NoCollision_NoDiagnostic()
    {
        // db/tables/Orders/ (the table's own PascalCase form) makes the
        // accessor "Orders", which never collides with the singularized row
        // type "Order" — the documented fix for the collision above.
        var result = RunGenerator(OrdersSchemaJson,
            ("db/tables/Orders/GetRecent.sql", "select order_id, order_date from orders where order_id = @order_id"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2006");
    }

    [Fact]
    public void FolderNamedAfterAlreadySingularTable_RowSuffixFallback_NoDiagnostic()
    {
        // "basket" singularizes to itself (a no-op), so Inflector.RowTypeName
        // already falls back to "BasketRow" — a folder literally named
        // "Basket" (matching the table's own PascalCase form) never collides.
        var result = RunGenerator(BasketSchemaJson,
            ("db/tables/Basket/GetById.sql", "select basket_id, buyer_id from basket where basket_id = @basket_id"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2006");
    }
}
