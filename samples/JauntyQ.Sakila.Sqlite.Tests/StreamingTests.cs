using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Sakila.Sqlite.Tests;

public class StreamingTests : IClassFixture<SakilaSqliteFixture>
{
    private readonly SakilaSqliteFixture _fx;
    public StreamingTests(SakilaSqliteFixture fx) => _fx = fx;

    [Fact]
    public void Stream_Sync_YieldsAllRowsInOrder()
    {
        int count = 0; int? lastId = null;
        foreach (var f in _fx.Db.Film.StreamAll())
        {
            Assert.False(string.IsNullOrEmpty(f.Title));
            Assert.True(lastId is null || f.FilmId > lastId);
            lastId = f.FilmId;
            count++;
        }
        Assert.Equal(1000, count);
    }

    [Fact]
    public async Task Stream_Async_YieldsAllRows()
    {
        int count = 0;
        await foreach (var f in _fx.Db.Film.StreamAllAsync())
        {
            Assert.False(string.IsNullOrEmpty(f.Title));
            count++;
        }
        Assert.Equal(1000, count);
    }

    [Fact]
    public void Stream_EmptyResult_YieldsZeroRows()
    {
        int count = 0;
        foreach (var f in _fx.Db.Film.StreamUpTo(1))
            count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public void Stream_UpToThreshold_RespectsBound()
    {
        int count = 0;
        foreach (var f in _fx.Db.Film.StreamUpTo(101))
        {
            Assert.True(f.FilmId < 101);
            count++;
        }
        Assert.Equal(100, count);
    }

    [Fact]
    public async Task Stream_Async_CancelMidEnumeration_StopsCleanly()
    {
        using var cts = new CancellationTokenSource();
        int seen = 0;
        await foreach (var f in _fx.Db.Film.StreamUpToAsync(101).WithCancellation(cts.Token))
        {
            seen++;
            if (seen == 50) cts.Cancel();
            if (cts.IsCancellationRequested) break;
        }
        Assert.Equal(50, seen);
    }

    [Fact]
    public void Stream_Sync_BreakEarly_StopsCleanly()
    {
        int seen = 0;
        foreach (var f in _fx.Db.Film.StreamUpTo(101))
        {
            seen++;
            if (seen == 50) break;
        }
        Assert.Equal(50, seen);
    }

    [Fact]
    public void Stream_DisposeMidEnumeration_IsSafe()
    {
        var e = _fx.Db.Film.StreamUpTo(101).GetEnumerator();
        Assert.True(e.MoveNext());
        e.Dispose();
        e.Dispose();
    }
}
