using System.Linq;
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

    [Fact]
    public void DeleteWithSomeOtherKeywordInsteadOfFrom_DoesNotSkipTheTableName()
    {
        // The optional-FROM guard is `pos < tokens.Count && Type == Keyword &&
        // Value == "FROM"`. A Keyword token that ISN'T literally "FROM" (e.g.
        // this malformed "DELETE WHERE ..." with no table at all) must leave
        // pos untouched -- if the guard treated any keyword as good enough to
        // skip, "WHERE" itself would be consumed as if it were FROM, and the
        // next check would then misread "id" (from "id = @id") as the table
        // name instead of correctly finding no table name at all.
        var tokens = new List<Token>
        {
            new(TokenType.Keyword, "DELETE"),
            new(TokenType.Keyword, "WHERE"),
            new(TokenType.Identifier, "id"),
            new(TokenType.Symbol, "="),
            new(TokenType.Parameter, "id"),
            new(TokenType.End, string.Empty),
        };

        var model = SqlParser.Parse(tokens, "q");

        Assert.Null(model.TargetTable);
        Assert.Empty(model.Tables);
    }

    [Fact]
    public void DeleteWithNothingAfterIt_DoesNotCrashAndCapturesNoTable()
    {
        // The table-name guard is `pos < tokens.Count && Type == Identifier`.
        // Reducing it to just the bounds check (forcing it always true) would
        // read the trailing End sentinel itself as if it were the table name.
        var tokens = new List<Token>
        {
            new(TokenType.Keyword, "DELETE"),
            new(TokenType.End, string.Empty),
        };

        var model = SqlParser.Parse(tokens, "q");

        Assert.Null(model.TargetTable);
        Assert.Empty(model.Tables);
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

    [Fact]
    public void ReturningKeywordInsideNestedParens_IsNotTreatedAsTheRealReturningClause()
    {
        // The scan's `depth == 0 && Type == Keyword && Value == "RETURNING"`
        // guard only fires at top-level depth. A RETURNING token appearing
        // inside parens elsewhere in the token stream (never produced by
        // SqlTokenizer from real SQL, but the parser accepts any token list)
        // must be skipped: without the depth==0 half of the check, this would
        // be misread as the statement's own RETURNING clause.
        var tokens = new List<Token>
        {
            new(TokenType.Keyword, "DELETE"),
            new(TokenType.Keyword, "FROM"),
            new(TokenType.Identifier, "t"),
            new(TokenType.Symbol, "("),
            new(TokenType.Keyword, "RETURNING"),
            new(TokenType.Symbol, ")"),
            new(TokenType.End, string.Empty),
        };

        var model = SqlParser.Parse(tokens, "q");

        Assert.False(model.HasReturning);
        Assert.Empty(model.Returning);
    }

    [Fact]
    public void Returning_ImplicitAliasOnTheVeryLastColumn_IsCapturedNotSplitIntoASecondColumn()
    {
        // The implicit-alias lookahead ("RETURNING col alias" with no AS)
        // requires `pos + 2 < tokens.Count` and then `afterAlias.Type ==
        // TokenType.End` when the alias is the very last real token. This
        // exercises that exact boundary: without the projection list's own
        // trailing End sentinel in place, the bounds check fails and "last_id"
        // is captured as a second, separate (wrong) column instead of an
        // alias on "product_id".
        var model = ParseSql("INSERT INTO products (name) VALUES (@n) RETURNING product_id last_id");

        var col = Assert.Single(model.Returning);
        Assert.Equal("product_id", col.ColumnName);
        Assert.Equal("last_id", col.OutputAlias);
    }

    [Fact]
    public void SecondTopLevelReturningKeyword_IsIgnored_FirstOneWins()
    {
        // ExtractReturning returns immediately after handling the first
        // top-level RETURNING it finds. A second (malformed) RETURNING later
        // in the stream must not re-trigger parsing and append/overwrite
        // model.Returning a second time.
        var tokens = new List<Token>
        {
            new(TokenType.Keyword, "DELETE"),
            new(TokenType.Keyword, "FROM"),
            new(TokenType.Identifier, "t"),
            new(TokenType.Keyword, "RETURNING"),
            new(TokenType.Identifier, "id"),
            new(TokenType.Keyword, "RETURNING"),
            new(TokenType.Identifier, "name"),
            new(TokenType.End, string.Empty),
        };

        var model = SqlParser.Parse(tokens, "q");

        var col = Assert.Single(model.Returning);
        Assert.Equal("id", col.ColumnName);
    }

    [Fact]
    public void Returning_TrailingSemicolonInsideNestedParens_DoesNotEndTheListEarly()
    {
        // The trailing-';' scan's guard is `innerDepth == 0 && Type == Symbol
        // && Value == ";"`. A ';' Symbol token nested inside parens (again,
        // not producible from real SQL text, but a valid token list) must not
        // end the captured run early -- everything up to the REAL top-level
        // terminator (or End) belongs to the expression.
        var tokens = new List<Token>
        {
            new(TokenType.Keyword, "INSERT"),
            new(TokenType.Keyword, "INTO"),
            new(TokenType.Identifier, "t"),
            new(TokenType.Symbol, "("),
            new(TokenType.Identifier, "a"),
            new(TokenType.Symbol, ")"),
            new(TokenType.Keyword, "VALUES"),
            new(TokenType.Symbol, "("),
            new(TokenType.Parameter, "a"),
            new(TokenType.Symbol, ")"),
            new(TokenType.Keyword, "RETURNING"),
            new(TokenType.Identifier, "fn"),
            new(TokenType.Symbol, "("),
            new(TokenType.Identifier, "x"),
            new(TokenType.Symbol, ";"),
            new(TokenType.Identifier, "y"),
            new(TokenType.Symbol, ")"),
            new(TokenType.Keyword, "AS"),
            new(TokenType.Identifier, "z"),
            new(TokenType.Symbol, ";"),
            new(TokenType.End, string.Empty),
        };

        var model = SqlParser.Parse(tokens, "q");

        var col = Assert.Single(model.Returning);
        Assert.True(col.IsExpression);
        Assert.Equal("z", col.OutputAlias);
    }

    // ── DetectMultipleStatements: ';' inside parens is not a terminator ──

    [Fact]
    public void SemicolonInsideParens_DoesNotFlagMultiStatement()
    {
        // The terminator guard is `depth == 0 && Type == Symbol && Value ==
        // ";"`. A ';' nested inside parens (not producible from real SQL by
        // the tokenizer, but a valid token list) must not set `terminated`:
        // without the depth==0 half, later tokens still inside those parens
        // would be misread as trailing content after a statement terminator.
        var tokens = new List<Token>
        {
            new(TokenType.Keyword, "SELECT"),
            new(TokenType.Symbol, "("),
            new(TokenType.Identifier, "a"),
            new(TokenType.Symbol, ";"),
            new(TokenType.Identifier, "b"),
            new(TokenType.Symbol, ")"),
            new(TokenType.Keyword, "FROM"),
            new(TokenType.Identifier, "t"),
            new(TokenType.End, string.Empty),
        };

        var model = SqlParser.Parse(tokens, "q");

        Assert.DoesNotContain("MULTI_STATEMENT", model.UnsupportedConstructs);
    }

    // ── DetectUnsupportedConstructs: SUBQUERY/UNION dedup guard ──────────

    [Fact]
    public void MultipleParenPrecededSubqueries_RecordSubqueryOnlyOnce()
    {
        // The dedup guard `if (!Contains("SUBQUERY")) Add("SUBQUERY")` must
        // actually prevent duplicates across multiple occurrences in one
        // query -- UnsupportedConstructs is a List, so a broken guard would
        // let a second occurrence append a second entry.
        var model = ParseSql(
            "SELECT (SELECT MAX(id) FROM orders) AS a, (SELECT MIN(id) FROM orders) AS b FROM users");

        Assert.Equal(1, model.UnsupportedConstructs.Count(c => c == "SUBQUERY"));
    }

    [Fact]
    public void MultipleUnionKeywords_RecordUnionOnlyOnce()
    {
        var model = ParseSql("SELECT a FROM t1 UNION SELECT b FROM t2 UNION SELECT c FROM t3");

        Assert.Equal(1, model.UnsupportedConstructs.Count(c => c == "UNION"));
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
    public void SelectAsThirdToken_WithGenuineExistsPattern_IsRecognizedAsExists()
    {
        // selectIndex == 2 is the exact boundary of the `selectIndex < 2`
        // guard: 2 < 2 is false, so the guard must NOT reject it -- tokens[0]
        // and tokens[1] are still perfectly valid to inspect. A `<=` boundary
        // mutant would incorrectly reject this and return false, causing the
        // caller to misclassify a genuine EXISTS(...) as a free-standing
        // SUBQUERY.
        var tokens = new List<Token>
        {
            new(TokenType.Keyword, "EXISTS"),
            new(TokenType.Symbol, "("),
            new(TokenType.Keyword, "SELECT"),
            new(TokenType.Symbol, "*"),
            new(TokenType.Keyword, "FROM"),
            new(TokenType.Identifier, "t"),
            new(TokenType.Symbol, ")"),
            new(TokenType.End, string.Empty),
        };

        var model = SqlParser.Parse(tokens, "q");

        Assert.DoesNotContain("SUBQUERY", model.UnsupportedConstructs);
    }

    [Fact]
    public void ExistsParenSelect_WithMultiTokenProjection_StillNotRecordedAsSubquery()
    {
        // The two "selectIndex - 2" reads (one for .Type, one for .Value) must
        // both point at the SAME token two positions back from the nested
        // SELECT. A wide multi-column projection is used specifically so that
        // "FROM" does NOT coincidentally land two tokens after SELECT (a
        // narrower projection like "SELECT 1" would let an offset-by-4 bug
        // hide behind FROM also being a Keyword).
        var model = ParseSql("SELECT EXISTS(SELECT id, name FROM t) AS has_match FROM users");

        Assert.DoesNotContain("SUBQUERY", model.UnsupportedConstructs);
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
