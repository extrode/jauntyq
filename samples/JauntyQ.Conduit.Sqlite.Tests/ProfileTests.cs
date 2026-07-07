using Microsoft.Data.Sqlite;
using JauntyQ.Conduit.Sqlite.Tests.Repositories;
using Xunit;

namespace JauntyQ.Conduit.Sqlite.Tests;

/// <summary>
/// Profile lookups (+ following flag) and follow/unfollow over the
/// composite-PK `follows` junction table - the first multi-column primary
/// key exercised across any of this repo's sample projects. Seeded:
/// jane(1) follows bob(2) and carol(3), but not dave(4).
/// </summary>
public class ProfileTests : IClassFixture<ConduitSqliteFixture>
{
    private readonly ProfileRepository _repo;

    public ProfileTests(ConduitSqliteFixture fx)
    {
        _repo = new ProfileRepository(fx.Db);
    }

    [Fact]
    public void GetProfile_Following_FollowingTrue()
    {
        var profile = _repo.GetProfile("bob", viewerId: 1);

        Assert.NotNull(profile);
        Assert.True(profile!.Following);
    }

    [Fact]
    public void GetProfile_NotFollowing_FollowingFalse()
    {
        var profile = _repo.GetProfile("dave", viewerId: 1);

        Assert.NotNull(profile);
        Assert.False(profile!.Following);
    }

    [Fact]
    public void GetProfile_NoViewer_FollowingFalse()
    {
        var profile = _repo.GetProfile("bob", viewerId: null);

        Assert.NotNull(profile);
        Assert.False(profile!.Following);
    }

    [Fact]
    public void GetProfile_UnknownUser_ReturnsNull()
    {
        var profile = _repo.GetProfile("nobody", viewerId: 1);

        Assert.Null(profile);
    }

    [Fact]
    public void Follow_ThenGetProfile_FollowingTrue()
    {
        _repo.Follow(followerId: 1, followedId: 4);

        var profile = _repo.GetProfile("dave", viewerId: 1);
        Assert.True(profile!.Following);

        _repo.Unfollow(followerId: 1, followedId: 4);
    }

    [Fact]
    public void Unfollow_ThenGetProfile_FollowingFalse()
    {
        _repo.Follow(followerId: 3, followedId: 4);
        _repo.Unfollow(followerId: 3, followedId: 4);

        var profile = _repo.GetProfile("dave", viewerId: 3);
        Assert.False(profile!.Following);
    }

    // Composite-PK conflict behavior: following an already-followed user a
    // second time hits the (follower_id, followed_id) primary key directly,
    // with no ON CONFLICT clause in Follow/Insert.sql - expect a raw
    // SqliteException, logged as a Part 3 finding either way.
    [Fact]
    public void Follow_Duplicate_ThrowsOnPrimaryKeyConflict()
    {
        Assert.Throws<SqliteException>(() => _repo.Follow(followerId: 1, followedId: 2));
    }
}
