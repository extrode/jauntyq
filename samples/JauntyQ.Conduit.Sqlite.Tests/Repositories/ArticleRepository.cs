using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using JauntyQ.Generated;

namespace JauntyQ.Conduit.Sqlite.Tests.Repositories;

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
        return row is null ? null : ToView(row, viewerId);
    }

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
        var views = rows.Select(r => ToView(r, viewerId)).ToList();
        return (views, total);
    }

    // Articles authored by users `userId` follows - exercises the
    // subquery-via-junction shape (IN over a SELECT against `follows`)
    // that Part 3 exists to stress-test.
    public (IReadOnlyList<Domain.ArticleView> Articles, int Total) Feed(int userId, int skip, int take)
    {
        var rows = _db.Articles.GetFeed(UserId: userId, Skip: skip, Take: take);
        int total = (int)_db.Articles.GetFeedCount(UserId: userId)!.Total;
        var views = rows.Select(r => ToView(r, userId)).ToList();
        return (views, total);
    }

    private Domain.ArticleView ToView(Article row, int? viewerId)
    {
        var tagNames = _db.ArticleTags.GetTagNamesByArticleId(row.Id).Select(t => t.Name).ToList();
        bool favorited = viewerId is not null
            && _db.Favorites.Exists(UserId: viewerId.Value, ArticleId: row.Id)?.Total > 0;
        int favoritesCount = (int)_db.Favorites.GetCountByArticleId(row.Id)!.Total;
        var author = _db.Users.GetById(row.AuthorId)!;
        var authorProfile = _profiles.GetProfile(author.Username, viewerId)!;

        return new Domain.ArticleView(
            row.Slug, row.Title, row.Description, row.Body, tagNames,
            row.CreatedAt, row.UpdatedAt, favorited, favoritesCount, authorProfile);
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
