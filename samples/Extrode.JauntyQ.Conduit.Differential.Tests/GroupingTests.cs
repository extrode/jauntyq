using Xunit;

namespace Conduit.Differential.Tests;

[Trait("Category", "Differential")]
public class GroupingTests
{
    private static readonly string[] Three = ["Sqlite", "Postgres", "MySql"];

    private static Dictionary<string, string> Results(string sqlite, string postgres, string mysql) =>
        new(StringComparer.Ordinal)
        {
            ["Sqlite"] = sqlite,
            ["Postgres"] = postgres,
            ["MySql"] = mysql,
        };

    [Fact]
    public void EnginesReturningTheSameResultLandInOneGroup()
    {
        var observed = GroupingComparer.Observe(Results("a", "a", "a"));

        Assert.Single(observed);
        Assert.Equal(["MySql", "Postgres", "Sqlite"], observed[0]);
    }

    [Fact]
    public void EnginesReturningDifferentResultsLandInSeparateGroups()
    {
        var observed = GroupingComparer.Observe(Results("a", "b", "a"));

        Assert.Equal(2, observed.Count);
    }

    [Fact]
    public void AnObservationMatchingTheDeclarationPasses()
    {
        var observed = GroupingComparer.Observe(Results("a", "b", "a"));

        var mismatches = GroupingComparer.Compare(
            "case", [["Sqlite", "MySql"], ["Postgres"]], observed, Three);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void AnEngineThatMovedBetweenGroupsIsNamed()
    {
        var observed = GroupingComparer.Observe(Results("a", "a", "a"));

        var mismatches = GroupingComparer.Compare(
            "case", [["Sqlite", "MySql"], ["Postgres"]], observed, Three);

        Assert.NotEmpty(mismatches);
        Assert.Contains(mismatches, m => m.Engine == "Postgres");
    }

    [Fact]
    public void UnexpectedAgreementFailsJustAsUnexpectedDivergenceDoes()
    {
        var agreed = GroupingComparer.Compare(
            "case", [["Sqlite"], ["Postgres"], ["MySql"]],
            GroupingComparer.Observe(Results("a", "a", "a")), Three);

        var diverged = GroupingComparer.Compare(
            "case", [["Sqlite", "Postgres", "MySql"]],
            GroupingComparer.Observe(Results("a", "b", "c")), Three);

        Assert.NotEmpty(agreed);
        Assert.NotEmpty(diverged);
    }

    [Fact]
    public void AnUnavailableEngineIsDroppedFromBothSides()
    {
        var observed = GroupingComparer.Observe(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sqlite"] = "a",
            ["Postgres"] = "b",
        });

        var mismatches = GroupingComparer.Compare(
            "case", [["Sqlite", "MySql"], ["Postgres"]], observed, ["Sqlite", "Postgres"]);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void TwoDeclaredGroupsThatMergeInObservationFail()
    {
        var observed = GroupingComparer.Observe(Results("a", "a", "b"));

        var mismatches = GroupingComparer.Compare(
            "case", [["Sqlite"], ["Postgres"], ["MySql"]], observed, Three);

        Assert.Contains(mismatches, m => m.Engine == "Sqlite" && m.Expected == "alone");
    }

    [Fact]
    public void TheMessageNamesTheCaseTheEngineAndBothSides()
    {
        var observed = GroupingComparer.Observe(Results("a", "a", "b"));

        var mismatch = GroupingComparer.Compare(
            "collation/equality-ignores-case", [["Sqlite"], ["Postgres"], ["MySql"]], observed, Three)[0];

        Assert.Contains("collation/equality-ignores-case", mismatch.ToString());
        Assert.Contains("Postgres", mismatch.ToString());
        Assert.Contains("alone", mismatch.ToString());
    }
}
