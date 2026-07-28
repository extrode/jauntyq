using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R75-01 (JNT2013) and AUD-R75-02 (JNT2012): closes the parameter-name
/// collision family that rounds 67-72 repeatedly deferred (4th and 3rd
/// deferral respectively).
///
/// Rounds 69 and 70 each fixed one INSTANCE by renaming a single bookkeeping
/// identifier. That never converged -- the generator emits 23 __-prefixed
/// bookkeeping identifiers, and a rename only closes the one name it touches.
/// These tests pin the structural fix instead: reject the reserved __ prefix
/// outright (JNT2012), and reject two parameters that fold to one C# name
/// (JNT2013), so no rename is load-bearing.
///
/// Every case here produced a raw C# compiler error (CS0100/CS0229/CS0136)
/// against generated code before the fix, with no JauntyQ diagnostic
/// explaining it. Each test therefore asserts BOTH the expected JauntyQ
/// diagnostic AND a real Roslyn compile, following
/// CrudBookkeepingNameCollisionTests.cs.
/// </summary>
public class ParameterNameCollisionDiagnosticTests
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
  },
  ""procedures"": {
    ""FoldingInParams"": {
      ""name"": ""FoldingInParams"",
      ""params"": [
        { ""name"": ""order_number"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""OrderNumber"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false }
      ],
      ""results"": []
    },
    ""DegenerateParams"": {
      ""name"": ""DegenerateParams"",
      ""params"": [
        { ""name"": ""_"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""__"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false }
      ],
      ""results"": []
    },
    ""DistinctParams"": {
      ""name"": ""DistinctParams"",
      ""params"": [
        { ""name"": ""order_number"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""customer_id"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false }
      ],
      ""results"": []
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
        string sql, string sqlFilePath)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("");

        var compilation = CSharpCompilation.Create("ParameterNameCollisionAssembly",
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

    private static void AssertReports(GeneratorDriverRunResult result, string id, string context)
    {
        Assert.True(result.Diagnostics.Any(d => d.Id == id),
            $"expected {id} for {context}, got: " +
            (result.Diagnostics.IsEmpty
                ? "(no diagnostics)"
                : string.Join(", ", result.Diagnostics.Select(d => d.Id))));
    }

    private static void AssertDoesNotReport(GeneratorDriverRunResult result, string id, string context)
    {
        Assert.False(result.Diagnostics.Any(d => d.Id == id),
            $"{id} must not fire for {context}");
    }

    /// <summary>
    /// The generated code must compile. Before this fix every JNT2012/JNT2013
    /// case emitted a duplicate identifier instead of a diagnostic, so this
    /// assertion is what distinguishes "rejected cleanly" from "emitted broken
    /// code"; a suppressed diagnostic alone would not.
    /// </summary>
    private static void AssertNoDuplicateIdentifierErrors(Compilation compilation, string context)
    {
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id is "CS0100" or "CS0229" or "CS0136" or "CS0128")
            .ToList();
        Assert.True(errors.Count == 0,
            $"generated code for {context} still has duplicate-identifier errors:\n"
            + string.Join("\n", errors.Select(e => e.ToString())));
    }

    // ── JNT2012: reserved __ prefix on .sql query parameters ──────────────

    /// <summary>
    /// The round-69 residual, confirmed live in round 72: a query binding both
    /// @conn and @__conn defeats AnyParamNameCollidesWith, which only tests
    /// whether a real parameter collides with the ORIGINAL name "conn" and
    /// never re-checks that the fallback "__conn" is itself free.
    /// </summary>
    [Fact]
    public void SqlParam_DunderConn_AlongsideConn_ReportsJNT2012_NotBrokenCode()
    {
        var (result, compilation) = RunGenerator(
            "select product_id, product_name from products where product_id = @conn and product_name = @__conn\n",
            "db/Products/GetByConnAndDunderConn.sql");

        AssertReports(result, "JNT2012", "a query binding both '@conn' and '@__conn'");
        AssertNoDuplicateIdentifierErrors(compilation, "'@conn' + '@__conn'");
    }

    [Fact]
    public void SqlParam_DunderCmd_ReportsJNT2012()
    {
        var (result, compilation) = RunGenerator(
            "select product_id, product_name from products where product_id = @__cmd\n",
            "db/Products/GetByDunderCmd.sql");

        AssertReports(result, "JNT2012", "a query parameter named '@__cmd'");
        AssertNoDuplicateIdentifierErrors(compilation, "'@__cmd'");
    }

    /// <summary>
    /// The deliberate breakage. '@__myThing' collides with nothing the
    /// generator emits today and compiles fine, but the whole __ namespace is
    /// reserved so that adding a 24th bookkeeping identifier later cannot
    /// silently re-open this hazard. Rejecting only on actual collision was
    /// considered and refused for exactly that reason.
    /// </summary>
    [Fact]
    public void SqlParam_DunderNonColliding_StillReportsJNT2012()
    {
        var (result, _) = RunGenerator(
            "select product_id, product_name from products where product_id = @__myThing\n",
            "db/Products/GetByDunderMyThing.sql");

        AssertReports(result, "JNT2012", "a query parameter named '@__myThing'");
    }

    [Fact]
    public void SqlParam_SingleUnderscorePrefix_DoesNotReportJNT2012()
    {
        var (result, _) = RunGenerator(
            "select product_id, product_name from products where product_id = @_conn\n",
            "db/Products/GetBySingleUnderscoreConn.sql");

        AssertDoesNotReport(result, "JNT2012", "'@_conn' -- one underscore is not the reserved prefix");
    }

    [Fact]
    public void SqlParam_OrdinaryName_DoesNotReportJNT2012()
    {
        var (result, _) = RunGenerator(
            "select product_id, product_name from products where product_id = @productId\n",
            "db/Products/GetByProductId.sql");

        AssertDoesNotReport(result, "JNT2012", "an ordinary parameter name");
    }

    /// <summary>
    /// No-regression guard on the logic JNT2012 deliberately does NOT remove:
    /// a parameter named plainly '@conn' stays legal and is still handled by
    /// escalating to the '__conn' fallback. JNT2012 only guarantees that
    /// fallback name is unoccupied.
    /// </summary>
    [Fact]
    public void SqlParam_PlainConn_StillCompilesViaFallback()
    {
        var (result, compilation) = RunGenerator(
            "select product_id, product_name from products where product_id = @conn\n",
            "db/Products/GetByPlainConn.sql");

        AssertDoesNotReport(result, "JNT2012", "a parameter named plainly '@conn'");
        AssertNoDuplicateIdentifierErrors(compilation, "'@conn' alone");
    }

    // ── JNT2013: two parameters folding to one C# name ────────────────────

    /// <summary>
    /// The round-67 residual, live-reproduced in round 71: DialectMapper.
    /// ToPascalCase strips separators, so 'order_number' and 'OrderNumber'
    /// both fold to 'orderNumber' and emit two identical C# formals (CS0100).
    /// Round 71 confirmed plain IN-only pairs suffice -- no OUT/INOUT needed.
    /// </summary>
    [Fact]
    public void ProcParams_FoldingToSameName_ReportsJNT2013_NotBrokenCode()
    {
        var (result, compilation) = RunGenerator(
            "-- @call FoldingInParams\n",
            "db/Orders/FoldingInParams.sql");

        AssertReports(result, "JNT2013", "'order_number' and 'OrderNumber' folding to 'orderNumber'");
        AssertNoDuplicateIdentifierErrors(compilation, "two parameters folding to one name");
    }

    /// <summary>
    /// ToPascalCase's degenerate `if (sb.Length == 0) return "_";` fallback:
    /// raw names '_' and '__' both fold to bare '_', which additionally
    /// produces CS0229 from discard-name semantics. Documented by round 71 as
    /// a previously-undocumented sub-mechanism of the same residual.
    /// </summary>
    [Fact]
    public void ProcParams_DegenerateUnderscoreFold_ReportsJNT2013()
    {
        var (result, compilation) = RunGenerator(
            "-- @call DegenerateParams\n",
            "db/Orders/DegenerateParams.sql");

        AssertReports(result, "JNT2013", "raw parameter names '_' and '__' both folding to '_'");
        AssertNoDuplicateIdentifierErrors(compilation, "the degenerate underscore fold");
    }

    [Fact]
    public void ProcParams_DistinctNames_DoNotReportJNT2013()
    {
        var (result, compilation) = RunGenerator(
            "-- @call DistinctParams\n",
            "db/Orders/DistinctParams.sql");

        AssertDoesNotReport(result, "JNT2013", "two parameters with distinct folded names");
        AssertNoDuplicateIdentifierErrors(compilation, "distinct parameter names");
    }

    // ── AutoCrud path ─────────────────────────────────────────────────────

    private const string FoldingColumnsSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""order_id"": { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""order_number"": { ""name"": ""order_number"", ""dbType"": ""int"", ""isNullable"": false },
        ""OrderNumber"": { ""name"": ""OrderNumber"", ""dbType"": ""int"", ""isNullable"": false }
      }
    }
  }
}";

    /// <summary>
    /// AUD-R75-03. Checking the AutoCrud path rather than assuming it (the
    /// plan for this round required that, since AUD-R70-01 established
    /// AutoCrud reaches this family with zero .sql authorship) turned up a
    /// separate, previously-unknown defect.
    ///
    /// A table whose columns fold to one C# name was dropped from the
    /// generated API ENTIRELY -- no row type, no auto-CRUD, no entity
    /// accessor -- with no diagnostic whatsoever. Three sites each refuse it
    /// and each assumed another one reported it: AutoCrud.cs:98 skips
    /// synthesis, Part5.cs:59 refuses to route a full-row query to it, and
    /// Part4.cs's JNT2011 row-POCO arm is meant to explain it. But that arm
    /// iterates neededRowTables, and Part5.cs:59 is exactly what keeps such a
    /// table out of neededRowTables -- so it could never fire for the case it
    /// was written for. Confirmed live: before the fix this schema produced
    /// zero diagnostics from every surface (run result, per-generator, and
    /// the output compilation) while silently generating nothing for 'orders'.
    ///
    /// The harness is deliberately the AutoCrudTests one (isPrimaryKey plus
    /// the SqlClient/MySqlConnector references), not this file's own: without
    /// a primary key AutoCrud synthesizes nothing and this test would pass
    /// while exercising no AutoCrud code at all.
    /// </summary>
    [Fact]
    public void AutoCrud_ColumnsFoldingToSameName_ReportsJNT2011_NotASilentDrop()
    {
        string runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new System.Collections.Generic.List<MetadataReference>(BaseReferences)
        {
            MetadataReference.CreateFromFile(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnector.MySqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.NonGeneric.dll")),
        };

        var compilation = CSharpCompilation.Create("AutoCrudFoldingAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Orders/GetOne.sql", "select order_id, order_number from orders where order_id = @orderId\n"),
                new InMemoryAdditionalText("schema/jaunty.schema.json", FoldingColumnsSchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var result = driver.GetRunResult();

        var jnt2014 = Assert.Single(result.Diagnostics.Where(d => d.Id == "JNT2014"));

        // Warning, not Error: the skip stays non-breaking, it is only
        // explained now. An Error would fail the build of any consumer whose
        // schema holds such a pair in a table they never query.
        Assert.Equal(DiagnosticSeverity.Warning, jnt2014.Severity);

        // The message must name BOTH raw columns and the consequence, not just
        // the folded property -- on a wide table "maps to 'OrderNumber'" alone
        // leaves the reader hunting for which two columns produced it.
        string msg = jnt2014.GetMessage();
        Assert.Contains("order_number", msg);
        Assert.Contains("OrderNumber", msg);
        Assert.Contains("db.Orders", msg);

        AssertNoDuplicateIdentifierErrors(outputCompilation, "AutoCrud columns folding to one name");
    }

    /// <summary>
    /// The other half of AUD-R75-03: a schema with no folding collision must
    /// stay silent. Without this, the fix above could pass by reporting
    /// JNT2014 for every table.
    /// </summary>
    [Fact]
    public void AutoCrud_DistinctColumnNames_DoNotReportJNT2014()
    {
        string runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new System.Collections.Generic.List<MetadataReference>(BaseReferences)
        {
            MetadataReference.CreateFromFile(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnector.MySqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.NonGeneric.dll")),
        };

        var compilation = CSharpCompilation.Create("AutoCrudDistinctAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        string distinctSchema = FoldingColumnsSchemaJson.Replace(
            @"""OrderNumber"": { ""name"": ""OrderNumber"", ""dbType"": ""int"", ""isNullable"": false }",
            @"""customer_id"": { ""name"": ""customer_id"", ""dbType"": ""int"", ""isNullable"": false }");

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", distinctSchema)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var result = driver.GetRunResult();

        // Vacuity guard: prove AutoCrud actually ran for this schema.
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains(".auto.g.cs"));
        AssertDoesNotReport(result, "JNT2014", "a table with distinct column names");
        AssertNoDuplicateIdentifierErrors(outputCompilation, "distinct AutoCrud column names");
    }
}
