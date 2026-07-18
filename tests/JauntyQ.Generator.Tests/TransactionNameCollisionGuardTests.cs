using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// §2.9 dialect-conditional fan-out mandate: the "transaction-name-collision
/// guard" is implemented THREE separate times, once per emission path that
/// injects an optional trailing <c>System.Data.Common.DbTransaction? transaction
/// = null</c> parameter on a static overload:
/// <list type="bullet">
/// <item>CodeEmitter.Part3.cs <c>HasStaticTransactionParam</c> -- named-query
/// CRUD methods (@insert/@update/@delete .sql files); checks the raw SQL
/// parameter <c>EmittedParam.Name</c> (never PascalCase/camelCase-transformed
/// -- <c>EmittedParam.CSharpName</c> is just <c>IdentifierGuard.Escape(Name)</c>)
/// via <c>OrdinalIgnoreCase</c>.</item>
/// <item>CodeEmitter.Part7.cs <c>TxForwardable</c> -- synthetic AutoCrud POCO
/// overloads; checks the raw <c>ColumnSchema.Name</c> (same untransformed
/// domain as above) via <c>OrdinalIgnoreCase</c>; the code comment explicitly
/// says it must "mirror" HasStaticTransactionParam so the POCO overload's
/// forwarded call always matches the underlying scalar method's real
/// signature.</item>
/// <item>CodeEmitter.Part11.cs <c>EmitProcCallBody</c> -- stored-procedure
/// calls; proc parameter names ARE transformed
/// (<c>ToCamelCase(DialectMapper.ToPascalCase(p.Name))</c>) before becoming
/// C# identifiers, and the guard checks the ALREADY-RENDERED parameter
/// string via case-sensitive <c>Ordinal</c> comparison instead.</item>
/// </list>
/// These three algorithms look inconsistent (two OrdinalIgnoreCase-on-raw-name,
/// one Ordinal-on-rendered-name) but are each individually correct FOR THEIR
/// OWN domain's name-transform pipeline:
/// <list type="bullet">
/// <item>Named-query/AutoCrud names pass through verbatim (no case folding at
/// all) into the emitted C# identifier, so OrdinalIgnoreCase-on-raw-name is a
/// safe (if occasionally over-cautious for e.g. "@Transaction" vs a lowercase
/// injected "transaction" -- which in fact DO collide once escaped, since C#
/// parameter names ARE case-sensitive but the raw name IS the final name here)
/// proxy for "will the final identifiers be equal".</item>
/// <item>Proc parameter names go through <c>ToPascalCase</c> (uppercases only
/// each word's first letter, leaves the rest of the casing untouched) then
/// <c>ToCamelCase</c> (lowercases only index 0). A proc parameter literally
/// named "TRANSACTION" (all caps) therefore becomes the C# identifier
/// "tRANSACTION", NOT "transaction" -- genuinely a different, non-colliding
/// identifier -- so Ordinal-on-the-final-rendered-name is not a bug, it is
/// the algorithm that actually tracks reality; a case-insensitive check here
/// would have been the wrong (over-suppressing) choice.</item>
/// </list>
/// No test exercised any of the three guards against a real "column/parameter
/// literally named transaction" pathological input before this round (§2.9,
/// M-10 secondary mandate) despite each guard existing specifically to avoid
/// a duplicate-parameter compile error (CS0100) in exactly that case. These
/// tests close all three cells with a live, real-compile proof.
/// </summary>
public class TransactionNameCollisionGuardTests
{
    // ── Algorithm 1: HasStaticTransactionParam (named-query CRUD) ──────────

    private const string ProductsSchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  }
}";

    private static (GeneratorDriverRunResult result, Compilation compilation) RunNamedQuery(
        string sql, string sqlFilePath, string schemaJson, bool autoCrud = false)
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
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
        };

        var compilation = CSharpCompilation.Create("TxGuardTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(sqlFilePath, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver.GetRunResult(), outputCompilation);
    }

    private static string AllSources(GeneratorDriverRunResult result) =>
        string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

    [Fact]
    public void NamedUpdate_WithParameterLiterallyNamedTransaction_DoesNotDuplicateAndCompiles()
    {
        // "@Transaction" bound (arbitrarily) to product_id: what matters is the
        // raw SQL parameter's name, not what it is bound to.
        var (result, compilation) = RunNamedQuery(
            "update products set product_name = @ProductName where product_id = @Transaction",
            "db/Products/Update.sql", ProductsSchema);

        string src = AllSources(result);

        // The static overload must NOT declare the injected
        // "DbTransaction? transaction = null" alongside the SQL parameter also
        // named "Transaction" -- that would be CS0100 (duplicate parameter).
        Assert.DoesNotContain("System.Data.Common.DbTransaction? transaction = null", src);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void NamedUpdate_WithOrdinaryParameterName_StillGetsInjectedTransactionParam()
    {
        // Control case: an unrelated parameter name must still get the usual
        // optional static DbTransaction overload (proves the guard isn't
        // simply always suppressing it).
        var (result, compilation) = RunNamedQuery(
            "update products set product_name = @ProductName where product_id = @ProductId",
            "db/Products/Update.sql", ProductsSchema);

        string src = AllSources(result);
        Assert.Contains("DbTransaction? transaction = null", src);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // ── Algorithm 2: TxForwardable (synthetic AutoCrud POCO overloads) ─────

    private const string TransactionColumnSchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""ledger_entries"": {
      ""name"": ""ledger_entries"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""transaction"": { ""name"": ""transaction"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  }
}";

    private static (GeneratorDriverRunResult result, Compilation compilation) RunAutoCrudOnly(string schemaJson)
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
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnector.MySqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.NonGeneric.dll")),
        };

        var compilation = CSharpCompilation.Create("TxGuardAutoCrudAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver.GetRunResult(), outputCompilation);
    }

    [Fact]
    public void AutoCrud_TableWithColumnLiterallyNamedTransaction_PocoOverloadsCompileClean()
    {
        // A real (if unusually named) column called "transaction": the
        // Insert(row) POCO static overload forwards it as
        // row.Transaction -- TxForwardable must suppress the injected
        // DbTransaction "transaction" parameter here too, exactly mirroring
        // HasStaticTransactionParam (which the underlying scalar Insert(...)
        // method itself uses), or the forwarding call would pass a mismatched
        // argument count/type, or the static Insert signature itself would
        // declare "transaction" twice (CS0100).
        var (result, compilation) = RunAutoCrudOnly(TransactionColumnSchema);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        string src = string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("row.Transaction", src);
    }

    // ── Algorithm 3: EmitProcCallBody (stored-procedure calls) ─────────────

    private const string ProcSchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {},
  ""procedures"": {
    ""ArchiveOrder"": {
      ""name"": ""ArchiveOrder"",
      ""params"": [
        { ""name"": ""OrderId"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""Transaction"", ""dbType"": ""varchar"", ""direction"": ""In"", ""isNullable"": false, ""maxLength"": 40 }
      ],
      ""results"": []
    },
    ""ArchiveOrderShouty"": {
      ""name"": ""ArchiveOrderShouty"",
      ""params"": [
        { ""name"": ""OrderId"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""TRANSACTION"", ""dbType"": ""varchar"", ""direction"": ""In"", ""isNullable"": false, ""maxLength"": 40 }
      ],
      ""results"": []
    }
  }
}";

    [Fact]
    public void ProcCall_WithInParamLiterallyNamedTransaction_DoesNotDuplicateAndCompiles()
    {
        var (result, compilation) = RunNamedQuery(
            "-- @call ArchiveOrder\n", "db/Orders/ArchiveOrder.sql", ProcSchema);

        string src = AllSources(result);

        // ToCamelCase(ToPascalCase("Transaction")) == "transaction": a genuine
        // collision. The static overload must carry the proc's own
        // "string transaction" parameter and must NOT also inject the
        // "System.Data.Common.DbTransaction? transaction = null" parameter.
        Assert.Contains("string transaction", src);
        Assert.DoesNotContain("System.Data.Common.DbTransaction? transaction = null", src);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ProcCall_WithInParamNamedAllCaps_CamelCasesToDifferentIdentifier_NoCollisionAndCompiles()
    {
        // "TRANSACTION" (all caps): ToPascalCase leaves an all-letters single
        // word's casing untouched apart from forcing the first letter upper
        // (already upper here), and ToCamelCase only lowercases index 0 -- so
        // this proc parameter's real C# identifier is "tRANSACTION", not
        // "transaction". There is genuinely no collision, so the static
        // overload's injected DbTransaction "transaction" parameter is
        // expected to be present alongside it.
        var (result, compilation) = RunNamedQuery(
            "-- @call ArchiveOrderShouty\n", "db/Orders/ArchiveOrderShouty.sql", ProcSchema);

        string src = AllSources(result);

        Assert.Contains("string tRANSACTION", src);
        Assert.Contains("System.Data.Common.DbTransaction? transaction = null", src);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}
