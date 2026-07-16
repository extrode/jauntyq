using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Generator coverage for -- @call: binding to a stored procedure captured in
/// the schema snapshot. Verifies the emitted CommandType.StoredProcedure call,
/// typed IN/OUT parameters, ordinal result materialization, and the JNT2005
/// "procedure not found" diagnostic. The generated code is also parsed to prove
/// it is valid C#.
/// </summary>
public class ProcCallTests
{
    // Snapshot with a row-returning proc (IN param) and a side-effect proc
    // (IN + OUT params, no result set).
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {},
  ""procedures"": {
    ""GetOrdersByCustomer"": {
      ""name"": ""GetOrdersByCustomer"",
      ""params"": [ { ""name"": ""CustomerId"", ""dbType"": ""nchar"", ""direction"": ""In"", ""isNullable"": false, ""maxLength"": 5 } ],
      ""results"": [
        { ""name"": ""OrderId"", ""dbType"": ""int"", ""isNullable"": false },
        { ""name"": ""OrderDate"", ""dbType"": ""datetime"", ""isNullable"": true }
      ]
    },
    ""ArchiveCustomer"": {
      ""name"": ""ArchiveCustomer"",
      ""params"": [
        { ""name"": ""CustomerId"", ""dbType"": ""nchar"", ""direction"": ""In"", ""isNullable"": false, ""maxLength"": 5 },
        { ""name"": ""ArchivedCount"", ""dbType"": ""int"", ""direction"": ""Out"", ""isNullable"": false }
      ],
      ""results"": []
    },
    ""AdjustStock"": {
      ""name"": ""AdjustStock"",
      ""params"": [
        { ""name"": ""ProductId"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""Quantity"", ""dbType"": ""int"", ""direction"": ""InOut"", ""isNullable"": false }
      ],
      ""results"": []
    },
    ""GetProductsAndCount"": {
      ""name"": ""GetProductsAndCount"",
      ""params"": [ { ""name"": ""Total"", ""dbType"": ""int"", ""direction"": ""Out"", ""isNullable"": false } ],
      ""results"": [ { ""name"": ""ProductId"", ""dbType"": ""int"", ""isNullable"": false } ]
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql, string path)
    {
        var compilation = CSharpCompilation.Create("ProcCallTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(path, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string AllSources(GeneratorDriverRunResult result) =>
        string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

    [Fact]
    public void Call_RowReturning_EmitsStoredProcedureCallAndResultType()
    {
        var result = Run("-- @call GetOrdersByCustomer\n", "db/Orders/GetOrdersByCustomer.sql");
        string src = AllSources(result);

        Assert.Contains("cmd.CommandType = System.Data.CommandType.StoredProcedure;", src);
        Assert.Contains("cmd.CommandText = \"GetOrdersByCustomer\";", src);
        // Typed result DTO with PascalCase columns.
        Assert.Contains("public required int OrderId", src);
        Assert.Contains("System.DateTime? OrderDate", src);
        // IN param typed and bound.
        Assert.Contains("string customerId", src);
        // Returns a List of the result type.
        Assert.Contains("System.Collections.Generic.List<Result.GetOrdersByCustomer>", src);
    }

    [Fact]
    public void Call_SideEffectWithOut_EmitsOutParamAndReturnsInt()
    {
        var result = Run("-- @call ArchiveCustomer\n", "db/Customers/ArchiveCustomer.sql");
        string src = AllSources(result);

        Assert.Contains("out int archivedCount", src);
        Assert.Contains("System.Data.ParameterDirection.Output", src);
        // No result set -> returns int (affected rows).
        Assert.Contains("int ArchiveCustomer(", src);
        Assert.DoesNotContain("List<Result.ArchiveCustomer>", src);
    }

    [Fact]
    public void Call_UnknownProc_ReportsJNT2005()
    {
        var result = Run("-- @call NoSuchProc\n", "db/Misc/NoSuchProc.sql");
        Assert.Contains(result.Results[0].Diagnostics, d => d.Id == "JNT2005");
    }

    [Fact]
    public void GeneratedProcCallCode_ParsesClean()
    {
        var result = Run("-- @call GetOrdersByCustomer\n", "db/Orders/GetOrdersByCustomer.sql");
        foreach (var gen in result.Results[0].GeneratedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(gen.SourceText.ToString());
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                $"proc-call code broke in {gen.HintName}: {string.Join("; ", errors.Select(e => e.GetMessage()))}");
        }
    }

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

    /// <summary>
    /// Real-compile verification (not just ParseText, which stays silent on
    /// ref/out-on-async since that is a binding-time error, not a syntax
    /// error) that the async overloads of a proc with OUT/INOUT params
    /// actually compile. Before the fix, EmitProcCallBody emitted `out`/`ref`
    /// on the async overloads unconditionally, which is illegal (CS1988) and
    /// was invisible to the old parse-only self-check.
    /// </summary>
    [Fact]
    public void GeneratedProcCallCode_WithOutParam_CompilesAgainstRealAdo()
    {
        var result = Run("-- @call ArchiveCustomer\n", "db/Customers/ArchiveCustomer.sql");
        string source = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Customers.ArchiveCustomer.g.cs").SourceText.ToString();

        // Sync keeps the real `out` parameter; async drops it entirely and
        // returns it via a tuple instead (no line declares both).
        Assert.Contains("out int archivedCount", source);
        Assert.Contains("Task<(int affected, int archivedCount)> ArchiveCustomerAsync(", source);
        Assert.DoesNotContain(source.Split('\n'), line => line.Contains("async") && line.Contains("out int archivedCount"));

        // Compile alongside every other file the generator emitted for this
        // run (Customers.Core.g.cs supplies the _conn/_db fields this partial
        // class relies on) so the check is a real, representative compile.
        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();

        var compilation = CSharpCompilation.Create("ProcCallOutEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated proc-call code with an OUT param failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void GeneratedProcCallCode_WithInOutParam_CompilesAgainstRealAdo()
    {
        var result = Run("-- @call AdjustStock\n", "db/Products/AdjustStock.sql");
        string source = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Products.AdjustStock.g.cs").SourceText.ToString();

        // Sync keeps `ref`; async takes it as a plain value and returns the
        // updated value via a tuple instead.
        Assert.Contains("ref int quantity", source);
        Assert.Contains("Task<(int affected, int quantity)> AdjustStockAsync(", source);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();

        var compilation = CSharpCompilation.Create("ProcCallInOutEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated proc-call code with an INOUT param failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// A row-returning proc with an OUT param must read the OUT param back
    /// AFTER the reader's using block closes, not inside it. Confirmed live
    /// against Microsoft.Data.SqlClient (Testcontainers SQL Server): reading
    /// cmd.Parameters[i].Value while a DbDataReader from the same command is
    /// still open -- even after every row has been read via reader.Read()
    /// returning false -- returns DBNull, not the procedure's real OUT value.
    /// Only once the reader is disposed does the provider populate it. Before
    /// the fix, EmitProcCallBody read it back inside the using block (at one
    /// extra indent level, 20 spaces); the fix moves it after the block's
    /// closing brace (back at the method body's own 16-space indent), so the
    /// indentation of the readback line is a precise, cheap proxy for "did
    /// this happen before or after the reader closed" without having to
    /// re-parse the emitted source's control flow.
    /// </summary>
    [Fact]
    public void Call_RowReturningWithOutParam_ReadsBackOutParamAfterReaderCloses()
    {
        var result = Run("-- @call GetProductsAndCount\n", "db/Products/GetProductsAndCount.sql");
        string source = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Products.GetProductsAndCount.g.cs").SourceText.ToString();

        // Sync overloads assign the out parameter directly (declareLocals: false).
        Assert.Contains("\n                total = p0.Value is null", source);
        Assert.DoesNotContain("\n                    total = p0.Value is null", source);

        // Async overloads declare a local to fold into the tuple return (declareLocals: true).
        Assert.Contains("\n                int totalOut = p0.Value is null", source);
        Assert.DoesNotContain("\n                    int totalOut = p0.Value is null", source);

        // Both readback forms occur once per overload (instance/static x sync/async).
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, @"total = p0\.Value is null").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, @"int totalOut = p0\.Value is null").Count);
    }

    /// <summary>
    /// Same shape as <see cref="GeneratedProcCallCode_WithOutParam_CompilesAgainstRealAdo"/>,
    /// for a proc that both returns rows AND has an OUT param -- the specific
    /// combination the readback-ordering fix applies to.
    /// </summary>
    [Fact]
    public void GeneratedProcCallCode_RowReturningWithOutParam_CompilesAgainstRealAdo()
    {
        var result = Run("-- @call GetProductsAndCount\n", "db/Products/GetProductsAndCount.sql");

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();

        var compilation = CSharpCompilation.Create("ProcCallRowReturningWithOutEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated row-returning proc-call code with an OUT param failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }
}
