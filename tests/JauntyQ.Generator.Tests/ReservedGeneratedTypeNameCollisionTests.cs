using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R52-01: an entity accessor or row POCO whose PascalCase name equals
/// one of the two types the generator itself always emits regardless of
/// schema content -- <c>JauntyDb</c> (this file's own <c>EmitJauntyDb</c>)
/// and <c>JauntyQShapeGuard</c> (<c>JauntyQGenerator.cs</c>'s
/// <c>RegisterPostInitializationOutput</c>) -- silently collided with zero
/// JauntyQ diagnostic. Neither reserved name is user-derived, so the
/// pre-existing entity-vs-entity (JNT2010) and entity-vs-rowtype (JNT2006)
/// checks never compared against them: a table named "jaunty_db" produced a
/// real CS0260/CS0542/CS0102/CS0111/CS0229 cascade (duplicate `_conn` field,
/// duplicate constructor, a self-referential "JauntyDb JauntyDb { get; }"
/// property) with no JauntyQ-prefixed guidance at all. Same family as
/// AUD-R44-01/AUD-R46-01/AUD-R50-02 (PascalCase-folded, table-derived names
/// silently breaking emitted C#), new sub-shape: collision against the
/// generator's own always-present infrastructure types rather than another
/// user-derived name.
/// </summary>
[Trait("Category", "AuditRegression")]
public class ReservedGeneratedTypeNameCollisionTests
{
    // Table "jaunty_db": entityPascal "JauntyDb" (collides with the fixed
    // JauntyDb root class directly -- this is the entity-level case).
    private const string JauntyDbEntitySchema = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""jaunty_db"": {
      ""name"": ""jaunty_db"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""label"": { ""name"": ""label"", ""dbType"": ""text"", ""isNullable"": false }
      }
    }
  }
}";

    // Table "jaunty_dbs" (plural): entityPascal "JauntyDbs" (no entity-level
    // collision), but Inflector.RowTypeName singularizes it to "JauntyDb" --
    // the row-POCO-level case, independent of the entity-level one.
    private const string JauntyDbRowPocoSchema = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""jaunty_dbs"": {
      ""name"": ""jaunty_dbs"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""label"": { ""name"": ""label"", ""dbType"": ""text"", ""isNullable"": false }
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
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
        };

        var compilation = CSharpCompilation.Create("ReservedNameCollisionTestAssembly",
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
    public void EntityNamedJauntyDb_ReportsJNT2006_NamingTheReservedType()
    {
        var (result, _) = RunAutoCrud(JauntyDbEntitySchema);

        var jnt2006 = result.Results[0].Diagnostics
            .Where(d => d.Id == "JNT2006")
            .Select(d => d.GetMessage())
            .ToList();

        Assert.Contains(jnt2006, m => m.Contains("JauntyDb") && m.Contains("reserved"));
    }

    [Fact]
    public void RowPocoSingularizingToJauntyDb_ReportsJNT2006_NamingTheReservedType()
    {
        var (result, _) = RunAutoCrud(JauntyDbRowPocoSchema);

        var jnt2006 = result.Results[0].Diagnostics
            .Where(d => d.Id == "JNT2006")
            .Select(d => d.GetMessage())
            .ToList();

        Assert.Contains(jnt2006, m => m.Contains("JauntyDb") && m.Contains("reserved"));
    }
}
