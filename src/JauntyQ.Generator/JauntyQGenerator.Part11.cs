using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.Generator;

/// <summary>
/// Sequence comparer for the collected file summaries: lets the aggregate
/// node treat "same files, same shapes" as an unchanged input even though
/// Collect() produces a fresh array instance on every upstream change.
/// </summary>
internal sealed class FileSummaryArrayComparer : System.Collections.Generic.IEqualityComparer<ImmutableArray<FileSummary>>
{
    public static readonly FileSummaryArrayComparer Instance = new FileSummaryArrayComparer();

    private FileSummaryArrayComparer() { }

    public bool Equals(ImmutableArray<FileSummary> x, ImmutableArray<FileSummary> y)
    {
        if (x.IsDefault || y.IsDefault)
            return x.IsDefault && y.IsDefault;
        if (x.Length != y.Length)
            return false;
        for (int i = 0; i < x.Length; i++)
        {
            if (!x[i].Equals(y[i]))
                return false;
        }
        return true;
    }

    public int GetHashCode(ImmutableArray<FileSummary> array)
    {
        if (array.IsDefault)
            return 0;
        unchecked
        {
            int hash = array.Length;
            foreach (var summary in array)
                hash = hash * 31 + summary.GetHashCode();
            return hash;
        }
    }
}
