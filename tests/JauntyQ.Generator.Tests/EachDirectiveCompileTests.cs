using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Real-compile verification for -- @each generated code: proves the emitted
/// IReadOnlyList&lt;T&gt; parameter, empty-list guard, runtime CommandText
/// expansion, and per-element parameter binding are not just plausible-looking
/// strings but actually compile against a real ADO.NET provider (plain
/// System.Data.Common for SQLite/SQL Server-shaped output, and the real
/// Npgsql assembly for the Postgres-typed binding path). Mirrors
/// NpgsqlTypedParamTests.GeneratedPostgresCode_CompilesAgainstRealNpgsql.
/// </summary>
public class EachDirectiveCompileTests
{
    private const string SqliteSchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""text"", ""isNullable"": false, ""maxLength"": 40 }
      }
    }
  }
}";

    private const string PostgresSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""character varying"", ""isNullable"": false, ""maxLength"": 40, ""isUnicode"": true }
      }
    }
  }
}";

    private const string EachSql = "-- @each Ids\nselect product_id, product_name from products where product_id in (@Ids)";

    private static GeneratorDriverRunResult Run(string schemaJson, string sql, string path = "db/Products/EachQuery.sql")
    {
        var compilation = CSharpCompilation.Create("EachCompileTestAssembly",
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

    private static string QuerySource(GeneratorDriverRunResult result) =>
        result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Products.EachQuery.g.cs")
            .SourceText.ToString();

    private static List<MetadataReference> BaseReferences(string runtimeDir) => new()
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
        MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
        MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Linq.dll")),
        MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.dll")),
        MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
        MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "netstandard.dll")),
    };

    [Fact]
    public void GeneratedEachCode_CompilesAgainstPlainAdo()
    {
        var result = Run(SqliteSchemaJson, EachSql);
        string source = QuerySource(result);

        string runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = BaseReferences(runtimeDir);

        var extraSources = result.Results[0].GeneratedSources
            .Where(s => s.HintName is "Products.Core.g.cs" or "Products.Row.g.cs" or "JauntyDb.g.cs" or "JauntyQShapeGuard.g.cs")
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        extraSources.Add(CSharpSyntaxTree.ParseText(source));

        var compilation = CSharpCompilation.Create("EachPlainAdoEmittedCode",
            extraSources,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "generated @each code failed to compile against plain ADO.NET:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void GeneratedEachCode_CompilesAgainstRealNpgsql()
    {
        var result = Run(PostgresSchemaJson, EachSql);
        string source = QuerySource(result);

        string runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = BaseReferences(runtimeDir);
        references.Add(MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlParameter).Assembly.Location));

        var extraSources = result.Results[0].GeneratedSources
            .Where(s => s.HintName is "Products.Core.g.cs" or "Products.Row.g.cs" or "JauntyDb.g.cs" or "JauntyQShapeGuard.g.cs")
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        extraSources.Add(CSharpSyntaxTree.ParseText(source));

        var compilation = CSharpCompilation.Create("EachNpgsqlEmittedCode",
            extraSources,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "generated @each code failed to compile against Npgsql:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }
}
