using System.Collections.Generic;
using System.Linq;
using JauntyQ.Generated;

namespace JauntyQ.Conduit.Postgres.Tests.Repositories;

public sealed class CommentRepository
{
    private readonly JauntyDb _db;
    private readonly ProfileRepository _profiles;

    public CommentRepository(JauntyDb db)
    {
        _db = db;
        _profiles = new ProfileRepository(db);
    }

    public int Add(int articleId, int authorId, string body, string nowIso)
        => _db.Comments.Insert(ArticleId: articleId, AuthorId: authorId, Body: body, CreatedAt: nowIso, UpdatedAt: nowIso);

    public IReadOnlyList<Domain.CommentView> GetByArticleId(int articleId, int? viewerId)
        => _db.Comments.GetByArticleId(articleId).Select(c => ToView(c, viewerId)).ToList();

    public void Delete(int commentId) => _db.Comments.Delete(commentId);

    private Domain.CommentView ToView(Comment row, int? viewerId)
    {
        var author = _db.Users.GetById(row.AuthorId)!;
        var profile = _profiles.GetProfile(author.Username, viewerId)!;
        return new Domain.CommentView(row.Id, row.CreatedAt, row.UpdatedAt, row.Body, profile);
    }
}
