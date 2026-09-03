using Conduit.SqlServer.Tests.Domain;

namespace Conduit.SqlServer.Tests.Contracts;

/// <summary>
/// Domain.ArticleView's field names already match the wire shape 1:1
/// ({"article": {slug, title, description, body, tagList, createdAt,
/// updatedAt, favorited, favoritesCount, author}}) -- just needs wrapping.
/// </summary>
public sealed record ArticleResponseEnvelope(ArticleView Article);

/// <summary>
/// {"articles": [...], "articlesCount": N} -- renames the repository's
/// (Articles, Total) tuple field Total to the wire name ArticlesCount.
/// </summary>
public sealed record ArticlesResponse(IReadOnlyList<ArticleView> Articles, int ArticlesCount);

public sealed record UpsertArticleRequest(string Title, string Description, string Body, IReadOnlyList<string>? TagList);
public sealed record UpsertArticleRequestEnvelope(UpsertArticleRequest Article);
