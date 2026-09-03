using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// Consumer-gaps-report gap #7 repro: two tables share a column name
/// ("outcome") with different nullability. A hand-written query that
/// explicitly qualifies a parameter's bound column with a source-table
/// alias should infer nullability from THAT table, not silently prefer
/// the CRUD target table just because it happens to share the column name.
/// </summary>
public class NullabilityInferenceTests
{
    private const string SchemaJson = @"{
  ""tables"": {
    ""mail_queue"": {
      ""name"": ""mail_queue"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""bigint"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""outcome"": { ""name"": ""outcome"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 64 }
      }
    },
    ""events"": {
      ""name"": ""events"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""bigint"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""outcome"": { ""name"": ""outcome"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 64 }
      }
    }
  }
}";

    private static GeneratorDriverRunResult RunGenerator(string sql, string sqlFilePath)
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
        };

        var compilation = CSharpCompilation.Create("NullabilityInferenceTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(sqlFilePath, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)
            ))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        var result = driver.GetRunResult();
        Assert.Empty(diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
        return result;
    }

    private static string GetSource(GeneratorDriverRunResult result, string hintSubstring)
    {
        foreach (var tree in result.GeneratedTrees)
        {
            if (tree.FilePath.Contains(hintSubstring))
                return tree.GetText().ToString();
        }
        throw new System.Exception($"No generated tree matching '{hintSubstring}' found. Available: " +
            string.Join(", ", result.GeneratedTrees.Select(t => t.FilePath)));
    }

    [Fact]
    public void Update_InfersNullabilityFromTargetTable_NotSameNamedColumnElsewhere()
    {
        var sql = "update mail_queue set outcome = @outcome where id = @id";

        var result = RunGenerator(sql, "db/MailQueue/Complete.sql");
        var source = GetSource(result, "MailQueue.Complete.g.cs");

        Assert.Contains("string? outcome", source);
    }

    [Fact]
    public void InsertSelect_AliasQualifiedParam_InfersFromSourceTable_NotCrudTargetTable()
    {
        // @filterOutcome is explicitly bound to events.outcome (non-nullable) via
        // the "e" alias in the SELECT source's WHERE clause. The INSERT target
        // (mail_queue) happens to ALSO have an "outcome" column, but a
        // differently-typed one (nullable). The alias qualification should win.
        var sql = "insert into mail_queue (id) " +
                   "select e.id from events e " +
                   "where e.id = @id and e.outcome = @filterOutcome";

        var result = RunGenerator(sql, "db/MailQueue/ImportFromEvents.sql");
        var source = GetSource(result, "MailQueue.ImportFromEvents.g.cs");

        Assert.Contains("string filterOutcome", source);
        Assert.DoesNotContain("string? filterOutcome", source);
    }
}
