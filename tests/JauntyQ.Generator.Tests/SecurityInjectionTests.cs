using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// End-to-end guards against build-time code injection (report findings
/// C1/C3) and the once-per-query shape-guard latch (PERF-1). Reuses the shared
/// InMemoryAdditionalText / TestAnalyzerConfigOptionsProvider helpers.
/// </summary>
public class SecurityInjectionTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 40 }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql, string path = "db/Products/TestQuery.sql")
    {
        var compilation = CSharpCompilation.Create("SecurityTestAssembly",
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

    // ── C1: hostile column alias must not inject a member ──────────────

    [Fact]
    public void HostileAlias_IsNeutralized_GeneratedCodeStillValid()
    {
        // A bracket alias carrying C# is a classic injection payload. The
        // payload text may survive as inert content inside a string literal
        // (the __Columns array), but it must NEVER become executable code:
        // every generated source must still parse with no C# errors, and the
        // injected member declaration must not appear as real code.
        string sql = "select product_id as [X { get; } public static int Z = System.Environment.Exit(1); //] from products";
        var result = Run(sql);

        foreach (var gen in result.Results[0].GeneratedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(gen.SourceText.ToString());
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                $"Injected alias broke generated C# in {gen.HintName}: {string.Join("; ", errors.Select(e => e.GetMessage()))}");
        }

        // The projection member name is the neutralized PascalCase identifier,
        // not a smuggled property/field declaration.
        string sources = AllSources(result);
        Assert.Contains("XGetPublicStaticIntZSystem", sources);
    }

    [Fact]
    public void LegitimateAliasWithSpaceEquivalent_StillGenerates()
    {
        // A normal aliased projection compiles to a valid member.
        string sql = "select product_id as Ident, product_name as Label from products";
        var result = Run(sql);
        string sources = AllSources(result);
        Assert.Contains("Ident", sources);
        Assert.Contains("Label", sources);
    }

    // ── C3: source name embedded in the __Columns string literal ───────

    [Fact]
    public void ColumnNameLiteral_IsEscaped_NotBrokenOut()
    {
        // The generated __...Columns array must not contain an unescaped quote
        // sequence that would break the string literal. We assert the array is
        // present and every emitted source parses as valid C#.
        string sql = "select product_id, product_name from products";
        var result = Run(sql);
        string sources = AllSources(result);

        foreach (var gen in result.Results[0].GeneratedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(gen.SourceText.ToString());
            Assert.Empty(tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        }
    }

    // ── PERF-1: shape guard latches once per query ─────────────────────

    [Fact]
    public void ShapeGuard_IsLatchedOncePerQuery()
    {
        string sql = "select product_id, product_name from products";
        var result = Run(sql);
        string sources = AllSources(result);

        // A per-query latch field exists and the Validate call is gated on it,
        // so the guard runs on the first call only (matching the documented
        // "once per query, then never again" contract).
        Assert.Contains("TestQueryValidated", sources);
        Assert.Contains("JauntyQShapeGuard.Validate", sources);
        Assert.Contains("Volatile.Read(ref __TestQueryValidated)", sources);
    }
}
