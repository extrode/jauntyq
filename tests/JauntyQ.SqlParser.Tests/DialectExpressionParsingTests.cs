using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

/// <summary>
/// AUD-R11 (§2.1-r12-concat, §2.1-r13-cast): JauntyQ does no dialect SQL
/// rewriting of query bodies -- SQL is emitted verbatim. "Dialect
/// translation" for these two matrix cells really means "does the
/// tokenizer/parser handle each dialect's own concat/cast spelling without
/// corrupting the expression shape," the same question AUD-R1-01 already
/// answered for postgres/sqlite's `||` and `::` operators (see
/// ExpressionAndCteTests.ConcatExpression_IsOneExpressionItem_NotTwoColumns
/// and .PostgresCast_IsExpression_NotPhantomColumnLookup). This file covers
/// the two cells AUD-R1-01 never touched: sqlserver's `+` concat operator
/// and MySQL's/sqlserver's function-style CONCAT(...)/CONVERT(...), plus a
/// universal CAST(...) case adversarial enough (nested parens AND a second
/// "AS" keyword inside the cast's own type spec) to actually stress the
/// paren-depth-tracked expression-item parser's trailing-alias peeling.
/// </summary>
public class DialectExpressionParsingTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    [Fact]
    public void SqlServerPlusConcat_IsOneExpressionItem_NotTwoColumns()
    {
        // sqlserver has no '||' string-concat operator; it overloads '+'.
        // '+' is an ordinary arithmetic Symbol token already, so this is a
        // parser-shape question, not a tokenizer one: does the whole
        // "first_name + last_name" run stay merged as one expression item
        // through to the "AS full_name" alias?
        var model = ParseSql("select first_name + last_name as full_name from users");

        var col = Assert.Single(model.Columns);
        Assert.True(col.IsExpression);
        Assert.Equal("full_name", col.OutputAlias);
        Assert.Empty(model.ExpressionsMissingAlias);
        // The silently-wrong old shape for '||' (pre AUD-R1-01) was two
        // separate plain columns; confirm that failure mode doesn't recur
        // here under a completely different operator character.
        Assert.DoesNotContain(model.Columns, c => !c.IsExpression);
    }

    [Fact]
    public void ConcatFunctionCall_IsOneExpressionItem_NotMisreadAsPlainColumn()
    {
        // MySQL (and sqlserver, which also supports it) spell concatenation
        // as a function call rather than an operator. CONCAT is a plain
        // Identifier token (not in SqlTokenizer's Keywords set), so this
        // exercises the *generic* Identifier-followed-by-'(' function-call
        // path rather than the Keyword-headed one CAST/COALESCE use.
        var model = ParseSql("select concat(first_name, ' ', last_name) as full_name from users");

        var col = Assert.Single(model.Columns);
        Assert.True(col.IsExpression);
        Assert.Equal("full_name", col.OutputAlias);
        Assert.Empty(model.ExpressionsMissingAlias);
    }

    [Fact]
    public void ConvertFunctionCall_WithParenthesizedTypeArgument_DoesNotSplitOnInnerComma()
    {
        // sqlserver's CONVERT(type, expr) puts the target type FIRST, and
        // that type can itself carry a parenthesized precision/scale, e.g.
        // VARCHAR(20) -- an inner ',' and inner '(' / ')' pair that the
        // paren-depth-tracked argument scan must not mistake for the
        // function call's own argument separator or closing paren.
        var model = ParseSql("select convert(varchar(20), product_id) as product_id_text from products");

        var col = Assert.Single(model.Columns);
        Assert.True(col.IsExpression);
        Assert.Equal("product_id_text", col.OutputAlias);
        Assert.Empty(model.ExpressionsMissingAlias);
    }

    [Fact]
    public void CastWithParenthesizedPrecisionType_AndTrailingAlias_AliasNotConfusedWithInnerAs()
    {
        // The universal CAST(x AS type) form, made adversarial two ways at
        // once: (1) the target type itself has a parenthesized
        // precision/scale (DECIMAL(10,2)), so there's a nested '(' ... ')'
        // the paren-depth tracker must stay inside of; (2) there is only
        // ONE "AS" token inside the cast (the cast's own), immediately
        // followed by a SECOND, real "AS price" alias outside the
        // function's parens -- the trailing-alias peel must bind to the
        // outer one, not misfire on the inner one or merge the two.
        var model = ParseSql("select cast(unit_price as decimal(10,2)) as price from products");

        var col = Assert.Single(model.Columns);
        Assert.True(col.IsExpression);
        Assert.Equal("price", col.OutputAlias);
        Assert.Empty(model.ExpressionsMissingAlias);
    }
}
