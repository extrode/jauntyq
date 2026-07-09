using System.Collections.Generic;

namespace JauntyQ.Conduit.Postgres.Tests.Domain;

public sealed record ArticleView(
    string Slug,
    string Title,
    string Description,
    string Body,
    IReadOnlyList<string> TagList,
    string CreatedAt,
    string UpdatedAt,
    bool Favorited,
    int FavoritesCount,
    Profile Author);
