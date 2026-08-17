using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R50-02 regression: <c>CodeEmitter.TypeRef</c>'s collision guard
/// (<c>SchemaHasEntityNamed</c>) checked only entity accessor names
/// (<c>DialectMapper.ToPascalCase(table.Name)</c>) — but row POCOs
/// (<c>Inflector.RowTypeName</c>, the singularized entity name) are ALSO
/// top-level classes in <c>namespace JauntyQ.Generated</c>, derived from the
/// same fully user-controlled table name. A table named "date_times" yields
/// entity "DateTimes" (no collision) and row POCO "DateTime" — which shadows
/// <c>System.DateTime</c> at arity 0 for every file in the namespace,
/// including the POCO's own DateTime-typed properties: an unguarded CS0029
/// build blocker with zero JauntyQ diagnostic. Same family as AUD-R44-01 /
/// AUD-R46-01 (PascalCase-folded schema names silently breaking emitted C#),
/// new sub-shape (BCL shadowing via the singularized POCO name slipping past
/// the entity-name-only guard).
/// </summary>
[Trait("Category", "AuditRegression")]
public class TypeRefRowPocoCollisionTests
{
    // Table "date_times": entity "DateTimes", row POCO "DateTime".
    private const string DateTimesSchema = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""date_times"": {
      ""name"": ""date_times"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""when_at"": { ""name"": ""when_at"", ""dbType"": ""datetime"", ""isNullable"": false }
      }
    }
  }
}";

    private static (GeneratorDriverRunResult result, Compilation compilation) RunAutoCrud(string schemaJson)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("");
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.Common.DbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
        };

        var compilation = CSharpCompilation.Create("RowPocoCollisionTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver.GetRunResult(), outputCompilation);
    }

    [Fact]
    public void RowPocoNamedDateTime_GeneratedCode_StillCompiles()
    {
        var (result, compilation) = RunAutoCrud(DateTimesSchema);

        // The row POCO really is named DateTime (this is the collision trigger,
        // not an artifact of the fix).
        Assert.Contains(result.GeneratedTrees, t => t.ToString().Contains("public class DateTime"));

        // Every DateTime-typed reference in the namespace must be qualified
        // past the shadowing POCO — zero compile errors.
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void TypeRef_RowPocoCollision_ReturnsGlobalQualified()
    {
        // Unit-level: the guard must treat the singularized row-POCO name as a
        // generated top-level type, exactly like the entity name itself.
        var schema = new JauntyQ.Schema.DatabaseSchema();
        schema.Tables["date_times"] = new JauntyQ.Schema.TableSchema
        {
            Name = "date_times",
            Columns = new Dictionary<string, JauntyQ.Schema.ColumnSchema>
            {
                ["id"] = new JauntyQ.Schema.ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true }
            }
        };
        Assert.Equal("global::System.DateTime", CodeEmitter.TypeRef(schema, "DateTime", "System"));
    }
}
