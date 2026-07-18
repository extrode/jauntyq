using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R69-01: extends AUD-R67-01/AUD-R68-01's -- @call collision-avoidance
/// fix to the ordinary hand-written .sql query path (EmitCrudMethodBody/
/// EmitMethodBody in CodeEmitter.Part3.cs/Part8.cs). Unlike -- @call, no
/// schema cooperation is needed here: EmittedParam.CSharpName is the literal
/// SQL parameter name (only keyword-escaped, never PascalCase/camelCase
/// folded), so any .sql file binding a parameter literally named e.g. "@cmd"
/// reaches these formal parameters/locals directly. Each test asserts the
/// exact renamed identifier text AND a real, clean Roslyn compile of the
/// generated code (via the generator driver's own output compilation, the
/// same pattern GeneratorIntegrationTests.cs already uses).
/// </summary>
public class CrudBookkeepingNameCollisionTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isIdentity"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  }
}";

    private static readonly System.Collections.Generic.List<MetadataReference> BaseReferences = BuildBaseReferences();

    private static System.Collections.Generic.List<MetadataReference> BuildBaseReferences()
    {
        string runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return new()
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "netstandard.dll")),
        };
    }

    private static (GeneratorDriverRunResult result, Compilation compilation) RunGenerator(
        string sql, string sqlFilePath = "db/Products/Query.sql")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("");

        var compilation = CSharpCompilation.Create("CrudBookkeepingCollisionAssembly",
            new[] { syntaxTree },
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(sqlFilePath, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver.GetRunResult(), outputCompilation);
    }

    private static string GetSource(GeneratorDriverRunResult result, string hintSubstring)
    {
        foreach (var tree in result.GeneratedTrees)
        {
            if (tree.FilePath.Contains(hintSubstring))
                return tree.GetText().ToString();
        }
        throw new System.InvalidOperationException($"No generated source matching '{hintSubstring}' found.");
    }

    private static void AssertCompilesClean(Compilation compilation, string context)
    {
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"generated code for {context} failed to compile:\n" + string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void Call_ParamNamedCmd_DoesNotCollideWithCommandLocal()
    {
        var (result, compilation) = RunGenerator(
            "select product_id, product_name from products where product_id = @cmd\n",
            "db/Products/GetByCmd.sql");
        string source = GetSource(result, "GetByCmd");

        Assert.Contains("using DbCommand __cmd = ", source);
        AssertCompilesClean(compilation, "a query parameter literally named '@cmd'");
    }

    [Fact]
    public void Call_ParamNamedReader_DoesNotCollideWithDataReaderLocal()
    {
        var (result, compilation) = RunGenerator(
            "select product_id, product_name from products where product_id = @reader\n",
            "db/Products/GetByReader.sql");
        string source = GetSource(result, "GetByReader");

        Assert.Contains("using DbDataReader __reader = ", source);
        AssertCompilesClean(compilation, "a query parameter literally named '@reader'");
    }

    [Fact]
    public void Call_ParamNamedWeOpened_DoesNotCollideWithConnectionLifecycleLocal()
    {
        var (result, compilation) = RunGenerator(
            "select product_id, product_name from products where product_id = @weOpened\n",
            "db/Products/GetByWeOpened.sql");
        string source = GetSource(result, "GetByWeOpened");

        Assert.Contains("bool __weOpened = ", source);
        AssertCompilesClean(compilation, "a query parameter literally named '@weOpened'");
    }

    [Fact]
    public void Call_ParamNamedP0_DoesNotCollideWithBoundParameterLocal()
    {
        var (result, compilation) = RunGenerator(
            "select product_id, product_name from products where product_id = @p0\n",
            "db/Products/GetByP0.sql");
        string source = GetSource(result, "GetByP0");

        Assert.Contains("DbParameter __p0 = __cmd.CreateParameter();", source);
        AssertCompilesClean(compilation, "a query parameter literally named '@p0'");
    }

    [Fact]
    public void Call_ParamNamedConn_DoesNotCollideWithStaticFormalParameter()
    {
        var (result, compilation) = RunGenerator(
            "select product_id, product_name from products where product_id = @conn\n",
            "db/Products/GetByConn.sql");
        string source = GetSource(result, "GetByConn");

        Assert.Contains("DbConnection __conn", source);
        AssertCompilesClean(compilation, "a query parameter literally named '@conn'");
    }

    [Fact]
    public void Call_ParamNamedCancellationToken_DoesNotCollideWithAsyncFormalParameter()
    {
        var (result, compilation) = RunGenerator(
            "select product_id, product_name from products where product_id = @cancellationToken\n",
            "db/Products/GetByCancellationToken.sql");
        string source = GetSource(result, "GetByCancellationToken");

        Assert.Contains("CancellationToken __cancellationToken = default", source);
        AssertCompilesClean(compilation, "a query parameter literally named '@cancellationToken'");
    }

    [Fact]
    public void Call_ParamNamedResults_DoesNotCollideWithRowListLocal()
    {
        var (result, compilation) = RunGenerator(
            "select product_id, product_name from products where product_name = @results\n",
            "db/Products/GetByResults.sql");
        string source = GetSource(result, "GetByResults");

        Assert.Contains("var __results = new List<", source);
        AssertCompilesClean(compilation, "a query parameter literally named '@results'");
    }

    /// <summary>
    /// The identity-insert path (EmitCrudMethodBody's `identity != null`
    /// branch) declares its OWN DbDataReader local to read the
    /// database-assigned identity value back -- a separate emission path
    /// from the ordinary SELECT/query reader above, entangled with the
    /// shared GetReaderCall/GetIdentityReaderCall helper (CodeEmitter.
    /// Part4.cs/Part9.cs), which is also called from unrelated,
    /// structurally-immune mapper methods that must keep the friendly
    /// "reader" name. Confirmed live this collides too, fixed via
    /// GetIdentityReaderCall's new readerVar parameter (default "reader",
    /// "__reader" passed only from this call site).
    /// </summary>
    [Fact]
    public void Call_IdentityInsert_ParamNamedReader_DoesNotCollideWithIdentityReadbackLocal()
    {
        var (result, compilation) = RunGenerator(
            "-- @identity\ninsert into products (product_name) values (@reader)\n",
            "db/Products/InsertByReader.sql");
        string source = GetSource(result, "InsertByReader");

        Assert.Contains("using DbDataReader __reader = ", source);
        Assert.Contains("return __reader.GetInt32(0)", source);
        AssertCompilesClean(compilation, "an -- @identity INSERT with a parameter literally named '@reader'");
    }
}
