using System.Linq;
using JauntyQ.Conduit.Postgres.Tests.Repositories;
using Xunit;

namespace JauntyQ.Conduit.Postgres.Tests;

/// <summary>
/// Add/list/delete over `comments`. Seeded: article 1 (intro-to-jauntyq)
/// has 2 comments, from jane and carol, in chronological order.
/// </summary>
public class CommentTests : IClassFixture<ConduitPostgresFixture>
{
    private readonly ConduitPostgresFixture _fx;
    private readonly CommentRepository _repo;

    public CommentTests(ConduitPostgresFixture fx)
    {
        _fx = fx;
        _repo = new CommentRepository(fx.Db);
    }

    [SkippableFact]
    public void GetByArticleId_ReturnsSeededCommentsInChronologicalOrder()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var comments = _repo.GetByArticleId(articleId: 1, viewerId: null);

        Assert.Equal(2, comments.Count);
        Assert.Equal(new[] { "jane", "carol" }, comments.Select(c => c.Author.Username));
    }

    [SkippableFact]
    public void Add_ThenGetByArticleId_IncludesNewComment()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int commentId = _repo.Add(articleId: 2, authorId: 1, body: "Nice tips!", nowIso: "2026-02-05T00:00:00Z");

        var comments = _repo.GetByArticleId(articleId: 2, viewerId: null);
        Assert.Contains(comments, c => c.Id == commentId && c.Body == "Nice tips!");

        _repo.Delete(commentId);
    }

    [SkippableFact]
    public void Delete_RemovesComment()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int commentId = _repo.Add(articleId: 3, authorId: 1, body: "Temp comment", nowIso: "2026-02-06T00:00:00Z");

        _repo.Delete(commentId);

        var comments = _repo.GetByArticleId(articleId: 3, viewerId: null);
        Assert.DoesNotContain(comments, c => c.Id == commentId);
    }
}
