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
        string sql, string sqlFilePath = "db/Products/GetProducts.sql")
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
                new InMemoryAdditionalText(sqlFilePath, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)
            ));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var result = driver.GetRunResult();

        return (result, outputCompilation);
    }

    private static (GeneratorDriverRunResult result, Compilation compilation) RunGeneratorMultiFile(
        params (string path, string sql)[] sqlFiles)
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

        var additionalTexts = new System.Collections.Generic.List<AdditionalText>();
        foreach (var (path, sql) in sqlFiles)
            additionalTexts.Add(new InMemoryAdditionalText(path, sql));
        additionalTexts.Add(new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson));

        var driver = CSharpGeneratorDriver.Create(generator)
            .AddAdditionalTexts(ImmutableArray.CreateRange(additionalTexts));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var result = driver.GetRunResult();

        return (result, outputCompilation);
    }

    /// <summary>
    /// Find the generated source whose hint name matches the given substring.
    /// </summary>
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

    // ── Basic generation ───────────────────────────────────

    [Fact]
    public void SimpleQuery_GeneratesThreeFiles()
    {
        // One SQL file should produce: query file + entity core + JauntyDb = 3
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        Assert.Equal(3, result.GeneratedTrees.Length);
    }

    [Fact]
    public void SimpleQuery_GeneratesQuerySource()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("ProductsGetProductsRow", source);
        Assert.Contains("GetProducts", source);
    }

    // ── DTO ────────────────────────────────────────────────

    [Fact]
    public void GeneratedCode_ContainsDtoWithCorrectProperties()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("public int ProductId { get; set; }", source);
        Assert.Contains("public string ProductName { get; set; }", source);
    }

    // ── Instance method (no conn param) ────────────────────

    [Fact]
    public void GeneratedCode_ContainsInstanceMethod()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("public System.Collections.Generic.List<ProductsGetProductsRow> GetProducts()", source);
    }

    // ── Static method (with conn param) ────────────────────

    [Fact]
    public void GeneratedCode_ContainsStaticMethod()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("public static System.Collections.Generic.List<ProductsGetProductsRow> GetProducts(System.Data.Common.DbConnection conn)", source);
    }

    // ── Connection lifecycle ───────────────────────────────

    [Fact]
    public void GeneratedCode_ContainsConnectionLifecycle()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("weOpened", source);
        Assert.Contains("try", source);
        Assert.Contains("finally", source);
    }

    // ── Ordinal reader access ──────────────────────────────

    [Fact]
    public void GeneratedCode_UsesOrdinalReaderAccess()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("reader.GetInt32(0)", source);
        Assert.Contains("reader.GetString(1)", source);
    }

    // ── Parameters ─────────────────────────────────────────

    [Fact]
    public void ParameterizedQuery_GeneratesParameterBinding()
    {
        var sql = @"select p.product_id, p.product_name
from products p
where p.category_id = @categoryId";

        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("@categoryId", source);
        Assert.Contains("CreateParameter()", source);
        Assert.Contains("cmd.Parameters.Add(", source);
    }

    [Fact]
    public void ParameterizedQuery_InstanceMethodHasParameterArg()
    {
        var sql = @"select p.product_id, p.product_name
from products p
where p.category_id = @categoryId";

        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        // Instance method: no conn, just the query parameter
        Assert.Contains("public System.Collections.Generic.List<ProductsGetProductsRow> GetProducts(int? categoryId)", source);
    }

    [Fact]
    public void ParameterizedQuery_StaticMethodHasConnAndParameterArgs()
    {
        var sql = @"select p.product_id, p.product_name
from products p
where p.category_id = @categoryId";

        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        // Static method: conn + query parameter
        Assert.Contains("public static System.Collections.Generic.List<ProductsGetProductsRow> GetProducts(System.Data.Common.DbConnection conn, int? categoryId)", source);
    }

    // ── Nullable columns ───────────────────────────────────

    [Fact]
    public void NullableColumn_GeneratesNullCheck()
    {
        var sql = "select p.category_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("int?", source);
        Assert.Contains("reader.IsDBNull(0)", source);
    }

    // ── Join queries ───────────────────────────────────────

    [Fact]
    public void JoinQuery_GeneratesCorrectly()
    {
        var sql = @"select p.product_id, p.product_name, c.category_name
from products p
join categories c on p.category_id = c.category_id
where p.category_id = @categoryId";

        var (result, _) = RunGenerator(sql, "db/Products/GetProductsByCategory.sql");

        var source = GetSource(result, "Products.GetProductsByCategory.g.cs");
        Assert.Contains("ProductsGetProductsByCategoryRow", source);
        Assert.Contains("ProductId", source);
        Assert.Contains("ProductName", source);
        Assert.Contains("CategoryName", source);
    }

    // ── Validation ─────────────────────────────────────────

    [Fact]
    public void InvalidQuery_EmitsDiagnostic_NoQuerySourceGenerated()
    {
        var sql = "select p.nonexistent_col from products p";
        var (result, _) = RunGenerator(sql);

        // Should still get diagnostics
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.Id == "JAUNTY001");
        // No per-query source generated (core + db also skipped since entity set is empty)
        Assert.Empty(result.GeneratedTrees);
    }

    // ── Namespace ──────────────────────────────────────────

    [Fact]
    public void GeneratedCode_IsInCorrectNamespace()
    {
        var sql = "select p.product_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("namespace JauntyQ.Generated", source);
    }

    // ── Partial class with entity name ─────────────────────

    [Fact]
    public void GeneratedCode_IsPartialClassWithEntityName()
    {
        var sql = "select p.product_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("public partial class Products", source);
        Assert.DoesNotContain("static partial class Queries", source);
    }

    // ── Parameter type inference ────────────────────────────

    [Fact]
    public void ParameterType_InferredFromWhereClause()
    {
        // category_id is int in schema — parameter type should be inferred as int?
        // (nullable because products.category_id is nullable in the test schema)
        var sql = @"select p.product_id, p.product_name
from products p
where p.category_id = @categoryId";

        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
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

        var source = GetSource(result, "Products.GetProducts.g.cs");
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

        var source = GetSource(result, "Products.GetProducts.g.cs");
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

    // ── Entity core file ───────────────────────────────────

    [Fact]
    public void EntityCoreFile_GeneratedForEntity()
    {
        var sql = "select p.product_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.Core.g.cs");
        Assert.Contains("public partial class Products", source);
        Assert.Contains("private readonly System.Data.Common.DbConnection _conn;", source);
        Assert.Contains("internal Products(System.Data.Common.DbConnection conn)", source);
    }

    // ── JauntyDb file ──────────────────────────────────────

    [Fact]
    public void JauntyDb_GeneratedWithEntityAccessor()
    {
        var sql = "select p.product_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "JauntyDb.g.cs");
        Assert.Contains("public class JauntyDb", source);
        Assert.Contains("public Products Products =>", source);
    }

    // ── Multi-file / multi-entity ──────────────────────────

    [Fact]
    public void MultipleEntities_GeneratesCorrectJauntyDb()
    {
        var (result, _) = RunGeneratorMultiFile(
            ("db/Products/GetAll.sql", "select p.product_id, p.product_name from products p"),
            ("db/Categories/GetAll.sql", "select c.category_id, c.category_name from categories c")
        );

        var dbSource = GetSource(result, "JauntyDb.g.cs");
        Assert.Contains("public Products Products =>", dbSource);
        Assert.Contains("public Categories Categories =>", dbSource);

        // Verify entity core files for both entities
        var productsCore = GetSource(result, "Products.Core.g.cs");
        Assert.Contains("public partial class Products", productsCore);

        var categoriesCore = GetSource(result, "Categories.Core.g.cs");
        Assert.Contains("public partial class Categories", categoriesCore);
    }

    [Fact]
    public void MultipleQueriesInSameEntity_SharePartialClass()
    {
        var (result, _) = RunGeneratorMultiFile(
            ("db/Products/GetAll.sql", "select p.product_id, p.product_name from products p"),
            ("db/Products/GetById.sql", "select p.product_id, p.product_name from products p where p.product_id = @productId")
        );

        var getAllSource = GetSource(result, "Products.GetAll.g.cs");
        Assert.Contains("public partial class Products", getAllSource);

        var getByIdSource = GetSource(result, "Products.GetById.g.cs");
        Assert.Contains("public partial class Products", getByIdSource);

        // Only one core file for Products
        var coreSource = GetSource(result, "Products.Core.g.cs");
        Assert.Contains("internal Products(", coreSource);
    }

    // ── Root-level SQL (no subfolder) → catch-all "Queries" ─

    [Fact]
    public void RootLevelSqlFile_UsesQueriesCatchAll()
    {
        var sql = "select p.product_id from products p";
        var (result, _) = RunGenerator(sql, "db/GetOrphaned.sql");

        var source = GetSource(result, "Queries.GetOrphaned.g.cs");
        Assert.Contains("public partial class Queries", source);

        var dbSource = GetSource(result, "JauntyDb.g.cs");
        Assert.Contains("public Queries Queries =>", dbSource);
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
