using MySqlConnector;
using Conduit.MySql.Tests.Repositories;
using Xunit;

namespace Conduit.MySql.Tests;

/// <summary>
/// Favorite/unfavorite over the composite-PK `favorites` junction table, and
/// favorites-count correctness across multiple users favoriting the same
/// article. Seeded: jane+carol both favorite article 1 (count 2), jane also
/// favorites article 2 (count 1), article 3 has no favorites (count 0).
/// </summary>
[Collection("ConduitMySql")]
public class FavoriteTests
{
    private readonly ConduitMySqlFixture _fx;
    private readonly FavoriteRepository _repo;

    public FavoriteTests(ConduitMySqlFixture fx)
    {
        _fx = fx;
        _repo = new FavoriteRepository(fx.Db);
    }

    [SkippableFact]
    public void GetCount_MultipleUsersFavorited_ReturnsCorrectCount()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.Equal(2, _repo.GetCount(articleId: 1));
        Assert.Equal(1, _repo.GetCount(articleId: 2));
        Assert.Equal(0, _repo.GetCount(articleId: 3));
    }

    [SkippableFact]
    public void IsFavorited_ReflectsSeededState()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.True(_repo.IsFavorited(userId: 1, articleId: 1));
        Assert.False(_repo.IsFavorited(userId: 4, articleId: 1));
    }

    [SkippableFact]
    public void Favorite_ThenGetCount_Increments()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        _repo.Favorite(userId: 4, articleId: 3);

        Assert.Equal(1, _repo.GetCount(articleId: 3));
        Assert.True(_repo.IsFavorited(userId: 4, articleId: 3));

        _repo.Unfavorite(userId: 4, articleId: 3);
    }

    [SkippableFact]
    public void Unfavorite_ThenGetCount_Decrements()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        _repo.Favorite(userId: 2, articleId: 3);
        _repo.Unfavorite(userId: 2, articleId: 3);

        Assert.Equal(0, _repo.GetCount(articleId: 3));
    }

    // Composite-PK conflict behavior, same shape as Follow_Duplicate in
    // ProfileTests: favoriting the same article twice hits the (user_id,
    // article_id) primary key directly, with no ON CONFLICT clause in
    // Favorites/Insert.sql - expect a raw MySqlException.
    [SkippableFact]
    public void Favorite_Duplicate_ThrowsOnPrimaryKeyConflict()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.Throws<MySqlException>(() => _repo.Favorite(userId: 1, articleId: 1));
    }
}
