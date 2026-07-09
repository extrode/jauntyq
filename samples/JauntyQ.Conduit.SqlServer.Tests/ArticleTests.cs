using System.Linq;
using JauntyQ.Conduit.SqlServer.Tests.Repositories;
using Xunit;

namespace JauntyQ.Conduit.SqlServer.Tests;

/// <summary>
/// Create/get/list/filter/paginate/update/delete over `articles`, exercising
/// the article_tags/favorites junction tables via EXISTS subqueries in
/// Article/GetFiltered.sql and Article/GetCount.sql - a shape not exercised
/// in Part 1. Seeded: intro-to-jauntyq(1, bob, tags dotnet+sql, 2 favorites),
/// sqlite-tips(2, carol, tags sqlite+sql, 1 favorite), composite-keys-101(3,
/// bob, tags sql+database), unrelated-post(4, dave, tag offtopic). jane(1)
/// favorites articles 1 and 2; carol(3) also favorites article 1.
/// Feed (Article/GetFeed.sql) and favorite/unfavorite actions are covered in
/// later milestones - this covers everything else.
/// </summary>
public class ArticleTests : IClassFixture<ConduitSqlServerFixture>
{
    private readonly ConduitSqlServerFixture _fx;
    private readonly ArticleRepository _repo;

    public ArticleTests(ConduitSqlServerFixture fx)
    {
        _fx = fx;
        _repo = new ArticleRepository(fx.Db);
    }

    [Fact]
    public void Create_WithTags_PersistsAndIsRetrievableBySlug()
    {
        if (!_fx.Available) return;

        string slug = _repo.Create(authorId: 4, title: "Fresh Article", description: "desc",
            body: "body text", tagNames: new[] { "dotnet", "new-tag" }, nowIso: "2026-02-01T00:00:00Z");

        Assert.Equal("fresh-article", slug);

        var view = _repo.GetBySlug(slug, viewerId: null);
        Assert.NotNull(view);
        Assert.Equal("Fresh Article", view!.Title);
        Assert.Equal(new[] { "dotnet", "new-tag" }, view.TagList);
        Assert.Equal(0, view.FavoritesCount);
        Assert.False(view.Favorited);

        _repo.Delete(slug); // avoid inflating article counts seen by other tests sharing this fixture
    }

    [Fact]
    public void GetBySlug_ComposesTagsAuthorAndFavoriteState()
    {
        if (!_fx.Available) return;

        var view = _repo.GetBySlug("intro-to-jauntyq", viewerId: 1);

        Assert.NotNull(view);
        Assert.Equal("Intro to JauntyQ", view!.Title);
        Assert.Equal(new[] { "dotnet", "sql" }, view.TagList);
        Assert.Equal(2, view.FavoritesCount);
        Assert.True(view.Favorited);
        Assert.Equal("bob", view.Author.Username);
        Assert.True(view.Author.Following); // jane follows bob
    }

    [Fact]
    public void GetBySlug_NoViewer_FavoritedFalse()
    {
        if (!_fx.Available) return;

        var view = _repo.GetBySlug("intro-to-jauntyq", viewerId: null);

        Assert.NotNull(view);
        Assert.False(view!.Favorited);
        Assert.Equal(2, view.FavoritesCount); // count is viewer-independent
    }

    [Fact]
    public void GetBySlug_UnknownSlug_ReturnsNull()
    {
        if (!_fx.Available) return;

        Assert.Null(_repo.GetBySlug("does-not-exist", viewerId: null));
    }

    [Fact]
    public void List_FilterByTag_ReturnsMatchingArticlesNewestFirst()
    {
        if (!_fx.Available) return;

        var (articles, total) = _repo.List(tag: "sql", author: null, favoritedBy: null, skip: 0, take: 10, viewerId: null);

        Assert.Equal(3, total);
        Assert.Equal(new[] { "composite-keys-101", "sqlite-tips", "intro-to-jauntyq" }, articles.Select(a => a.Slug));
    }

    [Fact]
    public void List_FilterByAuthor_ReturnsOnlyThatAuthorsArticles()
    {
        if (!_fx.Available) return;

        var (articles, total) = _repo.List(tag: null, author: "bob", favoritedBy: null, skip: 0, take: 10, viewerId: null);

        Assert.Equal(2, total);
        Assert.Equal(new[] { "composite-keys-101", "intro-to-jauntyq" }, articles.Select(a => a.Slug));
    }

    [Fact]
    public void List_FilterByFavoritedBy_ReturnsOnlyArticlesThatUserFavorited()
    {
        if (!_fx.Available) return;

        var (articles, total) = _repo.List(tag: null, author: null, favoritedBy: "jane", skip: 0, take: 10, viewerId: null);

        Assert.Equal(2, total);
        Assert.Equal(new[] { "sqlite-tips", "intro-to-jauntyq" }, articles.Select(a => a.Slug));
    }

    [Fact]
    public void List_CombinedTagAndAuthorFilters_Intersect()
    {
        if (!_fx.Available) return;

        var (articles, total) = _repo.List(tag: "sql", author: "bob", favoritedBy: null, skip: 0, take: 10, viewerId: null);

        Assert.Equal(2, total);
        Assert.Equal(new[] { "composite-keys-101", "intro-to-jauntyq" }, articles.Select(a => a.Slug));
    }

    [Fact]
    public void List_Pagination_SplitsAcrossPagesConsistently()
    {
        if (!_fx.Available) return;

        var (page1, total) = _repo.List(tag: null, author: null, favoritedBy: null, skip: 0, take: 2, viewerId: null);
        var (page2, _) = _repo.List(tag: null, author: null, favoritedBy: null, skip: 2, take: 2, viewerId: null);

        Assert.Equal(4, total);
        Assert.Equal(new[] { "unrelated-post", "composite-keys-101" }, page1.Select(a => a.Slug));
        Assert.Equal(new[] { "sqlite-tips", "intro-to-jauntyq" }, page2.Select(a => a.Slug));
    }

    [Fact]
    public void Feed_ReturnsOnlyFollowedAuthorsArticles()
    {
        if (!_fx.Available) return;

        // jane(1) follows bob(2) and carol(3), not dave(4): feed excludes
        // dave's unrelated-post despite it being the newest article.
        var (articles, total) = _repo.Feed(userId: 1, skip: 0, take: 10);

        Assert.Equal(3, total);
        Assert.Equal(new[] { "composite-keys-101", "sqlite-tips", "intro-to-jauntyq" }, articles.Select(a => a.Slug));
    }

    [Fact]
    public void Feed_NoFollows_ReturnsEmpty()
    {
        if (!_fx.Available) return;

        var (articles, total) = _repo.Feed(userId: 4, skip: 0, take: 10);

        Assert.Equal(0, total);
        Assert.Empty(articles);
    }

    [Fact]
    public void Update_ChangesFieldsAndUpdatedAt()
    {
        if (!_fx.Available) return;

        string slug = _repo.Create(authorId: 2, title: "Update Me", description: "d", body: "b",
            tagNames: System.Array.Empty<string>(), nowIso: "2026-02-02T00:00:00Z");

        _repo.Update(slug, "Updated Title", "new desc", "new body", "2026-02-03T00:00:00Z");

        var view = _repo.GetBySlug(slug, viewerId: null);
        Assert.Equal("Updated Title", view!.Title);
        Assert.Equal("new desc", view.Description);
        Assert.Equal("2026-02-03T00:00:00Z", view.UpdatedAt);

        _repo.Delete(slug); // avoid inflating article counts seen by other tests sharing this fixture
    }

    [Fact]
    public void Delete_RemovesArticleAndCascadesArticleTagsFavoritesAndComments()
    {
        if (!_fx.Available) return;

        string slug = _repo.Create(authorId: 3, title: "Delete Me", description: "d", body: "b",
            tagNames: new[] { "dotnet" }, nowIso: "2026-02-04T00:00:00Z");
        int articleId = _fx.Db.Articles.GetBySlug(slug)!.Id;
        _fx.Db.Favorites.Insert(UserId: 1, ArticleId: articleId);
        _fx.Db.Comments.Insert(ArticleId: articleId, AuthorId: 1, Body: "temp",
            CreatedAt: "2026-02-04T00:00:00Z", UpdatedAt: "2026-02-04T00:00:00Z");
        Assert.NotEmpty(_fx.Db.ArticleTags.GetTagNamesByArticleId(articleId));
        Assert.True(_fx.Db.Favorites.Exists(UserId: 1, ArticleId: articleId)?.Total > 0);
        Assert.NotEmpty(_fx.Db.Comments.GetByArticleId(articleId));

        _repo.Delete(slug);

        Assert.Null(_repo.GetBySlug(slug, viewerId: null));
        Assert.Empty(_fx.Db.ArticleTags.GetTagNamesByArticleId(articleId));
        Assert.False(_fx.Db.Favorites.Exists(UserId: 1, ArticleId: articleId)?.Total > 0);
        Assert.Empty(_fx.Db.Comments.GetByArticleId(articleId));
    }
}
