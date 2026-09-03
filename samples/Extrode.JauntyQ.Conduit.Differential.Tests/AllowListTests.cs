using Xunit;

namespace Conduit.Differential.Tests;

[Trait("Category", "Differential")]
public class AllowListTests
{
    private static AllowedDivergence Entry(
        string query = "Articles/GetFiltered",
        string? reason = "postgres sorts nulls first under a descending order by",
        string difference = "row 0 differs",
        params string[] engines) =>
        new()
        {
            Query = query,
            Engines = engines.Length == 0 ? new List<string> { "Sqlite", "Postgres" } : engines.ToList(),
            Difference = difference,
            Reason = reason ?? "",
        };

    private static Divergence Observed(
        string query = "Articles/GetFiltered",
        string pivot = "Sqlite",
        string other = "Postgres",
        string difference = "row 0 differs: {id=1} vs {id=2}") =>
        new(query, "no filters", pivot, other, difference);

    [Fact]
    public void AnEmptyAllowListIsValid()
    {
        Assert.Empty(new AllowList(Array.Empty<AllowedDivergence>()).Validate());
    }

    [Fact]
    public void TheShippedAllowListParsesAndValidates()
    {
        var list = AllowList.Load(AllowList.DefaultPath);

        Assert.Empty(list.Validate());
    }

    [Fact]
    public void AnEntryWithoutAReasonIsAProblem()
    {
        var problems = new AllowList(new[] { Entry(reason: null) }).Validate();

        Assert.Contains(problems, p => p.Contains("no reason given"));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void ABlankReasonIsAlsoAProblem(string reason)
    {
        var problems = new AllowList(new[] { Entry(reason: reason) }).Validate();

        Assert.Contains(problems, p => p.Contains("no reason given"));
    }

    [Fact]
    public void AnEntryNamingOneEngineIsAProblem()
    {
        var problems = new AllowList(new[] { Entry(engines: "Sqlite") }).Validate();

        Assert.Contains(problems, p => p.Contains("a divergence is between two"));
    }

    [Fact]
    public void AnEntryWithNoQueryOrDifferenceIsAProblem()
    {
        var problems = new AllowList(new[] { Entry(query: "", difference: "") }).Validate();

        Assert.Contains(problems, p => p.Contains("no query named"));
        Assert.Contains(problems, p => p.Contains("no difference described"));
    }

    [Fact]
    public void AMatchingEntryAcceptsTheDivergence()
    {
        Assert.True(new AllowList(new[] { Entry() }).Accepts(Observed()));
    }

    [Fact]
    public void TheEnginePairMatchesInEitherDirection()
    {
        var list = new AllowList(new[] { Entry(engines: new[] { "Sqlite", "Postgres" }) });

        Assert.True(list.Accepts(Observed(pivot: "Postgres", other: "Sqlite")));
    }

    [Fact]
    public void AnEntryForAnotherQueryDoesNotAccept()
    {
        Assert.False(new AllowList(new[] { Entry(query: "Tags/GetAll") }).Accepts(Observed()));
    }

    [Fact]
    public void AnEntryForAnotherEnginePairDoesNotAccept()
    {
        var list = new AllowList(new[] { Entry(engines: new[] { "MySql", "MariaDb" }) });

        Assert.False(list.Accepts(Observed()));
    }

    [Fact]
    public void AnEntryDescribingAnotherDifferenceDoesNotAccept()
    {
        Assert.False(new AllowList(new[] { Entry(difference: "row count") }).Accepts(Observed()));
    }

    [Fact]
    public void AnUnmatchedEntryIsDeadWhenItsQueryAndEnginesBothRan()
    {
        var list = new AllowList(new[] { Entry() });

        var dead = list.DeadEntries(
            enginesRun: new[] { "Sqlite", "Postgres" },
            queriesRun: new[] { "Articles/GetFiltered" });

        Assert.Single(dead);
    }

    [Fact]
    public void AMatchedEntryIsNotDead()
    {
        var list = new AllowList(new[] { Entry() });
        list.Accepts(Observed());

        var dead = list.DeadEntries(
            enginesRun: new[] { "Sqlite", "Postgres" },
            queriesRun: new[] { "Articles/GetFiltered" });

        Assert.Empty(dead);
    }

    [Fact]
    public void AnEntryWhoseEngineWasSkippedIsNotDeadItWasUntested()
    {
        var list = new AllowList(new[] { Entry() });

        var dead = list.DeadEntries(
            enginesRun: new[] { "Sqlite" },
            queriesRun: new[] { "Articles/GetFiltered" });

        Assert.Empty(dead);
    }

    [Fact]
    public void AnEntryWhoseQueryNeverRanIsNotDead()
    {
        var list = new AllowList(new[] { Entry() });

        var dead = list.DeadEntries(
            enginesRun: new[] { "Sqlite", "Postgres" },
            queriesRun: new[] { "Tags/GetAll" });

        Assert.Empty(dead);
    }

    [Fact]
    public void ParseReadsTheFieldNamesTheFileUses()
    {
        var entries = AllowList.Parse("""
            [
              {
                "query": "Tags/GetAll",
                "engines": ["Sqlite", "SqlServer"],
                "difference": "row 0 differs",
                "reason": "case-insensitive default collation on sql server"
              }
            ]
            """);

        var entry = Assert.Single(entries);
        Assert.Equal("Tags/GetAll", entry.Query);
        Assert.Equal(new[] { "Sqlite", "SqlServer" }, entry.Engines);
        Assert.Equal("row 0 differs", entry.Difference);
        Assert.Equal("case-insensitive default collation on sql server", entry.Reason);
    }

    [Fact]
    public void ParsingNullThrowsRatherThanYieldingNoEntries()
    {
        Assert.Throws<InvalidOperationException>(() => AllowList.Parse("null"));
    }
}
