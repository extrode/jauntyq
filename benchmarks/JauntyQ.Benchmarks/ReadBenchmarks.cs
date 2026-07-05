using BenchmarkDotNet.Attributes;
using JauntyQ.Generated;
using Microsoft.Data.Sqlite;

namespace JauntyQ.Benchmarks;

/// <summary>
/// Generated read-path benchmarks over real SQLite. Measures the buffered
/// List&lt;T&gt; materialization the generator emits today (ordinal reads, no
/// reflection), across result-set sizes. This is the baseline that the
/// streaming path (added later) is compared against.
/// </summary>
[MemoryDiagnoser]
public class ReadBenchmarks
{
    private SqliteConnection _conn = null!;

    [Params(10, 1000, 100_000)]
    public int RowCount;

    [GlobalSetup]
    public void Setup()
    {
        BenchDb.Init(RowCount);
        _conn = BenchDb.Open();
    }

    [GlobalCleanup]
    public void Cleanup() => _conn.Dispose();

    /// <summary>Full buffered read into List&lt;Widget&gt; via the generated GetAll.</summary>
    [Benchmark]
    public int GetAll_Buffered()
    {
        var rows = Widgets.GetAll(_conn);
        return rows.Count;
    }

    /// <summary>Buffered read that sums a column — the common "read then aggregate" shape.</summary>
    [Benchmark]
    public decimal GetAll_SumPrice()
    {
        var rows = Widgets.GetAll(_conn);
        decimal total = 0m;
        foreach (var r in rows)
            total += r.Price;
        return total;
    }

    /// <summary>Single-row lookup by primary key (@first path).</summary>
    [Benchmark]
    public string GetById_First()
    {
        var w = Widgets.GetById(_conn, 1);
        return w?.Name ?? "";
    }
}
