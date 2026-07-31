using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Generator-level coverage for the -- @stream directive: it emits lazy
/// IEnumerable/IAsyncEnumerable iterators (not buffered List&lt;T&gt;), and the
/// JNT3003 gate rejects invalid combinations.
/// </summary>
public class StreamDirectiveTests
{
    private const string SchemaJson = @"{
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

    private static GeneratorDriverRunResult Run(string sql, string path = "db/Products/StreamQuery.sql")
    {
        var compilation = CSharpCompilation.Create("StreamTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(path, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string AllSources(GeneratorDriverRunResult result) =>
        string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

    [Fact]
    public void Stream_EmitsEnumerableAndAsyncEnumerable_NotList()
    {
        string sql = "-- @stream\nselect product_id, product_name from products";
        var result = Run(sql);
        string src = AllSources(result);

        Assert.Contains("IEnumerable<", src);
        Assert.Contains("IAsyncEnumerable<", src);
        Assert.Contains("yield return", src);
        Assert.Contains("[EnumeratorCancellation]", src);
        // The streamed query method must not buffer into a List.
        Assert.DoesNotContain("var results = new List<", src);
    }

    [Fact]
    public void NonStream_StillBuffersIntoList()
    {
        string sql = "select product_id, product_name from products";
        var result = Run(sql, "db/Products/PlainQuery.sql");
        string src = AllSources(result);
        // AUD-R69-01: "__results", not "results".
        Assert.Contains("var __results = new List<", src);
        Assert.DoesNotContain("yield return", src);
    }

    [Fact]
    public void Stream_WithFirst_ReportsJNT3003()
    {
        string sql = "-- @stream\n-- @first\nselect product_id, product_name from products";
        var result = Run(sql);
        Assert.Contains(result.Results[0].Diagnostics, d => d.Id == "JNT3003");
    }

    [Fact]
    public void Stream_WithProc_ReportsJNT3003()
    {
        // R9 §2.5 mandate (2.5-directive-combinations-jnt3003), sibling-sweep
        // of the row: @stream's own precondition gate (JauntyQGenerator
        // .Part2.cs) also rejects @proc, alongside the already-tested
        // @first combo -- never previously exercised.
        string sql = "-- @stream\n-- @proc\nselect product_id, product_name from products";
        var result = Run(sql);
        Assert.Contains(result.Results[0].Diagnostics, d => d.Id == "JNT3003");
        Assert.Contains(result.Results[0].Diagnostics,
            d => d.Id == "JNT3003" && d.GetMessage().Contains("cannot be combined with -- @proc"));
    }

    [Fact]
    public void GeneratedStreamCode_ParsesClean()
    {
        string sql = "-- @stream\nselect product_id, product_name from products";
        var result = Run(sql);
        foreach (var gen in result.Results[0].GeneratedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(gen.SourceText.ToString());
            Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        }
    }
}
