using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Tier 5: PostgreSQL emits NpgsqlParameter&lt;T&gt;.TypedValue - the value
/// stays strongly typed through the ADO.NET boundary (no per-parameter
/// boxing). The generated output is COMPILED against the real Npgsql
/// assembly to prove the emitted API surface exists.
/// </summary>
public class NpgsqlTypedParamTests
{
    private const string PostgresSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""character varying"", ""isNullable"": false, ""maxLength"": 40, ""isUnicode"": true },
        ""unit_price"": { ""name"": ""unit_price"", ""dbType"": ""numeric"", ""isNullable"": true, ""precision"": 10, ""scale"": 2 }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(string schemaJson, string sql, string path = "db/Products/TestQuery.sql")
    {
        var compilation = CSharpCompilation.Create("NpgsqlTypedTestAssembly",
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
            .Single(s => s.HintName == "Products.TestQuery.g.cs")
            .SourceText.ToString();

    [Fact]
    public void PostgresDialect_EmitsTypedParameters_NoBoxing()
    {
        var result = Run(PostgresSchemaJson,
            "select product_id, product_name\nfrom products\nwhere products.product_id = @product_id and products.unit_price > @unit_price");
        string source = QuerySource(result);

        Assert.Contains("new global::Npgsql.NpgsqlParameter<int> { ParameterName = \"@product_id\", TypedValue = product_id }", source);
        Assert.Contains("new global::Npgsql.NpgsqlParameter<decimal?> { ParameterName = \"@unit_price\", TypedValue = unit_price }", source);

        // the boxing/untyped path is gone entirely
        Assert.DoesNotContain("cmd.CreateParameter()", source);
        Assert.DoesNotContain(".Value = (object?)", source);
        Assert.DoesNotContain(".DbType =", source);
        Assert.DoesNotContain(".Size =", source); // OID-typed protocol: no plan-cache-by-length concern
    }

    [Fact]
    public void PostgresCrud_TypedParameters_GuardsStillEmitted()
    {
        var result = Run(PostgresSchemaJson,
            "insert into products (product_name, unit_price)\nvalues (@product_name, @unit_price)");
        string source = QuerySource(result);

        Assert.Contains("new global::Npgsql.NpgsqlParameter<string>", source);
        // value safety is dialect-independent
        Assert.Contains("if (product_name.Length > 40)", source);
    }

    [Fact]
    public void GeneratedPostgresCode_CompilesAgainstRealNpgsql()
    {
        var result = Run(PostgresSchemaJson,
            "insert into products (product_name, unit_price)\nvalues (@product_name, @unit_price)");
        string source = QuerySource(result);

        string runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlParameter).Assembly.Location),
        };

        // entity core supplies _conn/_db; shape guard supplies the validator type
        var extraSources = result.Results[0].GeneratedSources
            .Where(s => s.HintName is "Products.Core.g.cs" or "JauntyDb.g.cs" or "JauntyQShapeGuard.g.cs")
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        extraSources.Add(CSharpSyntaxTree.ParseText(source));

        var compilation = CSharpCompilation.Create("NpgsqlEmittedCode",
            extraSources,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "generated postgres code failed to compile against Npgsql:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void SqlServerDialect_KeepsUntypedPathWithSizing()
    {
        const string sqlServerSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40, ""isUnicode"": true }
      }
    }
  }
}";
        var result = Run(sqlServerSchemaJson,
            "select product_id\nfrom products\nwhere products.product_name = @product_name");
        string source = QuerySource(result);

        Assert.Contains("cmd.CreateParameter()", source);
        Assert.Contains(".DbType = System.Data.DbType.String", source);
        Assert.Contains(".Size = product_name.Length > 40 ? product_name.Length : 40;", source);
        Assert.DoesNotContain("NpgsqlParameter", source);
    }
}
