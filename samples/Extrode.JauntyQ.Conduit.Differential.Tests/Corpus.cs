namespace Conduit.Differential.Tests;

/// <summary>One query plus one fixed argument set, run identically on every engine.</summary>
public sealed record CorpusCase(
    string Query,
    string ArgumentSet,
    IReadOnlyDictionary<string, object?> Arguments,
    bool Ordered);

/// <summary>
/// The canonical argument sets for the Conduit corpus.
///
/// Every value is fixed -- no clock reads, no GUIDs, no random -- because the
/// same arguments have to reach five engines and produce comparable rows. The
/// identity values (user 1-4, article 1-4, tag 1-5) come from the seed in each
/// schema.&lt;engine&gt;.sql, which inserts the same rows in the same order on
/// every engine.
///
/// <c>Ordered</c> is set by hand from the query's own text rather than parsed:
/// the harness does not read .sql. A query carrying ORDER BY compares
/// order-sensitively; one without it compares as a multiset, since row order
/// from an unordered SELECT is not a promise any engine makes.
/// </summary>
public static class Corpus
{
    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] cells)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var cell in cells)
            map[cell.Name] = cell.Value;
        return map;
    }

    private static readonly Dictionary<string, object?> None = new(StringComparer.Ordinal);

    /// <summary>
    /// Read-only cases. Safe to run before and after the mutation script, and
    /// safe to run in any order among themselves.
    /// </summary>
    public static IReadOnlyList<CorpusCase> Reads { get; } = new List<CorpusCase>
    {
        new("Users/GetById", "user 1 (jane)", Args(("Id", 1)), Ordered: false),
        new("Users/GetById", "user 4 (dave)", Args(("Id", 4)), Ordered: false),
        new("Users/GetById", "absent id", Args(("Id", 999)), Ordered: false),
        new("Users/GetByEmail", "jane", Args(("Email", "jane@example.com")), Ordered: false),
        new("Users/GetByEmail", "absent", Args(("Email", "nobody@example.com")), Ordered: false),
        new("Users/GetByUsername", "carol", Args(("Username", "carol")), Ordered: false),

        new("Tags/GetAll", "all", None, Ordered: true),
        new("Tags/GetByName", "sql", Args(("Name", "sql")), Ordered: false),
        new("Tags/GetByName", "absent", Args(("Name", "no-such-tag")), Ordered: false),

        new("Articles/GetBySlug", "sqlite-tips", Args(("Slug", "sqlite-tips")), Ordered: false),
        new("Articles/GetBySlug", "absent", Args(("Slug", "no-such-slug")), Ordered: false),

        new("Articles/GetCount", "no filters",
            Args(("Tag", null), ("Author", null), ("FavoritedBy", null)), Ordered: false),
        new("Articles/GetCount", "tag=sql",
            Args(("Tag", "sql"), ("Author", null), ("FavoritedBy", null)), Ordered: false),
        new("Articles/GetCount", "author=bob",
            Args(("Tag", null), ("Author", "bob"), ("FavoritedBy", null)), Ordered: false),
        new("Articles/GetCount", "favorited by jane",
            Args(("Tag", null), ("Author", null), ("FavoritedBy", "jane")), Ordered: false),
        new("Articles/GetCount", "tag with no articles",
            Args(("Tag", "no-such-tag"), ("Author", null), ("FavoritedBy", null)), Ordered: false),

        new("Articles/GetFiltered", "no filters, first page",
            Args(("Tag", null), ("Author", null), ("FavoritedBy", null), ("Skip", 0), ("Take", 10)), Ordered: true),
        new("Articles/GetFiltered", "no filters, page 2 of 2",
            Args(("Tag", null), ("Author", null), ("FavoritedBy", null), ("Skip", 2), ("Take", 2)), Ordered: true),
        new("Articles/GetFiltered", "no filters, past the end",
            Args(("Tag", null), ("Author", null), ("FavoritedBy", null), ("Skip", 100), ("Take", 10)), Ordered: true),
        new("Articles/GetFiltered", "take zero",
            Args(("Tag", null), ("Author", null), ("FavoritedBy", null), ("Skip", 0), ("Take", 0)), Ordered: true),
        new("Articles/GetFiltered", "tag=sql",
            Args(("Tag", "sql"), ("Author", null), ("FavoritedBy", null), ("Skip", 0), ("Take", 10)), Ordered: true),
        new("Articles/GetFiltered", "author=bob",
            Args(("Tag", null), ("Author", "bob"), ("FavoritedBy", null), ("Skip", 0), ("Take", 10)), Ordered: true),
        new("Articles/GetFiltered", "favorited by jane",
            Args(("Tag", null), ("Author", null), ("FavoritedBy", "jane"), ("Skip", 0), ("Take", 10)), Ordered: true),
        new("Articles/GetFiltered", "tag and author together",
            Args(("Tag", "sql"), ("Author", "bob"), ("FavoritedBy", null), ("Skip", 0), ("Take", 10)), Ordered: true),

        new("Articles/GetFeed", "jane, who follows bob and carol",
            Args(("UserId", 1), ("Skip", 0), ("Take", 10)), Ordered: true),
        new("Articles/GetFeed", "jane, paged",
            Args(("UserId", 1), ("Skip", 1), ("Take", 1)), Ordered: true),
        new("Articles/GetFeed", "dave, who follows nobody",
            Args(("UserId", 4), ("Skip", 0), ("Take", 10)), Ordered: true),
        new("Articles/GetFeedCount", "jane", Args(("UserId", 1)), Ordered: false),
        new("Articles/GetFeedCount", "dave", Args(("UserId", 4)), Ordered: false),

        new("Comments/GetByArticleId", "article 1, which has two",
            Args(("ArticleIds", new[] { 1 })), Ordered: true),
        new("Comments/GetByArticleId", "articles 1 and 2",
            Args(("ArticleIds", new[] { 1, 2 })), Ordered: true),
        new("Comments/GetByArticleId", "article with none",
            Args(("ArticleIds", new[] { 3 })), Ordered: true),

        new("ArticleTags/GetTagNamesByArticleId", "article 1",
            Args(("ArticleIds", new[] { 1 })), Ordered: true),
        new("ArticleTags/GetTagNamesByArticleId", "all four articles",
            Args(("ArticleIds", new[] { 1, 2, 3, 4 })), Ordered: true),

        new("Favorites/Exists", "jane favorited article 1",
            Args(("UserId", 1), ("ArticleId", 1)), Ordered: false),
        new("Favorites/Exists", "dave favorited nothing",
            Args(("UserId", 4), ("ArticleId", 1)), Ordered: false),
        new("Favorites/GetCountByArticleId", "articles 1 and 2",
            Args(("ArticleIds", new[] { 1, 2 })), Ordered: false),
        new("Favorites/GetCountByArticleId", "article nobody favorited",
            Args(("ArticleIds", new[] { 4 })), Ordered: false),

        new("Follows/Exists", "jane follows bob",
            Args(("FollowerId", 1), ("FollowedId", 2)), Ordered: false),
        new("Follows/Exists", "jane does not follow dave",
            Args(("FollowerId", 1), ("FollowedId", 4)), Ordered: false),

        // Auto-CRUD's own read surface. Its parameters are the snake_case
        // column names, not the PascalCase of a hand-written @params
        // directive, and it emits no ORDER BY -- so every case here is a
        // multiset comparison.
        new("Users/GetAll", "all users", None, Ordered: false),
        new("Articles/GetAll", "all articles", None, Ordered: false),
        new("Articles/GetById", "article 3", Args(("id", 3)), Ordered: false),
        new("Articles/GetById", "absent article", Args(("id", 999)), Ordered: false),
        new("Articles/GetByAuthorId", "bob's articles", Args(("author_id", 2)), Ordered: false),
        new("Articles/GetByAuthorId", "an author with none", Args(("author_id", 1)), Ordered: false),
        new("Tags/GetById", "tag 2", Args(("id", 2)), Ordered: false),
        new("Comments/GetAll", "all comments", None, Ordered: false),
        new("Comments/GetById", "comment 1", Args(("id", 1)), Ordered: false),
        new("Comments/GetByAuthorId", "carol's comments", Args(("author_id", 3)), Ordered: false),
        new("ArticleTags/GetAll", "all article tags", None, Ordered: false),
        new("ArticleTags/GetById", "composite key (1, 2)",
            Args(("article_id", 1), ("tag_id", 2)), Ordered: false),
        new("ArticleTags/GetById", "composite key that is not there",
            Args(("article_id", 1), ("tag_id", 5)), Ordered: false),
        new("ArticleTags/GetByArticleId", "article 1", Args(("article_id", 1)), Ordered: false),
        new("ArticleTags/GetByTagId", "tag sql, on three articles", Args(("tag_id", 2)), Ordered: false),
        new("Favorites/GetAll", "all favorites", None, Ordered: false),
        new("Favorites/GetById", "composite key (1, 1)",
            Args(("user_id", 1), ("article_id", 1)), Ordered: false),
        new("Favorites/GetByUserId", "jane's favorites", Args(("user_id", 1)), Ordered: false),
        new("Favorites/GetByArticleId", "article 1", Args(("article_id", 1)), Ordered: false),
        new("Follows/GetAll", "all follows", None, Ordered: false),
        new("Follows/GetById", "composite key (1, 2)",
            Args(("follower_id", 1), ("followed_id", 2)), Ordered: false),
        new("Follows/GetByFollowerId", "who jane follows", Args(("follower_id", 1)), Ordered: false),
        new("Follows/GetByFollowedId", "who follows bob", Args(("followed_id", 2)), Ordered: false),
    };

    /// <summary>
    /// Write cases, applied in this order to every engine, after which the
    /// full read list runs again. Identity values assume the seed's 4 users,
    /// 4 articles, 5 tags and 2 comments, so the inserts below land on 5, 5,
    /// 6 and 3 on every engine.
    /// </summary>
    public static IReadOnlyList<CorpusCase> Mutations { get; } = new List<CorpusCase>
    {
        new("Users/Insert", "new user erin",
            Args(("Username", "erin"), ("Email", "erin@example.com"),
                 ("PasswordHash", "0000000000000000000000000000000000000000000000000000000000000000"),
                 ("Bio", "Erin's bio"), ("Image", null)), Ordered: false),
        new("Tags/Insert", "new tag testing", Args(("Name", "testing")), Ordered: false),
        new("Articles/Insert", "new article by erin",
            Args(("Slug", "differential-testing"), ("Title", "Differential Testing"),
                 ("Description", "Five engines, one corpus"), ("Body", "Body 5"),
                 ("AuthorId", 5), ("CreatedAt", "2026-02-01T00:00:00Z"),
                 ("UpdatedAt", "2026-02-01T00:00:00Z")), Ordered: false),
        new("ArticleTags/Insert", "tag the new article",
            Args(("ArticleId", 5), ("TagId", 6)), Ordered: false),
        new("Follows/Insert", "erin follows bob",
            Args(("FollowerId", 5), ("FollowedId", 2)), Ordered: false),
        new("Favorites/Insert", "erin favorites article 1",
            Args(("UserId", 5), ("ArticleId", 1)), Ordered: false),
        new("Comments/Insert", "erin comments on article 1",
            Args(("ArticleId", 1), ("AuthorId", 5), ("Body", "Erin was here."),
                 ("CreatedAt", "2026-02-02T00:00:00Z"), ("UpdatedAt", "2026-02-02T00:00:00Z")), Ordered: false),

        new("Users/Update", "rename bob's bio",
            Args(("Id", 2), ("Username", "bob"), ("Email", "bob@example.com"),
                 ("Bio", "Bob's revised bio"), ("Image", null),
                 ("PasswordHash", "1111111111111111111111111111111111111111111111111111111111111111")), Ordered: false),
        new("Articles/Update", "revise article 2",
            Args(("Id", 2), ("Title", "SQLite Tips, Revised"), ("Description", "Still tips"),
                 ("Body", "Body 2 revised"), ("UpdatedAt", "2026-02-03T00:00:00Z")), Ordered: false),

        new("Favorites/Delete", "erin unfavorites article 1",
            Args(("UserId", 5), ("ArticleId", 1)), Ordered: false),
        new("Follows/Delete", "erin unfollows bob",
            Args(("FollowerId", 5), ("FollowedId", 2)), Ordered: false),
        new("Comments/Delete", "remove comment 3", Args(("Id", 3)), Ordered: false),
        new("Articles/Delete", "remove article 4, which cascades", Args(("Id", 4)), Ordered: false),
    };

    /// <summary>
    /// Generated methods the corpus deliberately does not run, each with the
    /// reason. CoverageTests fails on any generated query that is in neither
    /// this set nor the corpus, so a new Conduit query cannot go untested by
    /// omission.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Excluded { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Users/Upsert"] =
                "JNT2018 records that MySQL and MariaDb emit a non-atomic UPDATE-then-INSERT here "
                + "because the table carries a second UNIQUE constraint. The engines are documented "
                + "as differing, so equality across them is not a claim this suite should make.",
            ["Articles/Upsert"] = "same JNT2018 non-atomic emission as Users/Upsert",

            ["Users/Delete"] =
                "auto-CRUD writer. Users/Update and the cascade from Articles/Delete already exercise "
                + "this table's write path; adding another writer to the ordered mutation script makes "
                + "the post-mutation read state harder to reason about for no extra engine surface.",
            ["Tags/Delete"] = "auto-CRUD writer, same reason as Users/Delete",
            ["Tags/Update"] = "auto-CRUD writer, same reason as Users/Delete",
            ["ArticleTags/Delete"] = "auto-CRUD writer, same reason as Users/Delete",
            ["Comments/Update"] = "auto-CRUD writer, same reason as Users/Delete",
        };

    /// <summary>
    /// Async twins are the same SQL through the same emitted command, so
    /// running both would double the container time for no additional
    /// cross-engine surface. CoverageTests treats a Foo/BarAsync as covered
    /// when Foo/Bar is.
    /// </summary>
    public static string? SyncTwinOf(string query) =>
        query.EndsWith("Async", StringComparison.Ordinal)
            ? query[..^"Async".Length]
            : null;
}
