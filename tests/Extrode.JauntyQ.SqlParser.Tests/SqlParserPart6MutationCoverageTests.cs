using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>
/// Direct coverage for SqlParser.Part6.cs mutation survivors: DELETE's
/// optional FROM and target-table capture, RETURNING's trailing-';' and
/// nested-paren depth tracking, and IsExistsSubquery's exact
/// two-tokens-back lookback (including its own too-close-to-start guard).
/// </summary>
[Trait("Category", "AuditRegression")]
public class SqlParserPart6MutationCoverageTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    // ── ParseDelete: optional FROM ───────────────────────────────────

    [Fact]
    public void DeleteWithFrom_CapturesTargetTableAndRegistersIt()
    {
        var model = ParseSql("DELETE FROM products WHERE id = @id");

        Assert.Equal("products", model.TargetTable);
        var table = Assert.Single(model.Tables);
        Assert.Equal("products", table.TableName);
        Assert.Empty(table.Alias);
    }

    [Fact]
    public void DeleteWithoutFrom_StillCapturesTargetTable()
    {
        var model = ParseSql("DELETE products WHERE id = @id");

        Assert.Equal("products", model.TargetTable);
        var table = Assert.Single(model.Tables);
        Assert.Equal("products", table.TableName);
    }

    // ── ExtractReturning: trailing ';' must not be swallowed into the item ──

    [Fact]
    public void Returning_TrailingSemicolon_DoesNotDemoteLastColumnToAnExpression()
    {
        var model = ParseSql("INSERT INTO products (name) VALUES (@n) RETURNING product_id;");

        var col = Assert.Single(model.Returning);
        Assert.False(col.IsExpression);
        Assert.Equal("product_id", col.ColumnName);
    }

    [Fact]
    public void Returning_NoTrailingSemicolon_StillWorks()
    {
        var model = ParseSql("INSERT INTO products (name) VALUES (@n) RETURNING product_id");

        var col = Assert.Single(model.Returning);
        Assert.Equal("product_id", col.ColumnName);
    }

    [Fact]
    public void Returning_MultipleColumns_SplitOnTopLevelCommaOnly()
    {
        var model = ParseSql("INSERT INTO products (name) VALUES (@n) RETURNING product_id, name");

        Assert.Equal(2, model.Returning.Count);
    }

    [Fact]
    public void Returning_ExpressionWithNestedParens_TrailingSemicolonStillTerminatesTheWholeList()
    {
        // The ';' scan tracks innerDepth so a ')' inside the expression's own
        // parens does not end the RETURNING list early, and the trailing ';'
        // after it must still be excluded from the captured tokens.
        var model = ParseSql(
            "INSERT INTO products (name) VALUES (@n) RETURNING round(price, 2) AS rounded_price;");

        var col = Assert.Single(model.Returning);
        Assert.True(col.IsExpression);
        Assert.Equal("rounded_price", col.OutputAlias);
    }

    [Fact]
    public void NoReturningClause_HasReturningIsFalse()
    {
        var model = ParseSql("INSERT INTO products (name) VALUES (@n)");

        Assert.False(model.HasReturning);
        Assert.Empty(model.Returning);
    }

    // ── IsExistsSubquery: exact two-token lookback + near-start guard ────

    [Fact]
    public void SelectAsVeryFirstToken_IsNotMistakenForAnExistsSubquery()
    {
        // selectIndex == 0: the < 2 guard must short-circuit before indexing
        // tokens[-1]/tokens[-2], which would otherwise throw.
        var ex = Record.Exception(() => ParseSql("SELECT id FROM products"));

        Assert.Null(ex);
    }

    [Fact]
    public void SelectAsSecondToken_IsNotMistakenForAnExistsSubquery()
    {
        // selectIndex == 1: still short-circuited by the < 2 guard (there is
        // no tokens[-1]); must not throw and must not record SUBQUERY.
        var tokens = new List<Token>
        {
            new(TokenType.Symbol, "("),
            new(TokenType.Keyword, "SELECT"),
            new(TokenType.Symbol, "*"),
            new(TokenType.End, string.Empty),
        };

        var ex = Record.Exception(() => SqlParser.Parse(tokens, "q"));

        Assert.Null(ex);
    }

    [Fact]
    public void ParenPrecededSelect_NotExists_IsRecordedAsSubquery()
    {
        var model = ParseSql("SELECT (SELECT MAX(id) FROM orders) AS last_id FROM users");

        Assert.Contains("SUBQUERY", model.UnsupportedConstructs);
    }

    [Fact]
    public void ExistsParenSelect_IsNotRecordedAsAFreeStandingSubquery()
    {
        // EXISTS(SELECT ...) as a projection expression (Feature A) must be
        // excluded from the generic paren-preceded-SELECT subquery check.
        var model = ParseSql("SELECT EXISTS(SELECT 1 FROM orders) AS has_orders FROM users");

        Assert.DoesNotContain("SUBQUERY", model.UnsupportedConstructs);
    }

    [Fact]
    public void ParenPrecededSelect_WhereTokenTwoBackIsNotExists_IsStillASubquery()
    {
        // Two tokens back from SELECT is "AS", not EXISTS -- the exact-keyword
        // match on tokens[selectIndex - 2].Value == "EXISTS" must require the
        // real keyword, not just any keyword two back.
        var model = ParseSql("SELECT x, (SELECT MAX(id) FROM orders) AS last_id FROM users");

        Assert.Contains("SUBQUERY", model.UnsupportedConstructs);
    }
}
