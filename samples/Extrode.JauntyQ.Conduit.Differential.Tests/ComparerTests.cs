using Xunit;

namespace Conduit.Differential.Tests;

[Trait("Category", "Differential")]
public class ComparerTests
{
    private static IReadOnlyDictionary<string, object?> Row(params (string, object?)[] cells)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in cells)
            row[name] = value;
        return row;
    }

    private static Divergence? Compare(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> pivot,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> other,
        bool ordered) =>
        ResultComparer.Compare("Articles/GetFiltered", "no filters", "Sqlite", pivot, "Postgres", other, ordered);

    [Fact]
    public void IdenticalRowsAgree()
    {
        var rows = new[] { Row(("id", 1), ("name", "a")) };

        Assert.Null(Compare(rows, rows, ordered: true));
    }

    [Fact]
    public void EmptyOnBothSidesAgrees()
    {
        Assert.Null(Compare(Array.Empty<IReadOnlyDictionary<string, object?>>(),
            Array.Empty<IReadOnlyDictionary<string, object?>>(), ordered: true));
    }

    [Fact]
    public void AnIntAndALongOfTheSameValueAgree()
    {
        var pivot = new[] { Row(("total", 3)) };
        var other = new[] { Row(("total", 3L)) };

        Assert.Null(Compare(pivot, other, ordered: true));
    }

    [Fact]
    public void SwappedRowsAgreeWhenTheQueryHasNoOrderBy()
    {
        var pivot = new[] { Row(("id", 1)), Row(("id", 2)) };
        var other = new[] { Row(("id", 2)), Row(("id", 1)) };

        Assert.Null(Compare(pivot, other, ordered: false));
    }

    [Fact]
    public void SwappedRowsDivergeWhenTheQueryOrders()
    {
        var pivot = new[] { Row(("id", 1)), Row(("id", 2)) };
        var other = new[] { Row(("id", 2)), Row(("id", 1)) };

        var divergence = Compare(pivot, other, ordered: true);

        Assert.NotNull(divergence);
        Assert.Contains("row 0 differs", divergence!.Difference);
    }

    [Fact]
    public void ADifferingRowCountIsReportedAsSuch()
    {
        var pivot = new[] { Row(("id", 1)), Row(("id", 2)) };
        var other = new[] { Row(("id", 1)) };

        var divergence = Compare(pivot, other, ordered: false);

        Assert.NotNull(divergence);
        Assert.Equal("row count 2 vs 1", divergence!.Difference);
    }

    [Fact]
    public void ARowOnlyOneEngineReturnedNamesThatEngine()
    {
        var pivot = new[] { Row(("id", 1)) };
        var other = new[] { Row(("id", 2)) };

        var divergence = Compare(pivot, other, ordered: false);

        Assert.NotNull(divergence);
        Assert.Contains("row present only in Sqlite", divergence!.Difference);
        Assert.Contains("id=1", divergence.Difference);
    }

    [Fact]
    public void ADifferingColumnSetIsReportedBeforeAnyValue()
    {
        var pivot = new[] { Row(("id", 1), ("name", "a")) };
        var other = new[] { Row(("id", 1), ("title", "a")) };

        var divergence = Compare(pivot, other, ordered: true);

        Assert.NotNull(divergence);
        Assert.Contains("column set", divergence!.Difference);
        Assert.Contains("name", divergence.Difference);
        Assert.Contains("title", divergence.Difference);
    }

    [Fact]
    public void ANullWhereTheOtherEngineHadAValueDiverges()
    {
        var pivot = new[] { Row(("image", null)) };
        var other = new[] { Row(("image", "http://x")) };

        Assert.NotNull(Compare(pivot, other, ordered: true));
    }

    [Fact]
    public void DuplicateRowsAreAMultisetNotASet()
    {
        var pivot = new[] { Row(("id", 1)), Row(("id", 1)) };
        var other = new[] { Row(("id", 1)), Row(("id", 2)) };

        var divergence = Compare(pivot, other, ordered: false);

        Assert.NotNull(divergence);
    }

    private static Divergence? CompareOutcomes(EngineOutcome pivot, EngineOutcome other) =>
        ResultComparer.Compare("Articles/GetFiltered", "take zero", "Sqlite", pivot, "SqlServer", other, ordered: true);

    private static EngineOutcome Rows(params IReadOnlyDictionary<string, object?>[] rows) =>
        new(rows, null);

    private static EngineOutcome Threw(string message) => new(null, message);

    [Fact]
    public void BothEnginesThrowingIsAgreementNotADivergence()
    {
        Assert.Null(CompareOutcomes(Threw("syntax error"), Threw("something else entirely")));
    }

    [Fact]
    public void OneEngineThrowingWhereTheOtherReturnedRowsIsADivergence()
    {
        var divergence = CompareOutcomes(Rows(Row(("id", 1))), Threw("FETCH clause must be greater then zero"));

        Assert.NotNull(divergence);
        Assert.Contains("SqlServer raised an error where Sqlite returned 1 row(s)", divergence!.Difference);
        Assert.Contains("FETCH clause", divergence.Difference);
    }

    [Fact]
    public void ThePivotThrowingIsReportedFromThePivotsSide()
    {
        var divergence = CompareOutcomes(Threw("no such function"), Rows(Row(("id", 1))));

        Assert.NotNull(divergence);
        Assert.Contains("Sqlite raised an error where SqlServer returned 1 row(s)", divergence!.Difference);
    }

    [Fact]
    public void AnEngineThrowingWhereTheOtherReturnedNothingIsStillADivergence()
    {
        var divergence = CompareOutcomes(Rows(), Threw("FETCH clause must be greater then zero"));

        Assert.NotNull(divergence);
        Assert.Contains("returned 0 row(s)", divergence!.Difference);
    }

    [Fact]
    public void TwoSucceedingOutcomesCompareByTheirRows()
    {
        Assert.Null(CompareOutcomes(Rows(Row(("id", 1))), Rows(Row(("id", 1L)))));
        Assert.NotNull(CompareOutcomes(Rows(Row(("id", 1))), Rows(Row(("id", 2)))));
    }

    [Fact]
    public void OutcomeOfCapturesADispatchExceptionRatherThanLettingItEscape()
    {
        var outcome = EngineOutcome.Of(() => throw new QueryDispatcher.DispatchException("boom"));

        Assert.True(outcome.Threw);
        Assert.Equal("boom", outcome.Error);
        Assert.Null(outcome.Rows);
    }

    [Fact]
    public void OutcomeOfPassesRowsThroughWhenNothingThrows()
    {
        var outcome = EngineOutcome.Of(() => new[] { Row(("id", 1)) });

        Assert.False(outcome.Threw);
        Assert.Single(outcome.Rows!);
    }

    [Fact]
    public void TheDivergenceCarriesTheQueryArgumentSetAndBothEngines()
    {
        var divergence = Compare(new[] { Row(("id", 1)) }, Array.Empty<IReadOnlyDictionary<string, object?>>(), ordered: true);

        Assert.NotNull(divergence);
        Assert.Equal("Articles/GetFiltered", divergence!.Query);
        Assert.Equal("no filters", divergence.ArgumentSet);
        Assert.Equal("Sqlite", divergence.PivotEngine);
        Assert.Equal("Postgres", divergence.OtherEngine);
        Assert.Contains("Articles/GetFiltered [no filters]: Sqlite vs Postgres", divergence.ToString());
    }
}
