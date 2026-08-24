using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Sakila.MySql.Tests;

public class StreamingTests : IClassFixture<SakilaMySqlFixture>
{
    private readonly SakilaMySqlFixture _fx;
    public StreamingTests(SakilaMySqlFixture fx) => _fx = fx;

    [SkippableFact]
    public void Stream_Sync_YieldsAllRowsInOrder()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
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

    [SkippableFact]
    public async Task Stream_Async_YieldsAllRows()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        int count = 0;
        await foreach (var f in _fx.Db.Film.StreamAllAsync())
        {
            Assert.False(string.IsNullOrEmpty(f.Title));
            count++;
        }
        Assert.Equal(1000, count);
    }

    [SkippableFact]
    public void Stream_EmptyResult_YieldsZeroRows()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        int count = 0;
        foreach (var f in _fx.Db.Film.StreamUpTo(1))
            count++;
        Assert.Equal(0, count);
    }

    [SkippableFact]
    public void Stream_UpToThreshold_RespectsBound()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        int count = 0;
        foreach (var f in _fx.Db.Film.StreamUpTo(101))
        {
            Assert.True(f.FilmId < 101);
            count++;
        }
        Assert.Equal(100, count);
    }

    [SkippableFact]
    public async Task Stream_Async_CancelMidEnumeration_StopsCleanly()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
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

    [SkippableFact]
    public void Stream_Sync_BreakEarly_StopsCleanly()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        int seen = 0;
        foreach (var f in _fx.Db.Film.StreamUpTo(101))
        {
            seen++;
            if (seen == 50) break;
        }
        Assert.Equal(50, seen);
    }

    [SkippableFact]
    public void Stream_DisposeMidEnumeration_IsSafe()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var e = _fx.Db.Film.StreamUpTo(101).GetEnumerator();
        Assert.True(e.MoveNext());
        e.Dispose();
        e.Dispose();
    }
}
