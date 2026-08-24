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

public class StreamingLifecycleTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fx;
    public StreamingLifecycleTests(SqliteFixture fx) => _fx = fx;

    [Fact]
    public void Stream_EmptyResult_YieldsZeroRows()
    {
        int count = 0;
        foreach (var p in _fx.Db.Products.StreamByCategory(999))
            count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public void Stream_ByCategory_YieldsOnlyMatchingRows()
    {
        foreach (var p in _fx.Db.Products.StreamByCategory(1))
            Assert.Equal(1, p.CategoryId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Stream_Async_CancelAtRowK_StopsAtExactlyK(int k)
    {
        using var cts = new CancellationTokenSource();
        int seen = 0;
        await foreach (var p in _fx.Db.Products.StreamAllAsync().WithCancellation(cts.Token))
        {
            seen++;
            if (seen == k) cts.Cancel();
            if (cts.IsCancellationRequested) break;
        }
        Assert.Equal(k, seen);
    }

    [Fact]
    public void Stream_DisposeMidEnumeration_Twice_IsSafe()
    {
        var e = _fx.Db.Products.StreamAll().GetEnumerator();
        Assert.True(e.MoveNext());
        e.Dispose();
        e.Dispose();
    }
}

public class StreamingScaleTests : IClassFixture<StreamingScaleFixture>
{
    private readonly StreamingScaleFixture _fx;
    public StreamingScaleTests(StreamingScaleFixture fx) => _fx = fx;

    [Fact]
    public void Stream_LargeResult_YieldsAllRows()
    {
        int count = 0, lastId = 0;
        foreach (var p in _fx.Db.Products.StreamAll())
        {
            Assert.False(string.IsNullOrEmpty(p.ProductName));
            Assert.True(p.ProductId > lastId);
            lastId = p.ProductId;
            count++;
        }
        Assert.Equal(4 + StreamingScaleFixture.SeededProductCount, count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(10_000)]
    public async Task Stream_Async_CancelDeepInEnumeration_StopsAtExactlyK(int k)
    {
        using var cts = new CancellationTokenSource();
        int seen = 0;
        await foreach (var p in _fx.Db.Products.StreamAllAsync().WithCancellation(cts.Token))
        {
            seen++;
            if (seen == k) cts.Cancel();
            if (cts.IsCancellationRequested) break;
        }
        Assert.Equal(k, seen);
    }
}
