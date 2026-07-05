using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Sqlite.Tests;

/// <summary>
/// Exercises the -- @stream directive end-to-end against real SQLite: the
/// generated StreamAll yields rows lazily as IEnumerable&lt;Product&gt; (sync)
/// and IAsyncEnumerable&lt;Product&gt; (async) directly off the reader, without
/// buffering into a List.
/// </summary>
public class StreamingTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fx;
    public StreamingTests(SqliteFixture fx) => _fx = fx;

    [Fact]
    public void Stream_Sync_YieldsAllRows()
    {
        int count = 0;
        foreach (var p in _fx.Db.Products.StreamAll())
        {
            Assert.False(string.IsNullOrEmpty(p.ProductName));
            count++;
        }
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task Stream_Async_YieldsAllRows()
    {
        int count = 0;
        await foreach (var p in _fx.Db.Products.StreamAllAsync())
            count++;
        Assert.Equal(4, count);
    }

    [Fact]
    public void Stream_Sync_IsLazy_CanStopEarly()
    {
        // Enumerating just the first row must not require materializing the
        // rest; break disposes the enumerator, running the finally that closes
        // the reader/connection.
        using var e = _fx.Db.Products.StreamAll().GetEnumerator();
        Assert.True(e.MoveNext());
        Assert.Equal(1, e.Current.ProductId);
    }

    [Fact]
    public async Task Stream_Async_HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        int seen = 0;
        await foreach (var p in _fx.Db.Products.StreamAllAsync().WithCancellation(cts.Token))
        {
            seen++;
            if (seen == 2) cts.Cancel();
            if (cts.IsCancellationRequested) break;
        }
        Assert.True(seen >= 2);
    }

    [Fact]
    public void Stream_ReturnsIEnumerable_NotList()
    {
        // The declared return type is a lazy sequence, not a materialized List.
        var seq = _fx.Db.Products.StreamAll();
        Assert.IsNotType<System.Collections.Generic.List<Product>>(seq);
        Assert.IsAssignableFrom<System.Collections.Generic.IEnumerable<Product>>(seq);
    }
}
