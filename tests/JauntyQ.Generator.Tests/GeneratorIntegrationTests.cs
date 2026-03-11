using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class GeneratorIntegrationTests
{
    private const string SchemaJson = @"{
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""category_id"": { ""name"": ""category_id"", ""dbType"": ""int"", ""isNullable"": true }
      }
    },
    ""categories"": {
      ""name"": ""categories"",
      ""columns"": {
        ""category_id"": { ""name"": ""category_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""category_name"": { ""name"": ""category_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""products"", ""fromColumn"": ""category_id"", ""toTable"": ""categories"", ""toColumn"": ""category_id"" }
  ]
}";

    private static (GeneratorDriverRunResult result, Compilation compilation) RunGenerator(
        string sql, string sqlFileName = "GetProducts.sql")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("");
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.Common.DbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
        };

        // Add runtime assembly references
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
                new InMemoryAdditionalText(sqlFileName, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)
            ));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var result = driver.GetRunResult();

        return (result, outputCompilation);
    }

    [Fact]
    public void SimpleQuery_GeneratesSource()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        Assert.Single(result.GeneratedTrees);
        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("GetProductsRow", source);
        Assert.Contains("GetProducts", source);
    }

    [Fact]
    public void GeneratedCode_ContainsDtoWithCorrectProperties()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("public int ProductId { get; set; }", source);
        Assert.Contains("public string ProductName { get; set; }", source);
    }

    [Fact]
    public void GeneratedCode_ContainsQueryMethod()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("public static System.Collections.Generic.List<GetProductsRow> GetProducts(", source);
        Assert.Contains("System.Data.Common.DbConnection conn", source);
    }

    [Fact]
    public void GeneratedCode_UsesOrdinalReaderAccess()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("reader.GetInt32(0)", source);
        Assert.Contains("reader.GetString(1)", source);
    }

    [Fact]
    public void ParameterizedQuery_GeneratesParameterBinding()
    {
        var sql = @"select p.product_id, p.product_name
from products p
where p.category_id = @categoryId";

        var (result, _) = RunGenerator(sql);

        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("@categoryId", source);
        Assert.Contains("CreateParameter()", source);
        Assert.Contains("cmd.Parameters.Add(", source);
    }

    [Fact]
    public void NullableColumn_GeneratesNullCheck()
    {
        var sql = "select p.category_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("int?", source);
        Assert.Contains("reader.IsDBNull(0)", source);
    }

    [Fact]
    public void JoinQuery_GeneratesCorrectly()
    {
        var sql = @"select p.product_id, p.product_name, c.category_name
from products p
join categories c on p.category_id = c.category_id
where p.category_id = @categoryId";

        var (result, _) = RunGenerator(sql, "GetProductsByCategory.sql");

        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("GetProductsByCategoryRow", source);
        Assert.Contains("ProductId", source);
        Assert.Contains("ProductName", source);
        Assert.Contains("CategoryName", source);
    }

    [Fact]
    public void InvalidQuery_EmitsDiagnostic_NoSourceGenerated()
    {
        var sql = "select p.nonexistent_col from products p";
        var (result, _) = RunGenerator(sql);

        Assert.Empty(result.GeneratedTrees);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.Id == "JAUNTY001");
    }

    [Fact]
    public void GeneratedCode_IsInCorrectNamespace()
    {
        var sql = "select p.product_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("namespace JauntyQ.Generated", source);
    }

    [Fact]
    public void GeneratedCode_IsPartialClass()
    {
        var sql = "select p.product_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("public static partial class Queries", source);
    }

    [Fact]
    public void ParameterType_InferredFromWhereClause()
    {
        // category_id is int in schema — parameter type should be inferred as int?
        // (nullable because products.category_id is nullable in the test schema)
        var sql = @"select p.product_id, p.product_name
from products p
where p.category_id = @categoryId";

        var (result, _) = RunGenerator(sql);

        var source = result.GeneratedTrees[0].GetText().ToString();
        // Should contain "int? categoryId" (not "object categoryId")
        Assert.Contains("int?", source);
        Assert.Contains("categoryId", source);
        Assert.DoesNotContain("object categoryId", source);
    }

    [Fact]
    public void ParameterType_NonNullableColumn_InferredCorrectly()
    {
        // product_id is int NOT NULL — parameter type should be plain int
        var sql = @"select p.product_id
from products p
where p.product_id = @productId";

        var (result, _) = RunGenerator(sql);

        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("int productId", source);
        Assert.DoesNotContain("object productId", source);
    }

    [Fact]
    public void ParameterType_StringColumn_InferredCorrectly()
    {
        // product_name is varchar NOT NULL — parameter type should be string
        var sql = @"select p.product_id
from products p
where p.product_name = @name";

        var (result, _) = RunGenerator(sql);

        var source = result.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("string name", source);
        Assert.DoesNotContain("object name", source);
    }

    [Fact]
    public void UnresolvableParameter_EmitsJAUNTY008Warning()
    {
        // @limit has no column binding — should emit JAUNTY008
        var sql = @"select p.product_id from products p limit @limit";

        var (result, _) = RunGenerator(sql);

        Assert.Contains(result.Diagnostics, d => d.Id == "JAUNTY008");
    }
}

/// <summary>
/// In-memory AdditionalText for testing the generator.
/// </summary>
internal class InMemoryAdditionalText : AdditionalText
{
    private readonly string _text;

    public InMemoryAdditionalText(string path, string text)
    {
        Path = path;
        _text = text;
    }

    public override string Path { get; }

    public override SourceText? GetText(CancellationToken cancellationToken = default)
    {
        return SourceText.From(_text);
    }
}
