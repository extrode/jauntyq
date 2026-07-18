using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R50-03 (residual scope, closed round 59): generic BCL/ADO.NET simple
/// names that were emitted as bare literals — <c>DbType</c>, <c>Convert</c>,
/// <c>Math</c>, <c>Array</c>, <c>StringComparison</c>, <c>Volatile</c>,
/// <c>IDisposable</c>, <c>DbEnumerator</c>, <c>InvalidOperationException</c>,
/// <c>ArgumentException</c>, <c>ArgumentNullException</c>,
/// <c>IndexOutOfRangeException</c> — are now routed through
/// <c>CodeEmitter.TypeRef</c>, so a table whose entity or row-POCO name equals
/// one of them gets a <c>global::</c>-qualified reference instead of a
/// namespace-wide shadowing break (CS0117/CS1729/CS1615/CS9035 cascades with
/// zero JauntyQ diagnostic).
///
/// Unlike round 55's 12 ADO.NET plumbing names (DbCommand, DbParameter, ...,
/// guarded via the JNT2006 reserved-name set — those appear at ~60 emission
/// sites), these names appear at few enough sites that qualification is the
/// right fix: the colliding table simply works, and no diagnostic is needed.
/// The JNT2006-Error route was rejected for these names specifically because
/// several are dialect/feature-conditional (a sqlite project with a table
/// named "converts", "maths", "arrays", or "string_comparisons" compiled and
/// worked fine before this fix), so a reserved-name Error would have been a
/// false-positive build blocker on a valid schema.
/// </summary>
public class BclReservedNameTypeRefTests
{
    private static string TableSchema(string dialect, string tableName, bool withSequence = false)
    {
        string seq = withSequence
            ? @",
  ""sequences"": { ""order_number"": { ""name"": ""order_number"", ""startValue"": 100, ""increment"": 5 } }"
            : "";
        return $@"{{
  ""dialect"": ""{dialect}"",
  ""tables"": {{
    ""{tableName}"": {{
      ""name"": ""{tableName}"",
      ""columns"": {{
        ""id"": {{ ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true }},
        ""label"": {{ ""name"": ""label"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 40 }}
      }}
    }}
  }}{seq}
}}";
    }

    private static (List<string> errors, string allSources) RunAutoCrudAndCompile(string schemaJson)
    {
        var compilation0 = CSharpCompilation.Create("BclReservedNameSeed",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation0, out _, out _);
        var result = driver.GetRunResult();

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        string allSources = string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

        string runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.NonGeneric.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlParameter).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Data.SqlClient.SqlParameter).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnector.MySqlParameter).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create("BclReservedNameCompile",
            allTrees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        return (errors, allSources);
    }

    // One row per residual name, on the dialect/feature combination whose
    // emitted output actually contains the bare reference (empirically
    // verified round 59: every one of these produced a real pre-fix compile
    // failure — CS0117/CS1729/CS1615/CS9035 — with zero JauntyQ diagnostic).
    // rowPoco is the singularized row-POCO type the table name folds to; the
    // test asserts it is really declared, so the collision trigger is real
    // and a future Inflector change can't silently hollow this test out.
    [Theory]
    [InlineData("db_types", "sqlite", false, "DbType")]
    [InlineData("invalid_operation_exceptions", "sqlite", false, "InvalidOperationException")]
    [InlineData("i_disposables", "sqlite", false, "IDisposable")]
    [InlineData("argument_null_exceptions", "sqlite", false, "ArgumentNullException")]
    [InlineData("argument_exceptions", "sqlite", false, "ArgumentException")]
    [InlineData("volatiles", "sqlite", false, "Volatile")]
    [InlineData("converts", "postgres", true, "Convert")]
    [InlineData("maths", "sqlserver", false, "Math")]
    [InlineData("arrays", "sqlserver", false, "Array")]
    [InlineData("string_comparisons", "mysql", false, "StringComparison")]
    [InlineData("db_enumerators", "sqlserver", false, "DbEnumerator")]
    [InlineData("index_out_of_range_exceptions", "sqlserver", false, "IndexOutOfRangeException")]
    public void TableCollidingWithEmittedBclSimpleName_GeneratedCode_StillCompiles(
        string table, string dialect, bool withSequence, string rowPoco)
    {
        var (errors, allSources) = RunAutoCrudAndCompile(TableSchema(dialect, table, withSequence));

        // The collision trigger is real: the row POCO really is declared with
        // the shadowing simple name.
        Assert.Contains($"public class {rowPoco}", allSources);

        Assert.True(errors.Count == 0,
            $"generated code for a '{table}' table ({dialect}) failed to compile:\n" +
            string.Join("\n", errors.Take(8)));
    }

    // The qualification really is collision-driven, not unconditional: a
    // benign schema keeps the short, readable bare form (cosmetics guarantee
    // of the human-readable-codegen initiative), while a colliding schema
    // gets the global::-qualified reference.
    [Fact]
    public void NonCollidingSchema_KeepsBareNames_CollidingSchema_GetsGlobalQualified()
    {
        var (_, benign) = RunAutoCrudAndCompile(TableSchema("sqlite", "widgets"));
        Assert.Contains("throw new InvalidOperationException(", benign);
        Assert.Contains(".DbType = DbType.", benign);
        Assert.DoesNotContain("global::System.InvalidOperationException", benign);

        var (_, colliding) = RunAutoCrudAndCompile(TableSchema("sqlite", "invalid_operation_exceptions"));
        Assert.Contains("throw new global::System.InvalidOperationException(", colliding);

        var (_, collidingDbType) = RunAutoCrudAndCompile(TableSchema("sqlite", "db_types"));
        Assert.Contains(".DbType = global::System.Data.DbType.", collidingDbType);
    }
}
