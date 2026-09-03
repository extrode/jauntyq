using BenchmarkDotNet.Attributes;
using Extrode.JauntyQ.Generated;
using Microsoft.Data.Sqlite;

namespace Extrode.JauntyQ.Benchmarks;

/// <summary>
/// Buffered (List&lt;T&gt;) vs streamed (@stream / IEnumerable&lt;T&gt;) reads.
/// The full-consume cases show the allocation difference; the early-exit case
/// shows streaming's headline win — stopping after N rows never materializes
/// the rest, while the buffered path must read the entire result set first.
/// </summary>
[MemoryDiagnoser]
public class StreamBenchmarks
{
    private SqliteConnection _conn = null!;

    [Params(1000, 100_000)]
    public int RowCount;

    [GlobalSetup]
    public void Setup()
    {
        BenchDb.Init(RowCount);
        _conn = BenchDb.Open();
    }

    [GlobalCleanup]
    public void Cleanup() => _conn.Dispose();

    /// <summary>Buffered: GetAll materializes the whole List, then we sum.</summary>
    [Benchmark(Baseline = true)]
    public decimal Buffered_ConsumeAll()
    {
        decimal total = 0m;
        foreach (var w in Widgets.GetAll(_conn))
            total += w.Price;
        return total;
    }

    /// <summary>Streamed: rows flow one at a time, nothing buffered.</summary>
    [Benchmark]
    public decimal Streamed_ConsumeAll()
    {
        decimal total = 0m;
        foreach (var w in Widgets.StreamAll(_conn))
            total += w.Price;
        return total;
    }

    /// <summary>Buffered early-exit: still reads and allocates ALL rows first.</summary>
    [Benchmark]
    public decimal Buffered_First100()
    {
        decimal total = 0m;
        int n = 0;
        foreach (var w in Widgets.GetAll(_conn))
        {
            total += w.Price;
            if (++n == 100) break;
        }
        return total;
    }

    /// <summary>Streamed early-exit: reads only ~100 rows, then disposes.</summary>
    [Benchmark]
    public decimal Streamed_First100()
    {
        decimal total = 0m;
        int n = 0;
        foreach (var w in Widgets.StreamAll(_conn))
        {
            total += w.Price;
            if (++n == 100) break;
        }
        return total;
    }
}
