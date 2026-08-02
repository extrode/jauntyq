using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

/// <summary>
/// Two parser-level guarantees requested against 1a5296f by the manzil.my
/// perf-tail work (the plan):
/// <list type="bullet">
/// <item>A second top-level statement is recorded as MULTI_STATEMENT rather
/// than silently merged into (or truncated from) the first statement's
/// model. The generator turns that into JNT1008.</item>
/// <item><c>count(...) OVER (...)</c> infers bigint NOT NULL, the same as the
/// plain aggregate call, so the common paginate-with-total query needs no
/// <c>-- @type</c> directive and does not come out nullable.</item>
/// </list>
/// </summary>
public class MultiStatementAndWindowTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    private static ColumnRef Expr(QueryModel model, string alias) =>
        model.Columns.Find(c => c.IsExpression && c.OutputAlias == alias)
            ?? throw new Xunit.Sdk.XunitException($"no expression item aliased '{alias}'");

    // ── multiple statements ──

    [Fact]
    public void TwoSelects_AreDetectedAsMultiStatement()
    {
        // Before this check the two statements merged: Tables held bookmarks
        // twice and Columns held both projections, so the emitted mapper read
        // ordinals no single result set has.
        var model = ParseSql(
            "SELECT count(*) AS total FROM bookmarks; SELECT id, title FROM bookmarks");

        Assert.Contains("MULTI_STATEMENT", model.UnsupportedConstructs);
    }

    [Fact]
    public void TwoSelects_AcrossLines_AreDetected()
    {
        var model = ParseSql("SELECT id FROM users;\nSELECT id FROM orders\n");

        Assert.Contains("MULTI_STATEMENT", model.UnsupportedConstructs);
    }

    [Fact]
    public void InsertThenSelect_IsDetected()
    {
        // The INSERT arm of Parse returns as soon as the INSERT is parsed, so
        // the trailing SELECT is not merged — it vanishes. Just as silent, and
        // the detection runs before that early return for exactly this reason.
        var model = ParseSql(
            "INSERT INTO users (name) VALUES (@name); SELECT id FROM users");

        Assert.Contains("MULTI_STATEMENT", model.UnsupportedConstructs);
    }

    [Fact]
    public void WithCte_FollowedBySecondStatement_IsDetected()
    {
        // ParseWith strips every ';' from the final statement's tokens before
        // reparsing it, so a check inside that path would never see one; the
        // detection deliberately runs before the WITH branch.
        var model = ParseSql(
            "WITH recent AS (SELECT id FROM orders) SELECT id FROM recent; SELECT id FROM users");

        Assert.Contains("MULTI_STATEMENT", model.UnsupportedConstructs);
    }

    [Theory]
    [InlineData("SELECT id FROM users")]
    [InlineData("SELECT id FROM users;")]
    [InlineData("SELECT id FROM users;\n")]
    [InlineData("SELECT id FROM users;\n-- a trailing note\n")]
    [InlineData("SELECT id FROM users WHERE name = 'a;b'")]
    [InlineData("WITH recent AS (SELECT id FROM orders) SELECT id FROM recent;")]
    [InlineData("INSERT INTO users (name) VALUES (@name);")]
    public void SingleStatement_IsNotFlagged(string sql)
    {
        // False-positive guard: a terminating ';', a trailing comment (stripped
        // by the tokenizer) and a ';' inside a string literal (a Literal token,
        // never a Symbol) must all stay quiet.
        var model = ParseSql(sql);

        Assert.DoesNotContain("MULTI_STATEMENT", model.UnsupportedConstructs);
    }

    [Fact]
    public void SubqueryContainingSemicolonLiteral_IsNotFlagged()
    {
        var model = ParseSql(
            "SELECT id FROM users WHERE id IN (SELECT user_id FROM tags WHERE label = 'a;b')");

        Assert.DoesNotContain("MULTI_STATEMENT", model.UnsupportedConstructs);
    }

    // ── window COUNT inference ──

    [Fact]
    public void CountStarOverEmptyWindow_InfersBigintNotNull()
    {
        var model = ParseSql(
            "SELECT id, COUNT(*) OVER() AS total FROM bookmarks ORDER BY id");

        var col = Expr(model, "total");
        Assert.Equal("bigint", col.InferredDbType);
        Assert.True(col.InferredNotNull);
    }

    [Theory]
    [InlineData("COUNT(*) OVER ()")]
    [InlineData("COUNT(*) OVER (PARTITION BY user_id)")]
    [InlineData("count(id) OVER (PARTITION BY user_id ORDER BY created_at)")]
    [InlineData("COUNT(DISTINCT id) OVER (PARTITION BY user_id)")]
    public void CountOverWindow_InfersBigint(string expression)
    {
        var model = ParseSql($"SELECT {expression} AS total FROM bookmarks");

        var col = Expr(model, "total");
        Assert.Equal("bigint", col.InferredDbType);
        Assert.True(col.InferredNotNull);
    }

    [Fact]
    public void PlainCountStar_StillInfersBigint()
    {
        // The non-window path must survive the shape change.
        var model = ParseSql("SELECT COUNT(*) AS total FROM bookmarks");

        var col = Expr(model, "total");
        Assert.Equal("bigint", col.InferredDbType);
        Assert.True(col.InferredNotNull);
    }

    [Theory]
    [InlineData("COUNT(*) OVER() + 1")]
    [InlineData("COUNT(*) OVER() * 2")]
    [InlineData("COUNT(*) OVER w")]
    [InlineData("ROW_NUMBER() OVER (ORDER BY id)")]
    [InlineData("SUM(amount) OVER (PARTITION BY user_id)")]
    public void NotAWholeCountWindow_StaysUnresolved(string expression)
    {
        // The OVER clause must consume the whole run, exactly as the plain
        // call must: an arithmetic combination is not bigint-shaped. A named
        // window ("OVER w") is defined in a WINDOW clause this parser does not
        // model, and ROW_NUMBER/SUM are deliberately out of scope — all four
        // still require -- @type rather than getting a wrong type for free.
        var model = ParseSql($"SELECT {expression} AS val FROM bookmarks");

        var col = Expr(model, "val");
        Assert.Equal(string.Empty, col.InferredDbType);
        Assert.False(col.InferredNotNull);
    }

    [Fact]
    public void CountOverWindow_ComparedToLiteral_IsBoolean()
    {
        // Top-level comparison still wins over the count head, window or not.
        var model = ParseSql("SELECT COUNT(*) OVER() > 0 AS any_rows FROM bookmarks");

        var col = Expr(model, "any_rows");
        Assert.Equal("boolean", col.InferredDbType);
        Assert.True(col.InferredNotNull);
    }
}
