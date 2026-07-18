using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Real-compile verification for SELECT-list columns whose C# type has no
/// dedicated ADO.NET reader-getter method: TimeSpan (Postgres "time"),
/// IPAddress ("inet"/"cidr"), and any array type ("integer[]", "text[]",
/// "uuid[]", ...). GetReaderCall used to fall through to the untyped
/// `reader.GetValue(ordinal)` for all three, which is CS0266 (cannot
/// implicitly convert 'object' to the declared property type) at the
/// property-initializer assignment -- invisible to a parse-only check since
/// object-to-T is a binding-time error, not a syntax error.
/// </summary>
public class GetReaderCallCompileTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""network_events"": {
      ""name"": ""network_events"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""addr"": { ""name"": ""addr"", ""dbType"": ""inet"", ""isNullable"": false },
        ""happened_at"": { ""name"": ""happened_at"", ""dbType"": ""time"", ""isNullable"": true },
        ""tags"": { ""name"": ""tags"", ""dbType"": ""text[]"", ""isNullable"": true },
        ""ids"": { ""name"": ""ids"", ""dbType"": ""integer[]"", ""isNullable"": false },
        ""observed_at"": { ""name"": ""observed_at"", ""dbType"": ""time with time zone"", ""isNullable"": true }
      }
    }
  }
}";

    private const string Sql = "select id, addr, happened_at, tags, ids, observed_at from network_events where id = @id";

    private static GeneratorDriverRunResult Run()
    {
        var compilation = CSharpCompilation.Create("GetReaderCallCompileTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/NetworkEvents/GetById.sql", Sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static System.Collections.Generic.List<MetadataReference> BaseReferences()
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
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Net.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlParameter).Assembly.Location),
        };
    }

    [Fact]
    public void GeneratedSelectCode_WithInetTimeAndArrayColumns_CompilesClean()
    {
        var result = Run();
        string querySource = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "NetworkEvents.Row.g.cs").SourceText.ToString();

        // Each type that has no dedicated Get* method gets an explicit cast
        // on GetValue (against the non-nullable base type, same as the
        // pre-existing "byte[]" case); the surrounding IsDBNull conditional
        // (emitted separately for nullable columns) carries the "?" instead.
        Assert.Contains("(System.Net.IPAddress)reader.GetValue(", querySource);
        Assert.Contains("(TimeSpan)reader.GetValue(", querySource);
        Assert.Contains("(string[])reader.GetValue(", querySource);
        Assert.Contains("(int[])reader.GetValue(", querySource);
        // DialectMapper maps "time with time zone" (Postgres) and SQL Server's
        // "datetimeoffset" to System.DateTimeOffset (task #26), but GetReaderCall
        // had no case for it and fell through to the bare, untyped
        // reader.GetValue(ordinal) -- CS0266 assigning object to DateTimeOffset.
        Assert.Contains("(DateTimeOffset)reader.GetValue(", querySource);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();

        var compilation = CSharpCompilation.Create("GetReaderCallEmittedCode",
            allTrees,
            BaseReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated SELECT code with inet/time/array columns failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }
}
