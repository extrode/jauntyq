namespace Conduit.Differential.Tests;

/// <summary>
/// One observed difference between two engines running the same query with
/// the same arguments. Either an allow-list entry covers it or the suite fails.
/// </summary>
public sealed record Divergence(
    string Query,
    string ArgumentSet,
    string PivotEngine,
    string OtherEngine,
    string Difference)
{
    public override string ToString() =>
        $"{Query} [{ArgumentSet}]: {PivotEngine} vs {OtherEngine} -- {Difference}";
}

/// <summary>
/// What one engine did with one case: rows, or the error it raised.
///
/// An error is an outcome, not a harness failure. SQL Server rejecting
/// FETCH NEXT 0 ROWS where SQLite accepts LIMIT 0 is precisely the kind of
/// difference this suite exists to surface, and it can only be reported if
/// the throw is captured rather than allowed to abort the run.
/// </summary>
public sealed record EngineOutcome(
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Rows,
    string? Error)
{
    public bool Threw => Error is not null;

    public static EngineOutcome Of(Func<IReadOnlyList<IReadOnlyDictionary<string, object?>>> run)
    {
        try
        {
            return new EngineOutcome(run(), null);
        }
        catch (QueryDispatcher.DispatchException ex)
        {
            return new EngineOutcome(null, ex.Message);
        }
    }
}

public static class ResultComparer
{
    /// <summary>
    /// Compares two outcomes. Both throwing counts as agreement: the engines
    /// concur that the case is invalid, and the wording of two providers'
    /// error messages is not a claim JauntyQ makes.
    /// </summary>
    public static Divergence? Compare(
        string query,
        string argumentSet,
        string pivotEngine,
        EngineOutcome pivot,
        string otherEngine,
        EngineOutcome other,
        bool ordered)
    {
        if (pivot.Threw && other.Threw)
            return null;

        if (pivot.Threw)
            return new Divergence(query, argumentSet, pivotEngine, otherEngine,
                $"{pivotEngine} raised an error where {otherEngine} returned {other.Rows!.Count} row(s): {pivot.Error}");

        if (other.Threw)
            return new Divergence(query, argumentSet, pivotEngine, otherEngine,
                $"{otherEngine} raised an error where {pivotEngine} returned {pivot.Rows!.Count} row(s): {other.Error}");

        return Compare(query, argumentSet, pivotEngine, pivot.Rows!, otherEngine, other.Rows!, ordered);
    }

    /// <summary>
    /// Compares one engine's rows against the pivot's. Returns null when they
    /// agree. Column sets are compared first: a differing set is reported as
    /// such rather than surfacing as a value mismatch on a missing key.
    /// </summary>
    public static Divergence? Compare(
        string query,
        string argumentSet,
        string pivotEngine,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> pivot,
        string otherEngine,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> other,
        bool ordered)
    {
        string? columnDiff = CompareColumns(pivot, other);
        if (columnDiff is not null)
            return new Divergence(query, argumentSet, pivotEngine, otherEngine, columnDiff);

        if (pivot.Count != other.Count)
            return new Divergence(query, argumentSet, pivotEngine, otherEngine,
                $"row count {pivot.Count} vs {other.Count}");

        var left = pivot.Select(RowKey).ToList();
        var right = other.Select(RowKey).ToList();

        if (ordered)
        {
            for (int i = 0; i < left.Count; i++)
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return new Divergence(query, argumentSet, pivotEngine, otherEngine,
                        $"row {i} differs: {left[i]} vs {right[i]}");
            return null;
        }

        var remaining = new List<string>(right);
        foreach (string row in left)
        {
            int at = remaining.IndexOf(row);
            if (at < 0)
                return new Divergence(query, argumentSet, pivotEngine, otherEngine,
                    $"row present only in {pivotEngine}: {row}");
            remaining.RemoveAt(at);
        }

        if (remaining.Count > 0)
            return new Divergence(query, argumentSet, pivotEngine, otherEngine,
                $"row present only in {otherEngine}: {remaining[0]}");

        return null;
    }

    private static string? CompareColumns(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> pivot,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> other)
    {
        if (pivot.Count == 0 || other.Count == 0)
            return null;

        var left = pivot[0].Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var right = other[0].Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        if (left.SequenceEqual(right, StringComparer.Ordinal))
            return null;

        return $"column set [{string.Join(", ", left)}] vs [{string.Join(", ", right)}]";
    }

    private static string RowKey(IReadOnlyDictionary<string, object?> row) =>
        RowNormalizer.RenderRow(RowNormalizer.NormalizeRow(row));
}
