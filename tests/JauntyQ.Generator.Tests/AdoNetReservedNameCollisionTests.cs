using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R50-03 (partial fix): unlike AUD-R52-01's JauntyDb/JauntyQShapeGuard
/// (a *declaration* collision with a type the generator itself declares),
/// the ADO.NET plumbing types (DbCommand, DbParameter, DbDataReader,
/// DbConnection, DbTransaction, CancellationToken, StringBuilder, DBNull,
/// ConnectionState, CommandType, CommandBehavior, ParameterDirection) are
/// emitted as bare literals at every call site and never routed through
/// CodeEmitter.TypeRef at all -- a table named after one of them makes every
/// *reference* to the real BCL/ADO.NET type inside that entity's own emitted
/// methods resolve to the entity's own class instead, with zero JauntyQ
/// diagnostic. Deliberately scoped to just these ADO.NET-specific names;
/// broader generic BCL words (Convert/Math/Array/Type/StringComparison/
/// exception type names) remain deferred (see the findings registry).
/// </summary>
public class AdoNetReservedNameCollisionTests
{
    // Table "db_commands": entityPascal "DbCommands" (no entity-level
    // collision), but Inflector.RowTypeName singularizes it to "DbCommand" --
    // the row-POCO-level case, same shape as AUD-R52-01's "jaunty_dbs" test.
    private const string DbCommandRowPocoSchema = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""db_commands"": {
      ""name"": ""db_commands"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""label"": { ""name"": ""label"", ""dbType"": ""text"", ""isNullable"": false }
      }
    }
  }
}";

    // Table "string_builder": entityPascal "StringBuilder" directly --
    // entity-level case.
    private const string StringBuilderEntitySchema = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""string_builder"": {
      ""name"": ""string_builder"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""label"": { ""name"": ""label"", ""dbType"": ""text"", ""isNullable"": false }
      }
    }
  }
}";

    private static GeneratorDriverRunResult RunAutoCrud(string schemaJson)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("");
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create("AdoNetReservedNameTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Fact]
    public void RowPocoSingularizingToDbCommand_ReportsJNT2006_NamingTheReservedType()
    {
        var result = RunAutoCrud(DbCommandRowPocoSchema);

        var jnt2006 = result.Results[0].Diagnostics
            .Where(d => d.Id == "JNT2006")
            .Select(d => d.GetMessage())
            .ToList();

        Assert.Contains(jnt2006, m => m.Contains("DbCommand") && m.Contains("reserved"));
    }

    [Fact]
    public void EntityNamedStringBuilder_ReportsJNT2006_NamingTheReservedType()
    {
        var result = RunAutoCrud(StringBuilderEntitySchema);

        var jnt2006 = result.Results[0].Diagnostics
            .Where(d => d.Id == "JNT2006")
            .Select(d => d.GetMessage())
            .ToList();

        Assert.Contains(jnt2006, m => m.Contains("StringBuilder") && m.Contains("reserved"));
    }
}
