using MySqlConnector;
using JauntyQ.Conduit.MySql.Tests.Repositories;
using Xunit;

namespace JauntyQ.Conduit.MySql.Tests;

/// <summary>
/// Profile lookups (+ following flag) and follow/unfollow over the
/// composite-PK `follows` junction table - the first multi-column primary
/// key exercised across any of this repo's sample projects. Seeded:
/// jane(1) follows bob(2) and carol(3), but not dave(4).
/// </summary>
[Collection("ConduitMySql")]
public class ProfileTests
{
    private readonly ConduitMySqlFixture _fx;
    private readonly ProfileRepository _repo;

    public ProfileTests(ConduitMySqlFixture fx)
    {
        _fx = fx;
        _repo = new ProfileRepository(fx.Db);
    }

    [SkippableFact]
    public void GetProfile_Following_FollowingTrue()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var profile = _repo.GetProfile("bob", viewerId: 1);

        Assert.NotNull(profile);
        Assert.True(profile!.Following);
    }

    [SkippableFact]
    public void GetProfile_NotFollowing_FollowingFalse()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var profile = _repo.GetProfile("dave", viewerId: 1);

        Assert.NotNull(profile);
        Assert.False(profile!.Following);
    }

    [SkippableFact]
    public void GetProfile_NoViewer_FollowingFalse()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var profile = _repo.GetProfile("bob", viewerId: null);

        Assert.NotNull(profile);
        Assert.False(profile!.Following);
    }

    [SkippableFact]
    public void GetProfile_UnknownUser_ReturnsNull()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var profile = _repo.GetProfile("nobody", viewerId: 1);

        Assert.Null(profile);
    }

    [SkippableFact]
    public void Follow_ThenGetProfile_FollowingTrue()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        _repo.Follow(followerId: 1, followedId: 4);

        var profile = _repo.GetProfile("dave", viewerId: 1);
        Assert.True(profile!.Following);

        _repo.Unfollow(followerId: 1, followedId: 4);
    }

    [SkippableFact]
    public void Unfollow_ThenGetProfile_FollowingFalse()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        _repo.Follow(followerId: 3, followedId: 4);
        _repo.Unfollow(followerId: 3, followedId: 4);

        var profile = _repo.GetProfile("dave", viewerId: 3);
        Assert.False(profile!.Following);
    }

    // Composite-PK conflict behavior: following an already-followed user a
    // second time hits the (follower_id, followed_id) primary key directly,
    // with no ON CONFLICT clause in Follow/Insert.sql - expect a raw
    // MySqlException, logged as a Part 3 finding either way.
    [SkippableFact]
    public void Follow_Duplicate_ThrowsOnPrimaryKeyConflict()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.Throws<MySqlException>(() => _repo.Follow(followerId: 1, followedId: 2));
    }
}
