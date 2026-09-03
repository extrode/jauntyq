using BenchmarkDotNet.Attributes;
using Extrode.JauntyQ.SqlParser;

namespace Extrode.JauntyQ.Benchmarks;

/// <summary>
/// Build-time hot path: the generator re-tokenizes .sql files on every relevant
/// design-time build (per keystroke in the IDE). Measures allocations/throughput
/// of the tokenizer after the PERF-2/PERF-5 allocation fixes (no throwaway
/// Substring lookahead, interned single-char symbols).
/// </summary>
[MemoryDiagnoser]
public class TokenizerBenchmarks
{
    // A representative multi-clause query with symbols, params, and identifiers.
    private const string Sql = @"
        select w.WidgetId, w.Name, w.Price, w.Quantity, w.Active
        from Widgets w
        inner join Categories c on c.CategoryId = w.CategoryId
        where w.Price >= @minPrice and w.Active <> 0 and w.Quantity <= @maxQty
        order by w.Price desc";

    [Benchmark]
    public int Tokenize_OneQuery()
    {
        var tokens = SqlTokenizer.Tokenize(Sql);
        return tokens.Count;
    }

    [Benchmark]
    public int Tokenize_100Queries()
    {
        int total = 0;
        for (int i = 0; i < 100; i++)
            total += SqlTokenizer.Tokenize(Sql).Count;
        return total;
    }
}
