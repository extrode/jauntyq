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

    private const string MySqlSequenceSchema = @"{
  ""dialect"": ""mysql"",
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

    private const string CollidingSequenceNamesSchema = @"{
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
    ""order_number"": { ""name"": ""order_number"", ""startValue"": 100, ""increment"": 5 },
    ""OrderNumber"": { ""name"": ""OrderNumber"", ""startValue"": 1, ""increment"": 1 }
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

    /// <summary>
    /// Regression test for a silent-data-loss gap: MySqlExtractor populates
    /// DatabaseSchema.Sequences for a MariaDB CREATE SEQUENCE object (the
    /// "mysql" dialect string covers both real MySQL, which has none, and
    /// MariaDB, which does), but EmitSequenceAccessor previously excluded
    /// every dialect except sqlserver/postgres outright -- a MariaDB schema
    /// with real sequences got them extracted into the snapshot and then
    /// silently got no db.Sequences accessor generated at all, with no
    /// diagnostic. NEXTVAL(name) verified live against mariadb:11.
    /// </summary>
    [Fact]
    public void MySql_EmitsSequenceAccessor_WithNextvalFunctionSql()
    {
        var (result, compilation) = Run(MySqlSequenceSchema);

        var db = TryGetSource(result, "JauntyDb.g.cs");
        Assert.NotNull(db);
        Assert.Contains("public SequenceAccessor Sequences =>", db);
        Assert.Contains("public long NextOrderNumber()", db);
        Assert.Contains("public System.Threading.Tasks.Task<long> NextOrderNumberAsync(", db);
        Assert.Contains("SELECT NEXTVAL(order_number)", db);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Regression test for AUD-R44-01: two distinct, legal sequence identifiers
    /// that fold to the same PascalCase method name via
    /// <c>DialectMapper.ToPascalCase</c> ("order_number" and "OrderNumber" both
    /// -&gt; "OrderNumber") previously emitted two identically-named C# members
    /// in <c>SequenceAccessor</c> with no JauntyQ diagnostic at all -- a real
    /// CS0111 "already defines a member" compile error confirmed live against
    /// the unfixed source, the same defect shape AUD-R2-03/JNT3009 already
    /// covers for colliding result-column aliases. Now reports JNT2009 and
    /// keeps only the first-declared sequence's accessor.
    /// </summary>
    [Fact]
    public void CollidingSequenceNames_ReportJNT2009_KeepFirstOnly_NoCompileError()
    {
        var (result, compilation) = Run(CollidingSequenceNamesSchema);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT2009");

        var db = TryGetSource(result, "JauntyDb.g.cs");
        Assert.NotNull(db);
        Assert.Contains("public long NextOrderNumber()", db);
        // Only the first-declared sequence ("order_number") keeps its accessor;
        // the second ("OrderNumber") is dropped, not double-emitted.
        Assert.Contains("SELECT NEXT VALUE FOR order_number", db);
        Assert.DoesNotContain("SELECT NEXT VALUE FOR OrderNumber", db);

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }
}
