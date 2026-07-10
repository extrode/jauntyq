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
            ))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

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
            .AddAdditionalTexts(ImmutableArray.CreateRange(additionalTexts))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

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
        // One SQL file should produce: shape guard + query file + entity core + JauntyDb = 4
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        Assert.Equal(4, result.GeneratedTrees.Length);
    }

    [Fact]
    public void SimpleQuery_GeneratesQuerySource()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("Result", source);
        Assert.Contains("GetProducts", source);
    }

    // ── DTO ────────────────────────────────────────────────

    [Fact]
    public void GeneratedCode_ContainsDtoWithCorrectProperties()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("public required int ProductId { get; set; }", source);
        Assert.Contains("public required string ProductName { get; set; }", source);
    }

    // ── Instance method (no conn param) ────────────────────

    [Fact]
    public void GeneratedCode_ContainsInstanceMethod()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("public System.Collections.Generic.List<Result.GetProducts> GetProducts()", source);
    }

    // ── Static method (with conn param) ────────────────────

    [Fact]
    public void GeneratedCode_ContainsStaticMethod()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("public static System.Collections.Generic.List<Result.GetProducts> GetProducts(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction? transaction = null)", source);
    }

    [Fact]
    public void StaticMethod_AssignsCallerTransactionToCommand()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        // Static variants enlist in the caller's transaction: SqlClient throws
        // if a command runs on a connection with an active transaction the
        // command doesn't carry.
        Assert.Contains("if (transaction != null) cmd.Transaction = transaction;", source);
        // Instance variants keep flowing the ambient JauntyDb transaction.
        Assert.Contains("if (_db?.CurrentTransaction != null) cmd.Transaction = _db.CurrentTransaction;", source);
    }

    [Fact]
    public void StaticAsyncMethod_TransactionPrecedesCancellationToken()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains(
            "GetProductsAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction? transaction = null, System.Threading.CancellationToken cancellationToken = default)",
            source);
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
        Assert.Contains("public System.Collections.Generic.List<Result.GetProducts> GetProducts(int? categoryId)", source);
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
        Assert.Contains("public static System.Collections.Generic.List<Result.GetProducts> GetProducts(System.Data.Common.DbConnection conn, int? categoryId, System.Data.Common.DbTransaction? transaction = null)", source);
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
        Assert.Contains("Result.GetProductsByCategory", source);
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
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT2002");
        // Only the always-present shape guard source; no per-query source generated
        // (core + db also skipped since entity set is empty)
        var single = Assert.Single(result.GeneratedTrees);
        Assert.Contains("JauntyQShapeGuard", single.FilePath);
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
    public void UnresolvableParameter_EmitsJNT4003Error_AndEmitsNoSource()
    {
        // @limit has no column binding — should emit JNT4003 as an error and
        // skip emitting the query source entirely (no `object` fallback).
        var sql = @"select p.product_id from products p limit @limit";

        var (result, _) = RunGenerator(sql);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT4003");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("-- @params limit:<type>", diag.GetMessage());
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("Products.GetProducts"));
    }

    [Fact]
    public void UnresolvableCrudParameter_EmitsJNT4003Error_AndEmitsNoSource()
    {
        // The column list references a column that doesn't exist in the
        // schema, so the parameter bound to it (positionally) can't resolve
        // a type.
        var sql = "INSERT INTO products (unknown_column) VALUES (@unknown_thing)";
        var (result, _) = RunGenerator(sql, "db/Products/Insert.sql");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT4003");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("Products.Insert"));
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
    // ── CRUD generation ───────────────────────────────────

    [Fact]
    public void InsertStatement_GeneratesExecuteNonQuery()
    {
        var sql = "INSERT INTO products (product_name, category_id) VALUES (@product_name, @category_id)";
        var (result, _) = RunGenerator(sql, "db/Products/Insert.sql");

        var source = GetSource(result, "Products.Insert.g.cs");
        Assert.Contains("public int Insert(", source);
        Assert.Contains("ExecuteNonQuery()", source);
        Assert.DoesNotContain("ExecuteReader", source);
        Assert.DoesNotContain("Result", source);
    }

    [Fact]
    public void UpdateStatement_GeneratesExecuteNonQuery()
    {
        var sql = "UPDATE products SET product_name = @product_name WHERE product_id = @product_id";
        var (result, _) = RunGenerator(sql, "db/Products/Update.sql");

        var source = GetSource(result, "Products.Update.g.cs");
        Assert.Contains("public int Update(", source);
        Assert.Contains("public static int Update(", source);
        Assert.Contains("ExecuteNonQuery()", source);
    }

    [Fact]
    public void DeleteStatement_GeneratesExecuteNonQuery()
    {
        var sql = "DELETE FROM products WHERE product_id = @product_id";
        var (result, _) = RunGenerator(sql, "db/Products/Delete.sql");

        var source = GetSource(result, "Products.Delete.g.cs");
        Assert.Contains("public int Delete(", source);
        Assert.Contains("ExecuteNonQuery()", source);
    }

    [Fact]
    public void CrudStatement_InfersParameterTypesFromSchema()
    {
        var sql = "INSERT INTO products (product_name, category_id) VALUES (@product_name, @category_id)";
        var (result, _) = RunGenerator(sql, "db/Products/Insert.sql");

        var source = GetSource(result, "Products.Insert.g.cs");
        Assert.Contains("string product_name", source);
        Assert.Contains("int? category_id", source);
        // Method signature should not have "object" as a parameter type
        Assert.DoesNotContain("object product_name", source);
        Assert.DoesNotContain("object category_id", source);
    }

    [Fact]
    public void CrudStatement_SharesEntityPartialClass()
    {
        var (result, _) = RunGeneratorMultiFile(
            ("db/Products/GetAll.sql", "select p.product_id, p.product_name from products p"),
            ("db/Products/Insert.sql", "INSERT INTO products (product_name) VALUES (@product_name)")
        );

        var getAllSource = GetSource(result, "Products.GetAll.g.cs");
        Assert.Contains("public partial class Products", getAllSource);
        Assert.Contains("Result.GetAll", getAllSource);

        var insertSource = GetSource(result, "Products.Insert.g.cs");
        Assert.Contains("public partial class Products", insertSource);
        Assert.Contains("public int Insert(", insertSource);
    }

    // ── Folder structure: tables/ and views/ prefixes ──────

    [Fact]
    public void TablesPrefix_ExtractsCorrectEntityName()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGeneratorMultiFile(
            ("db/tables/Products/GetAll.sql", sql),
            ("db/tables/Categories/GetAll.sql", "select c.category_id, c.category_name from categories c")
        );

        var productsSource = GetSource(result, "Products.GetAll.g.cs");
        Assert.Contains("public partial class Products", productsSource);
        Assert.DoesNotContain("class Tables", productsSource);

        var categoriesSource = GetSource(result, "Categories.GetAll.g.cs");
        Assert.Contains("public partial class Categories", categoriesSource);

        var dbSource = GetSource(result, "JauntyDb.g.cs");
        Assert.Contains("public Products Products =>", dbSource);
        Assert.Contains("public Categories Categories =>", dbSource);
    }

    [Fact]
    public void ViewsPrefix_ExtractsCorrectEntityName()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGeneratorMultiFile(
            ("db/views/ProductSummary/GetAll.sql", sql),
            ("db/tables/Products/GetById.sql", "select p.product_id, p.product_name from products p where p.product_id = @productId")
        );

        var viewSource = GetSource(result, "ProductSummary.GetAll.g.cs");
        Assert.Contains("public partial class ProductSummary", viewSource);

        var tableSource = GetSource(result, "Products.GetById.g.cs");
        Assert.Contains("public partial class Products", tableSource);

        var dbSource = GetSource(result, "JauntyDb.g.cs");
        Assert.Contains("public ProductSummary ProductSummary =>", dbSource);
        Assert.Contains("public Products Products =>", dbSource);
    }

    [Fact]
    public void TablesPrefix_CrudWorksCorrectly()
    {
        var (result, _) = RunGeneratorMultiFile(
            ("db/tables/Products/GetAll.sql", "select p.product_id, p.product_name from products p"),
            ("db/tables/Products/Insert.sql", "INSERT INTO products (product_name, category_id) VALUES (@product_name, @category_id)")
        );

        var getAllSource = GetSource(result, "Products.GetAll.g.cs");
        Assert.Contains("Result.GetAll", getAllSource);

        var insertSource = GetSource(result, "Products.Insert.g.cs");
        Assert.Contains("public int Insert(", insertSource);
        Assert.Contains("ExecuteNonQuery()", insertSource);
    }

    // ── @proc directive ───────────────────────────────────

    [Fact]
    public void ProcDirective_EmitsStoredProcedureCommandType()
    {
        var sql = "-- @proc\nselect p.product_id, p.product_name from products p where p.category_id = @categoryId";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("cmd.CommandType = System.Data.CommandType.StoredProcedure", source);
    }

    [Fact]
    public void ProcDirective_DefaultNaming()
    {
        var sql = "-- @proc\nselect p.product_id, p.product_name from products p where p.category_id = @categoryId";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("cmd.CommandText = \"Products_GetProducts\"", source);
    }

    [Fact]
    public void ProcDirective_CustomName()
    {
        var sql = "-- @proc sp_GetProducts\nselect p.product_id, p.product_name from products p where p.category_id = @categoryId";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("cmd.CommandText = \"sp_GetProducts\"", source);
        Assert.DoesNotContain("Products_GetProducts", source);
    }

    [Fact]
    public void ProcDirective_EmitsProcConstant()
    {
        var sql = "-- @proc\nselect p.product_id, p.product_name from products p where p.category_id = @categoryId";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("public static partial class Proc", source);
        Assert.Contains("public const string GetProducts", source);
        Assert.Contains("CREATE OR ALTER PROCEDURE [Products_GetProducts]", source);
        Assert.Contains("SET NOCOUNT ON", source);
    }

    [Fact]
    public void ProcDirective_ProcConstantIncludesParameterTypes()
    {
        var sql = "-- @proc\nselect p.product_id, p.product_name from products p where p.category_id = @categoryId";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        // category_id is nullable int in schema → C# int? → SQL int
        Assert.Contains("@categoryId int", source);
    }

    [Fact]
    public void ProcDirective_CrudInsert()
    {
        var sql = "-- @proc\nINSERT INTO products (product_name, category_id) VALUES (@product_name, @category_id)";
        var (result, _) = RunGenerator(sql, "db/Products/Insert.sql");

        var source = GetSource(result, "Products.Insert.g.cs");
        Assert.Contains("cmd.CommandType = System.Data.CommandType.StoredProcedure", source);
        Assert.Contains("cmd.CommandText = \"Products_Insert\"", source);
        Assert.Contains("public static partial class Proc", source);
        Assert.Contains("CREATE OR ALTER PROCEDURE [Products_Insert]", source);
    }

    [Fact]
    public void NoProcDirective_RemainsTextMode()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.DoesNotContain("CommandType", source);
        Assert.DoesNotContain("StoredProcedure", source);
        Assert.DoesNotContain("partial class Proc", source);
        Assert.Contains("cmd.CommandText = @\"", source);
    }

    // ── GW-2: async variants ───────────────────────────────

    [Fact]
    public void AsyncVariants_AreGenerated()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("GetProductsAsync(", source);
        Assert.Contains("System.Threading.CancellationToken cancellationToken = default", source);
        Assert.Contains("ExecuteReaderAsync(System.Data.CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false)", source);
        Assert.Contains("OpenAsync(cancellationToken).ConfigureAwait(false)", source);
        Assert.Contains("CloseAsync().ConfigureAwait(false)", source);
    }

    [Fact]
    public void CrudAsyncVariants_AreGenerated()
    {
        var sql = "INSERT INTO products (product_name, category_id) VALUES (@product_name, @category_id)";
        var (result, _) = RunGenerator(sql, "db/Products/Insert.sql");

        var source = GetSource(result, "Products.Insert.g.cs");
        Assert.Contains("InsertAsync(", source);
        Assert.Contains("ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)", source);
    }

    // ── GW-2: one-time shape guard ─────────────────────────

    [Fact]
    public void ShapeGuard_ColumnArrayAndValidateCall_AreEmitted()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("private static readonly string[] __GetProductsColumns = { \"product_id\", \"product_name\" };", source);
        Assert.Contains("JauntyQShapeGuard.Validate(reader, __GetProductsColumns, \"Products.GetProducts\");", source);
    }

    // ── GW-2: -- @first ────────────────────────────────────

    [Fact]
    public void FirstDirective_ReturnsNullableRowInsteadOfList()
    {
        var sql = "-- @first\nselect p.product_id, p.product_name from products p where p.product_id = @product_id";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("public Result.GetProducts? GetProducts(", source);
        Assert.Contains("System.Threading.Tasks.Task<Result.GetProducts?> GetProductsAsync(", source);
        Assert.Contains("return null;", source);
        Assert.DoesNotContain("List<Result.GetProducts>", source);
    }

    // ── GW-2: DbType binding ───────────────────────────────

    [Fact]
    public void Parameters_GetDbTypeFromSchema()
    {
        var sql = "select p.product_id from products p where p.category_id = @category_id";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("p0.DbType = System.Data.DbType.Int32;", source);
    }

    // ── GW-2: leading comment stripping ────────────────────

    [Fact]
    public void LeadingComments_AreStrippedFromCommandText()
    {
        var sql = "--select old_col from old_table\n\nselect p.product_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.DoesNotContain("old_table", source);
        Assert.Contains("select p.product_id from products p", source);
    }

    [Fact]
    public void MidSqlComments_ArePreservedInCommandText()
    {
        // Leading comment stripped; mid-SQL comment on the WHERE line must survive
        var sql = @"-- @first
-- old leading comment
select p.product_id from products p -- filter
where p.product_id = @product_id";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        // Leading directive/comment lines stripped
        Assert.DoesNotContain("old leading comment", source);
        // Mid-SQL comment preserved
        Assert.Contains("-- filter", source);
    }

    // ── JNT3002: SELECT * forbidden ────────────────────────

    [Fact]
    public void SelectStar_IsRejected_WithPasteReadyColumnList()
    {
        var sql = "select * from products p";
        var (result, _) = RunGenerator(sql);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3002");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("Replace * with: product_id, product_name, category_id", diag.GetMessage());
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("Products.GetProducts"));
    }

    [Fact]
    public void SelectStar_JoinQuery_FixListIsAliasQualified()
    {
        var sql = "select * from products p join categories c on p.category_id = c.category_id";
        var (result, _) = RunGenerator(sql);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3002");
        Assert.Contains("p.product_id", diag.GetMessage());
        Assert.Contains("c.category_name", diag.GetMessage());
    }

    // ── shared materializer + CommandBehavior hints ────────

    [Fact]
    public void Materializer_EmittedOnce_CalledFromAllFourVariants()
    {
        var sql = "select p.product_id, p.product_name from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Equal(1, CountOccurrences(source, "__MapGetProducts(System.Data.Common.DbDataReader reader)"));
        Assert.Equal(4, CountOccurrences(source, "__MapGetProducts(reader)"));
    }

    [Fact]
    public void ListQuery_UsesSingleResultBehavior()
    {
        var sql = "select p.product_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("cmd.ExecuteReader(System.Data.CommandBehavior.SingleResult)", source);
        Assert.DoesNotContain("SingleRow", source);
    }

    [Fact]
    public void FirstDirective_UsesSingleRowBehavior()
    {
        var sql = "-- @first\nselect p.product_id from products p where p.product_id = @product_id";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        Assert.Contains("System.Data.CommandBehavior.SingleRow | System.Data.CommandBehavior.SingleResult", source);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    // ── Tier 1: transactions ───────────────────────────────

    [Fact]
    public void JauntyDb_ExposesTransactionApi()
    {
        var sql = "select p.product_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "JauntyDb.g.cs");
        Assert.Contains("public Transaction BeginTransaction()", source);
        Assert.Contains("public async System.Threading.Tasks.Task<Transaction> BeginTransactionAsync(", source);
        Assert.Contains("public sealed class Transaction : System.IDisposable", source);
        Assert.Contains("new Products(this)", source);
    }

    [Fact]
    public void InstanceMethods_EnlistInActiveTransaction_StaticsDoNot()
    {
        var sql = "select p.product_id from products p";
        var (result, _) = RunGenerator(sql);

        var source = GetSource(result, "Products.GetProducts.g.cs");
        // exactly the two instance variants (sync + async) enlist
        Assert.Equal(2, CountOccurrences(source, "if (_db?.CurrentTransaction != null) cmd.Transaction = _db.CurrentTransaction;"));

        var core = GetSource(result, "Products.Core.g.cs");
        Assert.Contains("internal Products(JauntyDb db)", core);
    }
}

/// <summary>
/// GW-4: incremental pipeline caching. Editing one .sql file must not re-run
/// the per-file transform for other files, and a body edit that keeps a
/// file's shape (entity, method, canonical row) must leave the aggregate
/// input (synthetics / POCO overloads / facade) cached.
/// </summary>
public class IncrementalCachingTests
{
    private const string SchemaJson = @"{
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""unit_price"": { ""name"": ""unit_price"", ""dbType"": ""decimal"", ""isNullable"": false }
      }
    }
  }
}";

    private const string SqlA = @"select product_id, product_name
from products
where products.unit_price > @unit_price";

    private const string SqlB = @"select product_id
from products
where products.product_name = @product_name";

    private static GeneratorDriver CreateDriver(params AdditionalText[] texts)
    {
        var generator = new JauntyQGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.AddAdditionalTexts(ImmutableArray.Create(texts));
        driver = driver.WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));
        return driver;
    }

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create("IncrementalTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [Fact]
    public void EditingOneFile_LeavesOtherFilePerFileStepCached()
    {
        var fileA = new InMemoryAdditionalText("db/Products/GetExpensive.sql", SqlA);
        var fileB = new InMemoryAdditionalText("db/Products/GetIdByName.sql", SqlB);
        var schema = new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson);

        var driver = CreateDriver(fileA, fileB, schema);
        var compilation = CreateCompilation();
        driver = driver.RunGenerators(compilation);

        // Body-only edit: > becomes >= (same entity/method/shape)
        var edited = new InMemoryAdditionalText(fileA.Path, SqlA.Replace(">", ">="));
        driver = driver.ReplaceAdditionalText(fileA, edited);
        driver = driver.RunGenerators(compilation);

        var steps = driver.GetRunResult().Results[0].TrackedSteps["JauntyQ_PerFile"];
        int recomputed = 0, cached = 0, total = 0;
        foreach (var step in steps)
        {
            foreach (var output in step.Outputs)
            {
                total++;
                if (output.Reason == IncrementalStepRunReason.Modified || output.Reason == IncrementalStepRunReason.New)
                    recomputed++;
                if (output.Reason == IncrementalStepRunReason.Cached || output.Reason == IncrementalStepRunReason.Unchanged)
                    cached++;
            }
        }

        Assert.Equal(2, total);      // one per .sql file
        Assert.Equal(1, recomputed); // the edited file
        Assert.Equal(1, cached);     // the untouched file
    }

    [Fact]
    public void BodyEditSameShape_LeavesAggregateInputCached()
    {
        var fileA = new InMemoryAdditionalText("db/Products/GetExpensive.sql", SqlA);
        var fileB = new InMemoryAdditionalText("db/Products/GetIdByName.sql", SqlB);
        var schema = new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson);

        var driver = CreateDriver(fileA, fileB, schema);
        var compilation = CreateCompilation();
        driver = driver.RunGenerators(compilation);

        var edited = new InMemoryAdditionalText(fileA.Path, SqlA.Replace(">", ">="));
        driver = driver.ReplaceAdditionalText(fileA, edited);
        driver = driver.RunGenerators(compilation);

        // Same file set, same shapes: synthetics/POCOs/facade must not re-run.
        var steps = driver.GetRunResult().Results[0].TrackedSteps["JauntyQ_AggregateInput"];
        Assert.NotEmpty(steps);
        foreach (var step in steps)
        {
            foreach (var output in step.Outputs)
            {
                Assert.True(
                    output.Reason == IncrementalStepRunReason.Cached || output.Reason == IncrementalStepRunReason.Unchanged,
                    $"aggregate input re-ran after a body-only edit: {output.Reason}");
            }
        }
    }

    [Fact]
    public void ShapeChange_ReRunsAggregate()
    {
        var fileA = new InMemoryAdditionalText("db/Products/GetExpensive.sql", SqlA);
        var fileB = new InMemoryAdditionalText("db/Products/GetIdByName.sql", SqlB);
        var schema = new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson);

        var driver = CreateDriver(fileA, fileB, schema);
        var compilation = CreateCompilation();
        driver = driver.RunGenerators(compilation);

        // Same method, new shape: full-column projection now resolves to the
        // canonical row POCO, so the aggregate must wake up and emit it.
        var edited = new InMemoryAdditionalText(fileA.Path, @"select product_id, product_name, unit_price
from products
where products.unit_price > @unit_price");
        driver = driver.ReplaceAdditionalText(fileA, edited);
        driver = driver.RunGenerators(compilation);

        var result = driver.GetRunResult().Results[0];

        bool aggregateReRan = false;
        foreach (var step in result.TrackedSteps["JauntyQ_AggregateInput"])
        {
            foreach (var output in step.Outputs)
            {
                if (output.Reason == IncrementalStepRunReason.Modified || output.Reason == IncrementalStepRunReason.New)
                    aggregateReRan = true;
            }
        }
        Assert.True(aggregateReRan);

        bool rowPocoEmitted = false;
        foreach (var source in result.GeneratedSources)
        {
            if (source.HintName == "Products.Row.g.cs")
                rowPocoEmitted = true;
        }
        Assert.True(rowPocoEmitted);
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

/// <summary>
/// Analyzer config stub controlling build_property.JauntyQAutoCrud for tests.
/// </summary>
internal sealed class TestAnalyzerConfigOptionsProvider : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
{
    private readonly TestAnalyzerConfigOptions _global;

    // dialect is null when the consumer sets no <JauntyQDialect> property (the
    // common case with a JSON snapshot); a non-null value stubs the property.
    public TestAnalyzerConfigOptionsProvider(bool autoCrud, string? dialect = null)
        => _global = new TestAnalyzerConfigOptions(autoCrud, dialect);

    public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions => _global;
    public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _global;
    public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _global;

    private sealed class TestAnalyzerConfigOptions : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
    {
        private readonly bool _autoCrud;
        private readonly string? _dialect;
        public TestAnalyzerConfigOptions(bool autoCrud, string? dialect)
        {
            _autoCrud = autoCrud;
            _dialect = dialect;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_property.JauntyQAutoCrud")
            {
                value = _autoCrud ? "true" : "false";
                return true;
            }
            if (key == "build_property.JauntyQDialect" && _dialect != null)
            {
                value = _dialect;
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
}
