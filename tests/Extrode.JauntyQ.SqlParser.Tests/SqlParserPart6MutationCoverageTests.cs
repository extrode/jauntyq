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
        // after it must still be excluded from the captured tokens. The
        // innerDepth increment/decrement/comparison mutants themselves appear
        // equivalent for any tokenizable SQL reachable through this parser: a
        // ';' Symbol inside real parens here would require a nested raw
        // statement terminator, which is not a shape this grammar produces.
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

    // SelectAsVeryFirstToken_IsNotMistakenForAnExistsSubquery removed:
    // IsExistsSubquery's only call site already requires `i > 0` before
    // calling it, so selectIndex == 0 can never reach the `< 2` guard through
    // the real Parse entry point -- "SELECT id FROM products" never engages
    // this function at all, so the assertion passed for a reason unrelated to
    // the guard it named.

    [Fact]
    public void SelectAsSecondToken_IsNotMistakenForAnExistsSubquery()
    {
        // selectIndex == 1 IS reachable (e.g. a leading-paren statement like
        // "(SELECT 1) UNION (SELECT 2)"): without the < 2 guard,
        // tokens[selectIndex - 2] would be tokens[-1], an out-of-range index.
        // Guarded, selectIndex == 1 returns false (not an EXISTS subquery),
        // so the caller records SUBQUERY -- the guard prevents a crash, it
        // does not prevent the SUBQUERY classification.
        var tokens = new List<Token>
        {
            new(TokenType.Symbol, "("),
            new(TokenType.Keyword, "SELECT"),
            new(TokenType.Symbol, "*"),
            new(TokenType.End, string.Empty),
        };

        var model = SqlParser.Parse(tokens, "q");

        Assert.Contains("SUBQUERY", model.UnsupportedConstructs);
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
        // Two tokens back from SELECT is "," here, same as "SELECT" in the
        // test above -- neither is a Keyword-typed "EXISTS", so this adds no
        // independent kill power over ParenPrecededSelect_NotExists_IsRecordedAsSubquery.
        // Kept as a plain regression check.
        var model = ParseSql("SELECT x, (SELECT MAX(id) FROM orders) AS last_id FROM users");

        Assert.Contains("SUBQUERY", model.UnsupportedConstructs);
    }

    [Fact]
    public void ParenPrecededSelect_TwoBackHasExistsTextButWrongTokenType_IsStillASubquery()
    {
        // The tokenizer always classifies the word EXISTS as a Keyword, so no
        // SQL string can produce a non-Keyword "EXISTS" token -- constructing
        // tokens directly is the only way to isolate the Type == Keyword half
        // of the check from the Value == "EXISTS" half. Here tokens[selectIndex
        // - 2] has Value "EXISTS" but Type Identifier: only the Type check
        // (not just text-matching) tells this apart from a real EXISTS(...).
        var tokens = new List<Token>
        {
            new(TokenType.Keyword, "SELECT"),
            new(TokenType.Identifier, "EXISTS"),
            new(TokenType.Symbol, "("),
            new(TokenType.Keyword, "SELECT"),
            new(TokenType.Keyword, "MAX"),
            new(TokenType.Symbol, "("),
            new(TokenType.Identifier, "id"),
            new(TokenType.Symbol, ")"),
            new(TokenType.Keyword, "FROM"),
            new(TokenType.Identifier, "orders"),
            new(TokenType.Symbol, ")"),
            new(TokenType.Keyword, "AS"),
            new(TokenType.Identifier, "last_id"),
            new(TokenType.Keyword, "FROM"),
            new(TokenType.Identifier, "users"),
            new(TokenType.End, string.Empty),
        };

        var model = SqlParser.Parse(tokens, "q");

        Assert.Contains("SUBQUERY", model.UnsupportedConstructs);
    }
}
