using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class PredicateDriftMutationCoverageTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""bookmarks"": {
      ""name"": ""bookmarks"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""bigint"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""bigint"", ""isNullable"": false },
        ""views"": { ""name"": ""views"", ""dbType"": ""integer"", ""isNullable"": false },
        ""status"": { ""name"": ""status"", ""dbType"": ""integer"", ""isNullable"": false },
        ""deleted_at"": { ""name"": ""deleted_at"", ""dbType"": ""timestamp"", ""isNullable"": true },
        ""created_at"": { ""name"": ""created_at"", ""dbType"": ""timestamp"", ""isNullable"": false }
      },
      ""indexes"": []
    },
    ""bookmark_tags"": {
      ""name"": ""bookmark_tags"",
      ""columns"": {
        ""bookmark_id"": { ""name"": ""bookmark_id"", ""dbType"": ""bigint"", ""isNullable"": false },
        ""tag_name"": { ""name"": ""tag_name"", ""dbType"": ""text"", ""isNullable"": false }
      },
      ""indexes"": []
    },
    ""notes"": {
      ""name"": ""notes"",
      ""columns"": {
        ""bookmark_id"": { ""name"": ""bookmark_id"", ""dbType"": ""bigint"", ""isNullable"": false },
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""bigint"", ""isNullable"": false }
      },
      ""indexes"": []
    }
  },
  ""foreignKeys"": []
}";

    private const string FlatList =
        "select bookmarks.id\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n";

    private const string CteList =
        "-- @params userId:long, since:DateTime\n" +
        "with page as (\n" +
        "    select bookmarks.id, bookmarks.created_at\n" +
        "    from bookmarks\n" +
        "    where bookmarks.user_id = @userId\n" +
        ")\n" +
        "select page.id\nfrom page\nwhere page.created_at > @since\n";

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("PredicateDriftMutationTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText> { new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson) };
        foreach (var (path, text) in files)
            texts.Add(new InMemoryAdditionalText(path, text));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result = driver.GetRunResult();
        Assert.Null(result.Results[0].Exception);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT0001");
        return result;
    }

    private static string Single(GeneratorDriverRunResult result, string id) =>
        Assert.Single(result.Diagnostics, d => d.Id == id).GetMessage();

    private static void AssertSilent(GeneratorDriverRunResult result) =>
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8011" || d.Id == "JNT3010");

    [Fact]
    public void UnknownTarget_ExactMessage()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql", FlatList),
            ("db/Bookmarks/CountForUser.sql", "-- @mirrors ListPageTypo\n" + FlatList));

        Assert.Equal(
            "Bookmarks.CountForUser declares -- @mirrors ListPageTypo, but no query by that name was compiled. " +
            "Check the spelling, and that the file is in AdditionalFiles. The two filters were not compared.",
            Single(result, "JNT3010"));
    }

    [Fact]
    public void AmbiguousTarget_NamesEveryMatchSortedAndCommaSeparated()
    {
        var result = Run(
            ("db/Public/ListPage.sql", FlatList),
            ("db/Bookmarks/ListPage.sql", FlatList),
            ("db/Bookmarks/CountForUser.sql", "-- @mirrors ListPage\n" + FlatList));

        Assert.Equal(
            "Bookmarks.CountForUser declares -- @mirrors ListPage, but that name is ambiguous — it matches " +
            "Bookmarks.ListPage, Public.ListPage. Qualify it as Entity.Method. The two filters were not compared.",
            Single(result, "JNT3010"));
    }

    [Fact]
    public void DeclaringQueryWithoutAModel_IsSkipped()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql", FlatList),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\nselect nosuchtable.id\nfrom nosuchtable\nwhere nosuchtable.user_id = @userId\n"));

        AssertSilent(result);
    }

    [Fact]
    public void SourceUnresolved_NamesTheSource()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql", FlatList),
            ("db/Bookmarks/CountForUser.sql", "-- @mirrors ListPage\n" + CteList));

        Assert.Equal(
            "Bookmarks.CountForUser declares -- @mirrors ListPage, but Bookmarks.CountForUser references a " +
            "WHERE-clause column that does not resolve to a schema column, so the two filters cannot be compared. " +
            "Qualify the column with its table, or remove the directive.",
            Single(result, "JNT3010"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8011");
    }

    [Fact]
    public void BothUnresolved_NamesBothQueries()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql", CteList),
            ("db/Bookmarks/CountForUser.sql", "-- @mirrors ListPage\n" + CteList));

        Assert.Equal(
            "Bookmarks.CountForUser declares -- @mirrors ListPage, but both queries reference a " +
            "WHERE-clause column that does not resolve to a schema column, so the two filters cannot be compared. " +
            "Qualify the column with its table, or remove the directive.",
            Single(result, "JNT3010"));
    }

    [Fact]
    public void DriftOnBothSides_ExactMessage()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\n" +
                "where bookmarks.user_id = @userId and bookmarks.deleted_at is null and bookmarks.status = 1\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\nselect count(*) as total\nfrom bookmarks\n" +
                "where bookmarks.user_id = @userId and bookmarks.views > 10\n"));

        Assert.Equal(
            "Bookmarks.CountForUser declares -- @mirrors ListPage but their WHERE clauses differ (only in " +
            "CountForUser: [bookmarks.views > 10]; only in ListPage: [bookmarks.deleted_at IS NULL], " +
            "[bookmarks.status = 1]). Two queries declared to filter alike must filter alike: a count that filters " +
            "differently from the list it sizes returns a total the page contents contradict.",
            Single(result, "JNT8011"));
    }

    [Fact]
    public void RepeatedAtom_CountsAsADifference()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql", FlatList),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\nselect count(*) as total\nfrom bookmarks\n" +
                "where bookmarks.user_id = @userId and bookmarks.user_id = @userId\n"));

        Assert.Contains("(only in CountForUser: [bookmarks.user_id = @userId])", Single(result, "JNT8011"));
    }

    [Fact]
    public void SeveralDeclaringQueries_ReportedInNameOrder()
    {
        const string drifting = "-- @mirrors ListPage\nselect count(*) as total\nfrom bookmarks\nwhere bookmarks.status = 1\n";
        var result = Run(
            ("db/Bookmarks/ZCount.sql", drifting),
            ("db/Bookmarks/ListPage.sql", FlatList),
            ("db/Bookmarks/ACount.sql", drifting));

        var messages = result.Diagnostics.Where(d => d.Id == "JNT8011").Select(d => d.GetMessage()).ToList();
        Assert.Equal(2, messages.Count);
        Assert.StartsWith("Bookmarks.ACount ", messages[0]);
        Assert.StartsWith("Bookmarks.ZCount ", messages[1]);
    }

    [Fact]
    public void ColumnAfterAClosedParenthesis_IsResolved()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\nwhere coalesce(bookmarks.deleted_at, bookmarks.created_at) > created_at\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\nselect count(*) as total\nfrom bookmarks\n" +
                "where coalesce(bookmarks.deleted_at, bookmarks.created_at) > bookmarks.created_at\n"));

        AssertSilent(result);
    }

    [Fact]
    public void ColumnAfterAnOperator_IsResolved()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql", "select bookmarks.id\nfrom bookmarks\nwhere views > status\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\nselect count(*) as total\nfrom bookmarks\nwhere bookmarks.views > bookmarks.status\n"));

        AssertSilent(result);
    }

    [Fact]
    public void SubqueryColumns_AreComparedAsWritten()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql", FlatList),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\nselect count(*) as total\nfrom bookmarks\n" +
                "where bookmarks.user_id = @userId\n" +
                "and exists (select 1 from bookmark_tags t where t.tag_name = @tag and bookmark_id = bookmarks.id)\n" +
                "-- @params userId:long, tag:string\n"));

        Assert.Equal(
            "Bookmarks.CountForUser declares -- @mirrors ListPage but their WHERE clauses differ (only in " +
            "CountForUser: [EXISTS ( SELECT 1 FROM bookmark_tags t WHERE t.tag_name = @tag AND bookmark_id = " +
            "bookmarks.id )]). Two queries declared to filter alike must filter alike: a count that filters " +
            "differently from the list it sizes returns a total the page contents contradict.",
            Single(result, "JNT8011"));
    }

    [Fact]
    public void AmbiguousUnqualifiedWhereColumn_IsUnresolved()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\njoin notes on notes.bookmark_id = bookmarks.id\nwhere user_id = @u\n-- @params u:long\n"),
            ("db/Bookmarks/CountForUser.sql", "-- @mirrors ListPage\n" + FlatList));

        Assert.Equal(
            "Bookmarks.CountForUser declares -- @mirrors ListPage, but Bookmarks.ListPage references a " +
            "WHERE-clause column that does not resolve to a schema column, so the two filters cannot be compared. " +
            "Qualify the column with its table, or remove the directive.",
            Single(result, "JNT3010"));
    }
}
