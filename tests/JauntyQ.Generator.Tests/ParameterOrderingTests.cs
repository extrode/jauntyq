using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Consumer-gaps-report gap #5: query.Parameters is ordered by each @name
/// token's first appearance in the SQL text, not by any author-declared
/// order. When a `-- @params` directive is present, its declared order is
/// the one place a query author states the intended parameter order --
/// the generated method signature should match it instead of silently
/// differing (a caller using positional/named args reading the directive
/// would otherwise get a mismatched signature).
/// </summary>
public class ParameterOrderingTests
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
    }
  }
}";

    private static GeneratorDriverRunResult RunGenerator(string sql, string sqlFilePath = "db/Products/GetFiltered.sql")
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

        var compilation = CSharpCompilation.Create("ParameterOrderingTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(sqlFilePath, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)
            ))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string GetSource(GeneratorDriverRunResult result, string hintSubstring)
    {
        foreach (var tree in result.GeneratedTrees)
        {
            if (tree.FilePath.Contains(hintSubstring))
                return tree.GetText().ToString();
        }
        throw new System.Exception($"No generated tree matching '{hintSubstring}' found. Available: " +
            string.Join(", ", result.GeneratedTrees.Select(t => t.FilePath)));
    }

    [Fact]
    public void ParamsDirective_ReordersSignatureToDeclaredOrder()
    {
        // First appearance in the WHERE clause is @CategoryId then @ProductId,
        // but the directive declares ProductId first -- the signature should
        // follow the directive, not raw token order.
        var sql = "select product_id, product_name from products " +
                   "where category_id = @CategoryId or product_id = @ProductId\n" +
                   "-- @params ProductId:int, CategoryId:int";

        var result = RunGenerator(sql);
        var source = GetSource(result, "Products.GetFiltered.g.cs");

        Assert.Contains("GetFiltered(int ProductId, int CategoryId)", source);
    }

    [Fact]
    public void NoParamsDirective_KeepsFirstAppearanceOrder()
    {
        // Without a directive, behavior is unchanged: parameters stay in
        // first-appearance order (CategoryId before ProductId here).
        var sql = "select product_id, product_name from products " +
                   "where category_id = @CategoryId or product_id = @ProductId";

        var result = RunGenerator(sql);
        var source = GetSource(result, "Products.GetFiltered.g.cs");

        Assert.Contains("GetFiltered(int? CategoryId, int ProductId)", source);
    }
}
