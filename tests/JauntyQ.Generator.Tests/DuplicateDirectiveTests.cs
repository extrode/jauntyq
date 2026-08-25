using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using JauntyQ.Generator.Directives;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class DuplicateDirectiveTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""articles"": {
      ""name"": ""articles"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""author_id"": { ""name"": ""author_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""created_at"": { ""name"": ""created_at"", ""dbType"": ""timestamp"", ""isNullable"": false },
        ""title"": { ""name"": ""title"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 200 }
      },
      ""indexes"": [
        { ""name"": ""ix_articles_author"", ""columns"": [""author_id""], ""isUnique"": false }
      ]
    }
  }
}";

    private const string UnsortedQuery =
        "select articles.id, articles.title\n" +
        "from articles\n" +
        "order by articles.created_at";

    private static GeneratorDriverRunResult Run(string sql)
    {
        var compilation = CSharpCompilation.Create("DuplicateDirectiveTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Articles/TestQuery.sql", sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Fact]
    public void SingleOccurrenceOfEach_RecordsNoDuplicate()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @first\n" +
            "-- @result ArticleSummary\n" +
            "-- @allow-sort paging keeps it to a screenful\n" +
            "select 1");

        Assert.Null(directives.DuplicateDirectives);
    }

    [Fact]
    public void Result_Twice_RecordsADuplicate()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @result ArticleSummary\n" +
            "-- @result ArticleDetail\n" +
            "select 1");

        var message = Assert.Single(directives.DuplicateDirectives!);
        Assert.Contains("-- @result ArticleSummary", message);
        Assert.Contains("-- @result ArticleDetail", message);
        Assert.Contains("discarded", message);
    }

    [Fact]
    public void Result_Twice_TheLastOneIsTheOneInForce()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @result ArticleSummary\n" +
            "-- @result ArticleDetail\n" +
            "select 1");

        Assert.Equal("ArticleDetail", directives.ResultTypeName);
    }

    [Fact]
    public void Result_VoidThenInlineColumns_DoesNotMergeTheTwo()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @result void\n" +
            "-- @result (int Id, string Title)\n" +
            "select 1");

        Assert.False(directives.ResultIsVoid);
        Assert.NotNull(directives.InlineColumns);
        Assert.Equal(2, directives.InlineColumns!.Count);
    }

    [Fact]
    public void Result_InlineColumnsThenVoid_DoesNotMergeTheTwo()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @result (int Id, string Title)\n" +
            "-- @result void\n" +
            "select 1");

        Assert.True(directives.ResultIsVoid);
        Assert.Null(directives.InlineColumns);
    }

    [Fact]
    public void Result_TwiceWithTheSameValue_RecordsTheMilderMessage()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @result ArticleSummary\n" +
            "-- @result ArticleSummary\n" +
            "select 1");

        var message = Assert.Single(directives.DuplicateDirectives!);
        Assert.Contains("the same value", message);
        Assert.DoesNotContain("discarded", message);
    }

    [Fact]
    public void Flag_Twice_RecordsADuplicate()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @first\n" +
            "-- @first\n" +
            "select 1");

        var message = Assert.Single(directives.DuplicateDirectives!);
        Assert.Contains("-- @first", message);
        Assert.Contains("the same value", message);
        Assert.True(directives.IsFirst);
    }

    [Fact]
    public void Type_Twice_RecordsNoDuplicate()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @type total int\n" +
            "-- @type average decimal\n" +
            "select 1");

        Assert.Null(directives.DuplicateDirectives);
        Assert.Equal(2, directives.TypeDirectives!.Count);
    }

    [Fact]
    public void Each_Twice_RecordsNoDuplicate()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @each ids\n" +
            "-- @each names\n" +
            "select 1");

        Assert.Null(directives.DuplicateDirectives);
        Assert.Equal(2, directives.EachParams!.Count);
    }

    [Fact]
    public void BareValueTakingLine_IsNotAnOccurrence()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @call\n" +
            "-- @call sp_get_articles\n" +
            "select 1");

        Assert.Null(directives.DuplicateDirectives);
        Assert.Equal("sp_get_articles", directives.CallProcName);
        Assert.NotNull(directives.SuspiciousDirectives);
    }

    [Fact]
    public void ThreeOccurrences_RecordOneDuplicatePerRepeat()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @mirrors ArticleList\n" +
            "-- @mirrors ArticleCount\n" +
            "-- @mirrors ArticleTotal\n" +
            "select 1");

        Assert.Equal(2, directives.DuplicateDirectives!.Count);
        Assert.Contains("-- @mirrors ArticleCount", directives.DuplicateDirectives[1]);
        Assert.Contains("-- @mirrors ArticleTotal", directives.DuplicateDirectives[1]);
        Assert.Equal("ArticleTotal", directives.MirrorsTarget);
    }

    [Fact]
    public void Params_SecondValueParsingToNothing_StillClearsTheFirst()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @params authorId:int\n" +
            "-- @params nonsense\n" +
            "select 1");

        Assert.Null(directives.ExplicitParams);
        Assert.Single(directives.DuplicateDirectives!);
    }

    [Fact]
    public void Proc_NamedThenBare_ClearsTheName()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @proc sp_articles\n" +
            "-- @proc\n" +
            "select 1");

        Assert.True(directives.IsProc);
        Assert.Null(directives.ProcName);
        Assert.Single(directives.DuplicateDirectives!);
    }

    [Fact]
    public void AllowSort_Twice_TheLastReasonIsTheOneInForce()
    {
        var (directives, _) = DirectiveParser.Parse(
            "-- @allow-sort waiting on migration 0042\n" +
            "-- @allow-sort 20 rows a page\n" +
            "select 1");

        Assert.Equal("20 rows a page", directives.AllowSortReason);
        var message = Assert.Single(directives.DuplicateDirectives!);
        Assert.Contains("waiting on migration 0042", message);
        Assert.Contains("20 rows a page", message);
    }

    [Fact]
    public void DuplicateLines_AreStillStrippedFromTheCleanedSql()
    {
        var (_, cleaned) = DirectiveParser.Parse(
            "-- @first\n" +
            "-- @first\n" +
            "select 1");

        Assert.DoesNotContain("@first", cleaned);
    }

    [Fact]
    public void Generator_ReportsJNT3011_AsAWarning()
    {
        var result = Run(
            "-- @allow-sort waiting on migration 0042\n" +
            "-- @allow-sort 20 rows a page\n" +
            UnsortedQuery);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3011");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("-- @allow-sort", diag.GetMessage());
        Assert.Contains("waiting on migration 0042", diag.GetMessage());
    }

    [Fact]
    public void Generator_TheLastDirectiveStillTakesEffect()
    {
        var result = Run(
            "-- @allow-sort waiting on migration 0042\n" +
            "-- @allow-sort 20 rows a page\n" +
            UnsortedQuery);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void Generator_WithoutADuplicate_ReportsNoJNT3011()
    {
        var result = Run("-- @allow-sort 20 rows a page\n" + UnsortedQuery);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT3011");
    }

    [Fact]
    public void Generator_RepeatableDirectiveTwice_ReportsNoJNT3011()
    {
        var result = Run(
            "-- @each ids\n" +
            "-- @each titles\n" +
            "select articles.id, articles.title\n" +
            "from articles\n" +
            "where articles.id in @ids and articles.title in @titles");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT3011");
    }

    [Fact]
    public void Generator_ReportsBothJNT3008AndJNT3011_WhenTheFileHasBoth()
    {
        var result = Run(
            "-- @frist\n" +
            "-- @allow-sort waiting on migration 0042\n" +
            "-- @allow-sort 20 rows a page\n" +
            UnsortedQuery);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3008");
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3011");
    }
}
