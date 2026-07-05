using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Generator coverage for the auto-synthesized BulkInsert(IEnumerable&lt;Row&gt;):
/// it takes a sequence, wraps a transaction, excludes identity columns, and the
/// emitted code parses as valid C#.
/// </summary>
public class BulkInsertTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""Widgets"": {
      ""name"": ""Widgets"",
      ""columns"": {
        ""WidgetId"": { ""name"": ""WidgetId"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""Name"": { ""name"": ""Name"", ""dbType"": ""text"", ""isNullable"": false, ""maxLength"": 40 }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run()
    {
        var compilation = CSharpCompilation.Create("BulkTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // autoCrud ON so the BulkInsert synthetic is generated.
        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string AllSources(GeneratorDriverRunResult result) =>
        string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

    [Fact]
    public void BulkInsert_IsSynthesized_TakesIEnumerableAndExcludesIdentity()
    {
        var src = AllSources(Run());
        Assert.Contains("int BulkInsert(System.Collections.Generic.IEnumerable<", src);
        Assert.Contains("System.Threading.Tasks.Task<int> BulkInsertAsync(", src);
        Assert.Contains("BeginTransaction", src);
        // Identity WidgetId is excluded from the insert column list.
        Assert.Contains("insert into Widgets (Name)", src);
        Assert.DoesNotContain("insert into Widgets (WidgetId", src);
    }

    [Fact]
    public void GeneratedBulkCode_ParsesClean()
    {
        var result = Run();
        foreach (var gen in result.Results[0].GeneratedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(gen.SourceText.ToString());
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                $"bulk code broke in {gen.HintName}: {string.Join("; ", errors.Select(e => e.GetMessage()))}");
        }
    }
}
