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
    },
    ""GetOrderTotal"": {
      ""name"": ""GetOrderTotal"",
      ""params"": [
        { ""name"": ""OrderId"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""Total"", ""dbType"": ""decimal"", ""direction"": ""Out"", ""isNullable"": false, ""precision"": 12, ""scale"": 4 }
      ],
      ""results"": []
    },
    ""GetOrderSummary"": {
      ""name"": ""GetOrderSummary"",
      ""params"": [ { ""name"": ""OrderId"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false } ],
      ""results"": [
        { ""name"": ""order_number"", ""dbType"": ""int"", ""isNullable"": false },
        { ""name"": ""OrderNumber"", ""dbType"": ""int"", ""isNullable"": false }
      ]
    },
    ""ArchiveWithAffectedOutParam"": {
      ""name"": ""ArchiveWithAffectedOutParam"",
      ""params"": [
        { ""name"": ""CustomerId"", ""dbType"": ""nchar"", ""direction"": ""In"", ""isNullable"": false, ""maxLength"": 5 },
        { ""name"": ""Affected"", ""dbType"": ""int"", ""direction"": ""Out"", ""isNullable"": false }
      ],
      ""results"": []
    },
    ""GetProductsWithResultsOutParam"": {
      ""name"": ""GetProductsWithResultsOutParam"",
      ""params"": [ { ""name"": ""Results"", ""dbType"": ""int"", ""direction"": ""Out"", ""isNullable"": false } ],
      ""results"": [ { ""name"": ""ProductId"", ""dbType"": ""int"", ""isNullable"": false } ]
    },
    ""ArchiveWithCmdAndP0Params"": {
      ""name"": ""ArchiveWithCmdAndP0Params"",
      ""params"": [
        { ""name"": ""P0"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""Cmd"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false }
      ],
      ""results"": []
    },
    ""GetItemsWithReaderParam"": {
      ""name"": ""GetItemsWithReaderParam"",
      ""params"": [ { ""name"": ""Reader"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false } ],
      ""results"": [ { ""name"": ""ItemId"", ""dbType"": ""int"", ""isNullable"": false } ]
    },
    ""ArchiveWithConnAndCancellationTokenParams"": {
      ""name"": ""ArchiveWithConnAndCancellationTokenParams"",
      ""params"": [
        { ""name"": ""Conn"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""CancellationToken"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false }
      ],
      ""results"": []
    },
    ""UpdateTotalsWithOutSuffixCollision"": {
      ""name"": ""UpdateTotalsWithOutSuffixCollision"",
      ""params"": [
        { ""name"": ""CustomerId"", ""dbType"": ""nchar"", ""direction"": ""In"", ""isNullable"": false, ""maxLength"": 5 },
        { ""name"": ""Total"", ""dbType"": ""int"", ""direction"": ""Out"", ""isNullable"": false },
        { ""name"": ""TotalOut"", ""dbType"": ""int"", ""direction"": ""InOut"", ""isNullable"": false }
      ],
      ""results"": []
    },
    ""ArchiveWithKeywordNamedOutParam"": {
      ""name"": ""ArchiveWithKeywordNamedOutParam"",
      ""params"": [
        { ""name"": ""CustomerId"", ""dbType"": ""nchar"", ""direction"": ""In"", ""isNullable"": false, ""maxLength"": 5 },
        { ""name"": ""Ref"", ""dbType"": ""int"", ""direction"": ""Out"", ""isNullable"": false }
      ],
      ""results"": []
    }
  }
}";

    // AUD-R66-01: postgres-dialect twin of SchemaJson, used to confirm the
    // ExecuteNonQuery()-always-returns--1 caveat doc comment is emitted only
    // for postgres non-row-returning procs, never for sqlserver/mysql (which
    // both report a real affected-row count for the identical call shape).
    private const string PostgresSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {},
  ""procedures"": {
    ""BumpAllWidgets"": {
      ""name"": ""BumpAllWidgets"",
      ""params"": [],
      ""results"": []
    },
    ""GetProductsByCategory"": {
      ""name"": ""GetProductsByCategory"",
      ""params"": [ { ""name"": ""CategoryId"", ""dbType"": ""integer"", ""direction"": ""In"", ""isNullable"": false } ],
      ""results"": [
        { ""name"": ""ProductId"", ""dbType"": ""integer"", ""isNullable"": false }
      ]
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql, string path) =>
        Run(sql, path, SchemaJson);

    private static GeneratorDriverRunResult Run(string sql, string path, string schemaJson)
    {
        var compilation = CSharpCompilation.Create("ProcCallTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(path, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
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

        Assert.Contains("cmd.CommandType = CommandType.StoredProcedure;", src);
        Assert.Contains("cmd.CommandText = \"GetOrdersByCustomer\";", src);
        // Typed result DTO with PascalCase columns.
        Assert.Contains("public required int OrderId", src);
        Assert.Contains("DateTime? OrderDate", src);
        // IN param typed and bound.
        Assert.Contains("string customerId", src);
        // Returns a List of the result type.
        Assert.Contains("List<Result.GetOrdersByCustomer>", src);
    }

    [Fact]
    public void Call_SideEffectWithOut_EmitsOutParamAndReturnsInt()
    {
        var result = Run("-- @call ArchiveCustomer\n", "db/Customers/ArchiveCustomer.sql");
        string src = AllSources(result);

        Assert.Contains("out int archivedCount", src);
        Assert.Contains("ParameterDirection.Output", src);
        // No result set -> returns int (affected rows).
        Assert.Contains("int ArchiveCustomer(", src);
        Assert.DoesNotContain("List<Result.ArchiveCustomer>", src);
        // AUD-R66-01: unlike postgres, sqlserver's affected-row count isn't
        // unconditionally -1 (confirmed live against SQL Server 2022: a
        // plain proc without SET NOCOUNT ON genuinely reports a real
        // count -- though one using SET NOCOUNT ON, a common T-SQL
        // pattern, reports -1 too, a pre-existing ADO.NET/T-SQL nuance
        // outside this fix's scope), so the postgres-only "-1 always"
        // caveat doc comment -- which only applies where the value is
        // unconditionally meaningless -- must NOT appear here.
        Assert.DoesNotContain("PostgreSQL note: the returned row count is always", src);
    }

    /// <summary>
    /// AUD-R66-01: confirmed live against Postgres 16 (Npgsql 8.0.6) that
    /// cmd.ExecuteNonQuery() for a CommandType.StoredProcedure CALL always
    /// returns -1, even for a procedure that performs a genuine multi-row
    /// UPDATE -- Postgres's CALL protocol reports only a bare completion
    /// tag, never an affected-row count, regardless of how the procedure
    /// is authored. MySQL confirmed live to always report a real count for
    /// the identical call shape; SQL Server confirmed live to usually do
    /// so too, but not unconditionally (see the sibling test above). Since
    /// Postgres's case is a structural PostgreSQL/Npgsql limitation with
    /// no ADO.NET-level fix, the caveat is surfaced as a doc comment on
    /// the generated method itself, right at the call site.
    /// </summary>
    [Fact]
    public void Call_Postgres_SideEffectProc_EmitsAlwaysNegativeOneCaveatDocComment()
    {
        var result = Run("-- @call BumpAllWidgets\n", "db/Widgets/BumpAllWidgets.sql", PostgresSchemaJson);
        string src = AllSources(result);

        Assert.Contains("PostgreSQL note: the returned row count is always", src);
        Assert.Contains("int BumpAllWidgets(", src);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        var compilation = CSharpCompilation.Create("ProcCallPostgresSideEffectEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated postgres non-row-returning proc-call code (with caveat doc comment) failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// The caveat only applies to the non-row-returning (int-returning)
    /// branch -- a row-returning postgres proc returns List&lt;Result&gt;,
    /// a type for which the "-1 always" caveat doesn't apply and must not
    /// be emitted.
    /// </summary>
    [Fact]
    public void Call_Postgres_RowReturningProc_DoesNotEmitCaveatDocComment()
    {
        var result = Run("-- @call GetProductsByCategory\n", "db/Products/GetProductsByCategory.sql", PostgresSchemaJson);
        string src = AllSources(result);

        Assert.Contains("List<Result.GetProductsByCategory>", src);
        Assert.DoesNotContain("PostgreSQL note: the returned row count is always", src);
    }

    [Fact]
    public void Call_DecimalOutParam_EmitsPrecisionAndScale()
    {
        // Without an explicit Precision/Scale, several providers can
        // silently truncate or round a decimal OUT parameter's returned
        // value instead of matching what the procedure actually assigned.
        var result = Run("-- @call GetOrderTotal\n", "db/Orders/GetOrderTotal.sql");
        string src = AllSources(result);

        Assert.Contains("out decimal total", src);
        Assert.Contains(".Precision = 12;", src);
        Assert.Contains(".Scale = 4;", src);
    }

    [Fact]
    public void Call_UnknownProc_ReportsJNT2005()
    {
        var result = Run("-- @call NoSuchProc\n", "db/Misc/NoSuchProc.sql");
        Assert.Contains(result.Results[0].Diagnostics, d => d.Id == "JNT2005");
    }

    [Fact]
    public void Call_CombinedWithProc_ReportsJNT3003_NotSilentlyDropped()
    {
        // AUD-R8: before this guard existed, "-- @call X" + "-- @proc Y" in
        // the same file emitted ZERO diagnostics: the @call branch returns
        // early and never looks at IsProc, so @proc was silently discarded
        // with no signal to the author. Verified live: DIAGS=[] pre-fix.
        var result = Run(
            "-- @call GetOrdersByCustomer\n-- @proc SomeOtherProc\n",
            "db/Orders/Conflict.sql");

        Assert.Contains(result.Results[0].Diagnostics, d => d.Id == "JNT3003");
        Assert.Contains(result.Results[0].Diagnostics,
            d => d.GetMessage().Contains("@call") && d.GetMessage().Contains("@proc"));
        Assert.DoesNotContain(result.Results[0].GeneratedSources,
            s => s.HintName.Contains("Conflict"));
    }

    [Fact]
    public void Call_CombinedWithIdentity_ReportsJNT3003()
    {
        // Same gap, different sibling directive (§4.4 sibling-sweep): @call
        // ignores @identity exactly the same way it ignored @proc.
        var result = Run(
            "-- @call GetOrdersByCustomer\n-- @identity\n",
            "db/Orders/ConflictIdentity.sql");

        Assert.Contains(result.Results[0].Diagnostics, d => d.Id == "JNT3003");
        Assert.DoesNotContain(result.Results[0].GeneratedSources,
            s => s.HintName.Contains("Conflict"));
    }

    [Fact]
    public void Call_CombinedWithFirst_ReportsJNT3003()
    {
        var result = Run(
            "-- @call GetOrdersByCustomer\n-- @first\n",
            "db/Orders/ConflictFirst.sql");

        Assert.Contains(result.Results[0].Diagnostics, d => d.Id == "JNT3003");
        Assert.DoesNotContain(result.Results[0].GeneratedSources,
            s => s.HintName.Contains("Conflict"));
    }

    [Theory]
    [InlineData("-- @stream\n")]
    [InlineData("-- @each SomeParam\n")]
    [InlineData("-- @type foo int\n")]
    [InlineData("-- @params Id:int\n")]
    [InlineData("-- @result void\n")]
    public void Call_CombinedWithAnyOtherDirective_ReportsJNT3003(string secondDirectiveLine)
    {
        // Sibling-sweep (§4.4) of the row: the same @call-short-circuit gap
        // applies uniformly to every remaining directive, not just
        // @proc/@identity/@first covered by the dedicated tests above.
        var result = Run(
            "-- @call GetOrdersByCustomer\n" + secondDirectiveLine,
            "db/Orders/ConflictOther.sql");

        Assert.Contains(result.Results[0].Diagnostics, d => d.Id == "JNT3003");
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
        // AUD-R68-01: the bound DbParameter local is "__p0", not "p0" (see
        // ProcCallTests's collision regression tests below).
        Assert.Contains("\n                total = __p0.Value is null", source);
        Assert.DoesNotContain("\n                    total = __p0.Value is null", source);

        // Async overloads declare a local to fold into the tuple return (declareLocals: true).
        // AUD-R68-01: "__totalOut", not "totalOut" -- see EmitProcOutReadback.
        Assert.Contains("\n                int __totalOut = __p0.Value is null", source);
        Assert.DoesNotContain("\n                    int __totalOut = __p0.Value is null", source);

        // Both readback forms occur once per overload (instance/static x sync/async).
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, @"total = __p0\.Value is null").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, @"int __totalOut = __p0\.Value is null").Count);
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

    /// <summary>
    /// AUD-R64-01 (fix 2): two distinct, individually-legal result columns
    /// ("order_number"/"OrderNumber") that fold to the same PascalCased
    /// property name previously made EmitProcCall's Result DTO
    /// (CodeEmitter.Part10.cs) emit a duplicate member -- the exact sibling
    /// of the canonical row-POCO collision this same round's fix guards for
    /// tables (JauntyQGenerator.Part4.cs), here for a stored procedure's own
    /// result-set columns instead. Now reports JNT2011 and the file emits no
    /// source at all (mirroring JNT3009's per-query "skip this one file"
    /// policy, since -- @call is a per-file directive with its own
    /// independent Result DTO, unlike the table-level row POCO).
    /// </summary>
    [Fact]
    public void Call_ResultColumnsFoldToSamePascalCase_ReportsJNT2011_NoSourceEmitted()
    {
        var result = Run("-- @call GetOrderSummary\n", "db/Orders/GetOrderSummary.sql");

        var diag = Assert.Single(result.Results[0].Diagnostics, d => d.Id == "JNT2011");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("'OrderNumber'", diag.GetMessage());
        Assert.DoesNotContain(result.Results[0].GeneratedSources, s => s.HintName.Contains("GetOrderSummary"));
    }

    /// <summary>
    /// AUD-R67-01: a non-row-returning proc's async overload declares a
    /// tuple return whose first element is the JauntyQ-introduced bookkeeping
    /// name "affected" -- unchecked against the OTHER tuple elements, which
    /// ARE schema-derived (each OUT/INOUT param's own camelCased name).
    /// Confirmed live (before the fix) that an OUT param literally named
    /// "Affected" produces a tuple type with two identically-named elements
    /// -- a real CS8127 "Tuple element names must be unique" compiler error,
    /// with zero JauntyQ diagnostic of any kind raised first. The sync
    /// overload was unaffected (it already used the collision-safe
    /// "__affected" local, not "affected"). Fixed by falling back to
    /// "__affected" for JauntyQ's own tuple element specifically when a real
    /// OUT/INOUT param would otherwise collide with it, while keeping the
    /// friendly "affected" name in the (overwhelmingly common) non-colliding
    /// case -- see AdjustStockAsync's own generated signature elsewhere in
    /// this file, unaffected by this fix.
    /// </summary>
    [Fact]
    public void Call_AsyncOutParamNamedAffected_DoesNotCollideWithBookkeepingTupleElement()
    {
        var result = Run("-- @call ArchiveWithAffectedOutParam\n", "db/Customers/ArchiveWithAffectedOutParam.sql");
        string source = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Customers.ArchiveWithAffectedOutParam.g.cs").SourceText.ToString();

        // The colliding OUT param keeps its own friendly name; JauntyQ's own
        // element backs off to the collision-safe fallback instead.
        Assert.Contains("Task<(int __affected, int affected)> ArchiveWithAffectedOutParamAsync(", source);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        var compilation = CSharpCompilation.Create("ProcCallAffectedCollisionEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated proc-call code for an OUT param literally named 'Affected' failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// AUD-R67-01: a row-returning proc's shared "var results = ..." local
    /// (used by BOTH the sync and async overloads) was not checked against
    /// real schema parameter names either. Confirmed live (before the fix)
    /// that an OUT param literally named "Results" broke BOTH overloads, via
    /// two different mechanisms: async got the same CS8127 tuple-name
    /// collision as the "Affected" case above, while sync got a genuine
    /// local/parameter redeclaration -- CS0136 ("a local ... named 'results'
    /// cannot be declared in this scope") cascading into CS0029 ("cannot
    /// convert 'int' to 'List&lt;...&gt;'") at the OUT-param readback line,
    /// since the pre-fix code reused the same "results" name for both the
    /// out parameter and the row-list local. Fixed by unconditionally
    /// renaming the internal row-list local to "__results" (a schema name
    /// can never camelCase to a name containing an underscore, so this can
    /// never collide with a real parameter -- a pure-internal rename with
    /// zero effect on the tuple TYPE's own, separately-computed, friendly
    /// public element name) plus the same tuple-element-name fallback used
    /// for the "Affected" case above.
    /// </summary>
    [Fact]
    public void Call_OutParamNamedResults_DoesNotCollideWithRowListLocal_SyncAndAsync()
    {
        var result = Run("-- @call GetProductsWithResultsOutParam\n", "db/Products/GetProductsWithResultsOutParam.sql");
        string source = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Products.GetProductsWithResultsOutParam.g.cs").SourceText.ToString();

        Assert.Contains("Task<(List<Result.GetProductsWithResultsOutParam> __results, int results)> GetProductsWithResultsOutParamAsync(", source);
        Assert.Contains("var __results = new List<Result.GetProductsWithResultsOutParam>();", source);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        var compilation = CSharpCompilation.Create("ProcCallResultsCollisionEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated proc-call code for an OUT param literally named 'Results' failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// AUD-R68-01: the bound-parameter loop var ("p0"/"p1"/...) and the
    /// DbCommand local ("cmd") are JauntyQ-introduced pure-internal
    /// bookkeeping names, never part of the public API surface, that were
    /// never checked against real schema parameter names either -- same
    /// defect family as AUD-R67-01's "results"/"affected". Confirmed live
    /// (before the fix) that a schema param literally named "P0" collides
    /// (CS0136) with the bare "p0" local for the first bound parameter, and
    /// a schema param literally named "Cmd" collides (CS0136) with the bare
    /// "cmd" DbCommand local, in all four overloads. Fixed by unconditionally
    /// prefixing both with a double underscore ("__p0", "__cmd") -- always
    /// safe, since (per DialectMapper.ToPascalCase's own invariant) no
    /// schema-derived name can ever camelCase to a string containing an
    /// underscore.
    /// </summary>
    [Fact]
    public void Call_ParamsNamedP0AndCmd_DoNotCollideWithBookkeepingLocals()
    {
        var result = Run("-- @call ArchiveWithCmdAndP0Params\n", "db/Customers/ArchiveWithCmdAndP0Params.sql");
        string source = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Customers.ArchiveWithCmdAndP0Params.g.cs").SourceText.ToString();

        Assert.Contains("using DbCommand __cmd = ", source);
        Assert.Contains("DbParameter __p0 = __cmd.CreateParameter();", source);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        var compilation = CSharpCompilation.Create("ProcCallP0CmdCollisionEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated proc-call code for params literally named 'P0'/'Cmd' failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// AUD-R68-01: same defect family, for the row-returning overload's
    /// DbDataReader local. Confirmed live (before the fix) that a schema
    /// param literally named "Reader" on a row-returning proc collides
    /// (CS0136) with the bare "reader" local. Fixed the same way: an
    /// unconditional "__reader" rename.
    /// </summary>
    [Fact]
    public void Call_ParamNamedReader_DoesNotCollideWithDataReaderLocal()
    {
        var result = Run("-- @call GetItemsWithReaderParam\n", "db/Items/GetItemsWithReaderParam.sql");
        string source = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Items.GetItemsWithReaderParam.g.cs").SourceText.ToString();

        Assert.Contains("DbDataReader __reader = ", source);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        var compilation = CSharpCompilation.Create("ProcCallReaderCollisionEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated proc-call code for a param literally named 'Reader' failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// AUD-R68-01: unlike the pure-internal locals above, the static
    /// overload's "conn" parameter and the async overload's
    /// "cancellationToken" parameter DO appear in the method's public
    /// signature, so they use the same conditional collision-check-and-
    /// fallback pattern AUD-R67-01 established for the tuple's first
    /// element name, rather than an unconditional rename. Confirmed live
    /// (before the fix) that a schema param literally named "Conn"
    /// (static overload) or "CancellationToken" (async overload) produces a
    /// duplicate-parameter compile failure (CS0100/CS0229). Fixed by
    /// escalating to "__conn"/"__cancellationToken" only in the actual
    /// collision case, keeping the conventional name otherwise (see e.g.
    /// AdjustStock's own generated static/async signatures elsewhere in this
    /// file, unaffected by this fix).
    /// </summary>
    [Fact]
    public void Call_ParamsNamedConnAndCancellationToken_DoNotCollideWithFormalParameters()
    {
        var result = Run("-- @call ArchiveWithConnAndCancellationTokenParams\n", "db/Customers/ArchiveWithConnAndCancellationTokenParams.sql");
        string source = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Customers.ArchiveWithConnAndCancellationTokenParams.g.cs").SourceText.ToString();

        Assert.Contains("DbConnection __conn", source);
        Assert.Contains("CancellationToken __cancellationToken = default", source);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        var compilation = CSharpCompilation.Create("ProcCallConnCancellationTokenCollisionEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated proc-call code for params literally named 'Conn'/'CancellationToken' failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// AUD-R68-01: the async overload's OUT-param readback local (folded
    /// into the tuple return) is named "{csName}Out" -- also never checked
    /// against real schema parameter names. Confirmed live (before the fix)
    /// that an OUT param named "Total" (producing readback local "totalOut")
    /// collides (CS0136) with a sibling INOUT param literally named
    /// "TotalOut" (which itself stays a real formal parameter named
    /// "totalOut" on the async overload, since INOUT parameters are kept as
    /// plain input values on async, never dropped like OUT). Fixed by
    /// unconditionally prefixing the readback local ("__totalOut") -- a
    /// pure-internal rename with zero effect on the sibling INOUT param's
    /// own, separately-computed, public parameter name.
    /// </summary>
    [Fact]
    public void Call_OutParamNamedTotal_DoesNotCollideWithSiblingParamNamedTotalOut()
    {
        var result = Run("-- @call UpdateTotalsWithOutSuffixCollision\n", "db/Customers/UpdateTotalsWithOutSuffixCollision.sql");
        string source = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Customers.UpdateTotalsWithOutSuffixCollision.g.cs").SourceText.ToString();

        Assert.Contains("int __totalOut = ", source);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        var compilation = CSharpCompilation.Create("ProcCallOutSuffixCollisionEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated proc-call code for an OUT param named 'Total' alongside a sibling INOUT param named 'TotalOut' failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// AUD-R68-01 (amendment, caught by independent post-fix review): the
    /// async readback local's name is built from "csName", which is already
    /// IdentifierGuard.Escape()-d -- so an OUT param whose camelCased name is
    /// itself a C# keyword (e.g. "Ref" -&gt; "ref" -&gt; "@ref") arrives as
    /// "@ref". "@" is only legal at position 0 of an identifier, so naively
    /// prefixing it ("__" + "@ref" + "Out") emitted "__@refOut", a syntax
    /// error -- confirmed live via a real Roslyn compile (CS1002, CS0841,
    /// CS0118, CS8185) before this specific amendment. Fixed by stripping the
    /// leading '@' before building the readback local's name; the sync
    /// overload's direct out-param assignment (declareLocals: false, which
    /// legitimately needs the '@' escape) is unaffected.
    /// </summary>
    [Fact]
    public void Call_KeywordNamedOutParam_ReadbackLocalDoesNotEmitLeadingAtSign()
    {
        var result = Run("-- @call ArchiveWithKeywordNamedOutParam\n", "db/Customers/ArchiveWithKeywordNamedOutParam.sql");
        string source = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Customers.ArchiveWithKeywordNamedOutParam.g.cs").SourceText.ToString();

        Assert.Contains("int __refOut = ", source);
        Assert.DoesNotContain("__@refOut", source);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        var compilation = CSharpCompilation.Create("ProcCallKeywordOutParamReadbackEmittedCode",
            allTrees,
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated proc-call code for a keyword-named ('Ref') OUT param failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }
}
