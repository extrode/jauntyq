using Xunit;

namespace JauntyQ.Conduit.Differential.Tests;

[Trait("Category", "Differential")]
public class StressCorpusTests
{
    private static StressCase Case(
        IReadOnlyList<IReadOnlyList<string>>? groups = null,
        string reason = "reason",
        string guidance = "guidance",
        string sql = "SELECT 1",
        string name = "case",
        Dictionary<string, string>? overrides = null) =>
        new()
        {
            Name = name,
            Category = "test",
            Sql = sql,
            SqlByEngine = overrides ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Groups = groups ?? [["Sqlite", "Postgres", "MySql", "MariaDb"], ["SqlServer"]],
            Reason = reason,
            Guidance = guidance,
        };

    [Fact]
    public void AWellFormedCaseRaisesNothing()
    {
        Assert.Empty(StressCorpus.Validate([Case()]));
    }

    [Fact]
    public void AGroupingWithNoReasonIsRejected()
    {
        Assert.Contains(StressCorpus.Validate([Case(reason: "  ")]), p => p.Contains("no reason"));
    }

    [Fact]
    public void ACaseWithNoConsumerGuidanceIsRejected()
    {
        Assert.Contains(StressCorpus.Validate([Case(guidance: "")]), p => p.Contains("no consumer guidance"));
    }

    [Fact]
    public void OneGroupHoldingEveryEngineIsRejected()
    {
        var problems = StressCorpus.Validate(
            [Case(groups: [["Sqlite", "Postgres", "MySql", "MariaDb", "SqlServer"]])]);

        Assert.Contains(problems, p => p.Contains("does not diverge"));
    }

    [Fact]
    public void AnUnmeasuredCaseIsRejected()
    {
        Assert.Contains(StressCorpus.Validate([Case(groups: [])]), p => p.Contains("no grouping declared"));
    }

    [Fact]
    public void AnEngineInNoGroupIsRejected()
    {
        var problems = StressCorpus.Validate([Case(groups: [["Sqlite"], ["Postgres"]])]);

        Assert.Contains(problems, p => p.Contains("'MySql' is in no group"));
    }

    [Fact]
    public void AnEngineInTwoGroupsIsRejected()
    {
        var problems = StressCorpus.Validate(
            [Case(groups: [["Sqlite", "Postgres", "MySql", "MariaDb"], ["SqlServer", "Sqlite"]])]);

        Assert.Contains(problems, p => p.Contains("more than one group"));
    }

    [Fact]
    public void AnUnknownEngineNameIsRejected()
    {
        var problems = StressCorpus.Validate(
            [Case(groups: [["Sqlite", "Postgres", "MySql", "MariaDb"], ["SqlServer"], ["Oracle"]])]);

        Assert.Contains(problems, p => p.Contains("'Oracle' is not one of the engines"));
    }

    [Fact]
    public void AnOverrideForAnUnknownEngineIsRejected()
    {
        var problems = StressCorpus.Validate([Case(overrides: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Oracle"] = "SELECT 1 FROM dual",
        })]);

        Assert.Contains(problems, p => p.Contains("SQL override for 'Oracle'"));
    }

    [Fact]
    public void ACaseWithNoSqlIsRejected()
    {
        Assert.Contains(StressCorpus.Validate([Case(sql: "")]), p => p.Contains("no SQL"));
    }

    [Fact]
    public void TwoCasesWithTheSameNameAreRejected()
    {
        Assert.Contains(StressCorpus.Validate([Case(), Case()]), p => p.Contains("duplicate case name"));
    }

    [Fact]
    public void EveryCategoryNamedInTheSpecIsCovered()
    {
        var categories = StressCorpus.Cases.Select(c => c.Category).Distinct(StringComparer.Ordinal).ToList();

        Assert.Equal(7, categories.Count);
    }

    [Fact]
    public void EveryOverrideNamesAnEngineTheSuiteRuns()
    {
        foreach (StressCase c in StressCorpus.Cases)
            foreach (string engine in c.SqlByEngine.Keys)
                Assert.Contains(engine, StressCorpus.KnownEngines);
    }
}
