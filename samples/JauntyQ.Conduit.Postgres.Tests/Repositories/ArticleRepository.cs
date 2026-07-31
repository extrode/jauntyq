using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using JauntyQ.Generated;

namespace JauntyQ.Conduit.Postgres.Tests.Repositories;

public sealed class ArticleRepository
{
    private readonly JauntyDb _db;
    private readonly ProfileRepository _profiles;

    public ArticleRepository(JauntyDb db)
    {
        _db = db;
        _profiles = new ProfileRepository(db);
    }

    public string Create(int authorId, string title, string description, string body,
        IEnumerable<string> tagNames, string nowIso)
    {
        string slug = Slugify(title);

        int articleId = _db.Articles.Insert(
            Slug: slug, Title: title, Description: description, Body: body,
            AuthorId: authorId, CreatedAt: nowIso, UpdatedAt: nowIso);

        foreach (var tagName in tagNames)
        {
            int tagId = GetOrCreateTagId(tagName);
            _db.ArticleTags.Insert(ArticleId: articleId, TagId: tagId);
        }

        return slug;
    }

    public Domain.ArticleView? GetBySlug(string slug, int? viewerId)
    {
        var row = _db.Articles.GetBySlug(slug);
        return row is null ? null : ToViews(new[] { row }, viewerId)[0];
    }

    public int? GetIdBySlug(string slug) => _db.Articles.GetBySlug(slug)?.Id;

    public void Update(string slug, string title, string description, string body, string nowIso)
    {
        var row = _db.Articles.GetBySlug(slug);
        if (row is null) return;
        _db.Articles.Update(Id: row.Id, Title: title, Description: description, Body: body, UpdatedAt: nowIso);
    }

    public void Delete(string slug)
    {
        var row = _db.Articles.GetBySlug(slug);
        if (row is null) return;
        _db.Articles.Delete(row.Id);
    }

    public (IReadOnlyList<Domain.ArticleView> Articles, int Total) List(
        string? tag, string? author, string? favoritedBy, int skip, int take, int? viewerId)
    {
        var rows = _db.Articles.GetFiltered(Tag: tag, Author: author, FavoritedBy: favoritedBy, Skip: skip, Take: take);
        int total = (int)_db.Articles.GetCount(Tag: tag, Author: author, FavoritedBy: favoritedBy)!.Total;
        return (ToViews(rows, viewerId), total);
    }

    // Articles authored by users `userId` follows - exercises the
    // subquery-via-junction shape (IN over a SELECT against `follows`)
    // that Part 3 exists to stress-test.
    public (IReadOnlyList<Domain.ArticleView> Articles, int Total) Feed(int userId, int skip, int take)
    {
        var rows = _db.Articles.GetFeed(UserId: userId, Skip: skip, Take: take);
        int total = (int)_db.Articles.GetFeedCount(UserId: userId)!.Total;
        return (ToViews(rows, userId), total);
    }

    private IReadOnlyList<Domain.ArticleView> ToViews(IReadOnlyList<Article> rows, int? viewerId)
    {
        var ids = rows.Select(r => r.Id).ToArray();
        var tagNamesById = _db.ArticleTags.GetTagNamesByArticleId(ids)
            .GroupBy(t => t.ArticleId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Name).ToList());
        var favoriteCountsById = _db.Favorites.GetCountByArticleId(ids)
            .ToDictionary(f => f.ArticleId, f => (int)f.Total);

        var views = new List<Domain.ArticleView>(rows.Count);
        foreach (var row in rows)
        {
            bool favorited = viewerId is not null
                && _db.Favorites.Exists(UserId: viewerId.Value, ArticleId: row.Id)?.Total > 0;
            var author = _db.Users.GetById(row.AuthorId)!;
            var authorProfile = _profiles.GetProfile(author.Username, viewerId)!;

            views.Add(new Domain.ArticleView(
                row.Slug, row.Title, row.Description, row.Body,
                tagNamesById.TryGetValue(row.Id, out var tagNames) ? tagNames : new List<string>(),
                row.CreatedAt, row.UpdatedAt, favorited,
                favoriteCountsById.TryGetValue(row.Id, out int favoritesCount) ? favoritesCount : 0,
                authorProfile));
        }
        return views;
    }

    private int GetOrCreateTagId(string name)
    {
        var existing = _db.Tags.GetByName(name);
        if (existing is not null) return existing.Id;
        return _db.Tags.Insert(Name: name);
    }

    private static string Slugify(string title)
    {
        string lowered = title.ToLowerInvariant();
        string hyphenated = Regex.Replace(lowered, "[^a-z0-9]+", "-");
        return hyphenated.Trim('-');
    }
}
