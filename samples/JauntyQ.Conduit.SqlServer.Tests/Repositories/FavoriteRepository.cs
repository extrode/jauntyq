using JauntyQ.Generated;

namespace JauntyQ.Conduit.SqlServer.Tests.Repositories;

public sealed class FavoriteRepository
{
    private readonly JauntyDb _db;

    public FavoriteRepository(JauntyDb db) => _db = db;

    public void Favorite(int userId, int articleId)
        => _db.Favorites.Insert(UserId: userId, ArticleId: articleId);

    public void Unfavorite(int userId, int articleId)
        => _db.Favorites.Delete(UserId: userId, ArticleId: articleId);

    public int GetCount(int articleId)
        => (int)_db.Favorites.GetCountByArticleId(articleId)!.Total;

    public bool IsFavorited(int userId, int articleId)
        => _db.Favorites.Exists(UserId: userId, ArticleId: articleId)?.Total > 0;
}
