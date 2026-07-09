using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class SequenceGenerationTests
{
    private const string SqlServerSequenceSchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": [],
  ""sequences"": {
    ""order_number"": { ""name"": ""order_number"", ""startValue"": 100, ""increment"": 5 }
  }
}";

    private const string PostgresSequenceSchema = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": [],
  ""sequences"": {
    ""order_number"": { ""name"": ""order_number"", ""startValue"": 100, ""increment"": 5 }
  }
}";

    private static (GeneratorDriverRunResult result, Compilation compilation) Run(string schemaJson)
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
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnector.MySqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlParameter).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.NonGeneric.dll")),
        };

        var compilation = CSharpCompilation.Create("SequenceTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText>
        {
            new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)
        };

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.CreateRange(texts))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver.GetRunResult(), outputCompilation);
    }

    private static string? TryGetSource(GeneratorDriverRunResult result, string hintSuffix)
    {
        foreach (var tree in result.GeneratedTrees)
        {
            if (tree.FilePath.EndsWith(hintSuffix, StringComparison.OrdinalIgnoreCase))
                return tree.ToString();
        }
        return null;
    }

    [Fact]
    public void SqlServer_EmitsSequenceAccessor_WithNextValueForSql()
    {
        var (result, compilation) = Run(SqlServerSequenceSchema);

        var db = TryGetSource(result, "JauntyDb.g.cs");
        Assert.NotNull(db);
        Assert.Contains("public SequenceAccessor Sequences =>", db);
        Assert.Contains("public sealed class SequenceAccessor", db);
        Assert.Contains("public long NextOrderNumber()", db);
        Assert.Contains("public System.Threading.Tasks.Task<long> NextOrderNumberAsync(", db);
        Assert.Contains("SELECT NEXT VALUE FOR order_number", db);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Postgres_EmitsSequenceAccessor_WithNextvalSql()
    {
        var (result, compilation) = Run(PostgresSequenceSchema);

        var db = TryGetSource(result, "JauntyDb.g.cs");
        Assert.NotNull(db);
        Assert.Contains("public long NextOrderNumber()", db);
        Assert.Contains(@"SELECT nextval('order_number')", db);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}
