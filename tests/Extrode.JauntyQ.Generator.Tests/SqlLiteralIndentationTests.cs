using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R50-01 regression: the human-readable-codegen pass introduced
/// <c>IndentSqlContinuationLines</c>, which re-indents every continuation
/// line of the emitted CommandText so the SQL reads aligned under the
/// opening quote in the generated source. But the CommandText is a C#
/// verbatim string — its whitespace IS the runtime SQL — and SQL string
/// literals may legally span lines (SqlTokenizer's literal scan accepts any
/// character except an unescaped closing quote, including '\n'). Padding a
/// newline that sits INSIDE a single-quoted literal silently changes the
/// value the database receives and returns: a pure behavioral regression
/// from a change that was scoped to never alter emitted behavior.
/// The helper must pad only newlines OUTSIDE literals/quoted identifiers.
/// </summary>
[Trait("Category", "AuditRegression")]
public class SqlLiteralIndentationTests
{
    private const string ProductsSchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  }
}";

    private static string RunAndCollectSources(string sql, string sqlFilePath)
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

        var compilation = CSharpCompilation.Create("SqlLiteralIndentTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(sqlFilePath, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", ProductsSchema)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result = driver.GetRunResult();
        return string.Join("\n\n", result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));
    }

    // The query under test: a projection expression whose string literal
    // spans two lines. "line1\nline2" must reach the engine byte-for-byte.
    private const string MultiLineLiteralSql =
        "-- @type labeled text\nselect product_name || 'line1\nline2' as labeled\nfrom products";

    [Fact]
    public void MultiLineStringLiteral_NotPaddedInEmittedCommandText()
    {
        string src = RunAndCollectSources(MultiLineLiteralSql, "db/Products/GetLabeled.sql");

        // The padded form (36 spaces injected after the newline INSIDE the
        // literal — EmitCommandText's alignment column) must never appear.
        string padded = "line1\n" + new string(' ', 36) + "line2";
        Assert.DoesNotContain(padded, src);

        // The literal must survive byte-for-byte.
        Assert.Contains("'line1\nline2'", src);
    }

    [Fact]
    public void ContinuationLineOutsideLiteral_StillIndented()
    {
        string src = RunAndCollectSources(MultiLineLiteralSql, "db/Products/GetLabeled.sql");

        // The readable-codegen alignment is preserved for the newline that
        // sits OUTSIDE the literal: "from products" aligns under the opening
        // verbatim quote (column 36).
        Assert.Contains("\n" + new string(' ', 36) + "from products", src);
    }

    [Fact]
    public void EscapedQuoteInsideLiteral_DoesNotConfuseIndentGuard()
    {
        // 'it''s\nfine' — the '' escape must not be misread as literal-close,
        // which would re-enable padding for the newline still inside it.
        string sql = "-- @type labeled text\nselect product_name || 'it''s\nfine' as labeled\nfrom products";
        string src = RunAndCollectSources(sql, "db/Products/GetEscaped.sql");

        string padded = "it''s\n" + new string(' ', 36) + "fine";
        Assert.DoesNotContain(padded, src);
        Assert.Contains("'it''s\nfine'", src);
        Assert.Contains("\n" + new string(' ', 36) + "from products", src);
    }
}
