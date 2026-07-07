using Microsoft.Data.Sqlite;
using JauntyQ.Conduit.Sqlite.Tests.Repositories;
using Xunit;

namespace JauntyQ.Conduit.Sqlite.Tests;

/// <summary>
/// Favorite/unfavorite over the composite-PK `favorites` junction table, and
/// favorites-count correctness across multiple users favoriting the same
/// article. Seeded: jane+carol both favorite article 1 (count 2), jane also
/// favorites article 2 (count 1), article 3 has no favorites (count 0).
/// </summary>
public class FavoriteTests : IClassFixture<ConduitSqliteFixture>
{
    private readonly FavoriteRepository _repo;

    public FavoriteTests(ConduitSqliteFixture fx)
    {
        _repo = new FavoriteRepository(fx.Db);
    }

    [Fact]
    public void GetCount_MultipleUsersFavorited_ReturnsCorrectCount()
    {
        Assert.Equal(2, _repo.GetCount(articleId: 1));
        Assert.Equal(1, _repo.GetCount(articleId: 2));
        Assert.Equal(0, _repo.GetCount(articleId: 3));
    }

    [Fact]
    public void IsFavorited_ReflectsSeededState()
    {
        Assert.True(_repo.IsFavorited(userId: 1, articleId: 1));
        Assert.False(_repo.IsFavorited(userId: 4, articleId: 1));
    }

    [Fact]
    public void Favorite_ThenGetCount_Increments()
    {
        _repo.Favorite(userId: 4, articleId: 3);

        Assert.Equal(1, _repo.GetCount(articleId: 3));
        Assert.True(_repo.IsFavorited(userId: 4, articleId: 3));

        _repo.Unfavorite(userId: 4, articleId: 3);
    }

    [Fact]
    public void Unfavorite_ThenGetCount_Decrements()
    {
        _repo.Favorite(userId: 2, articleId: 3);
        _repo.Unfavorite(userId: 2, articleId: 3);

        Assert.Equal(0, _repo.GetCount(articleId: 3));
    }

    // Composite-PK conflict behavior, same shape as Follow_Duplicate in
    // ProfileTests: favoriting the same article twice hits the (user_id,
    // article_id) primary key directly, with no ON CONFLICT clause in
    // Favorites/Insert.sql - expect a raw SqliteException.
    [Fact]
    public void Favorite_Duplicate_ThrowsOnPrimaryKeyConflict()
    {
        Assert.Throws<SqliteException>(() => _repo.Favorite(userId: 1, articleId: 1));
    }
}
