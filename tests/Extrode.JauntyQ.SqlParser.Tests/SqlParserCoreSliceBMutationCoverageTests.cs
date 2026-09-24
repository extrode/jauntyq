using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class SqlParserCoreSliceBMutationCoverageTests
{
    private static QueryModel ParseSql(string sql) =>
        SqlParser.Parse(SqlTokenizer.Tokenize(sql), "TestQuery");

    private static ColumnRef Single(QueryModel model) => Assert.Single(model.Columns);

    private static List<Token> TokensWithoutEnd(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        Assert.Equal(TokenType.End, tokens[^1].Type);
        tokens.RemoveAt(tokens.Count - 1);
        return tokens;
    }

    [Theory]
    [InlineData("SELECT a b")]
    [InlineData("SELECT a INTO TEMP")]
    [InlineData("SELECT a INTO #")]
    [InlineData("SELECT a INTO t")]
    [InlineData("SELECT a")]
    [InlineData("SELECT a + b")]
    public void TokenListWithoutEndSentinel_ParsesWithoutThrowing(string sql)
    {
        var tokens = TokensWithoutEnd(sql);

        var ex = Record.Exception(() => SqlParser.Parse(tokens, "TestQuery"));

        Assert.Null(ex);
    }

    [Fact]
    public void LoneIdentifierAtEndOfSentinelFreeList_IsAPlainColumn()
    {
        var model = SqlParser.Parse(TokensWithoutEnd("SELECT a"), "TestQuery");

        var col = Single(model);
        Assert.False(col.IsExpression);
        Assert.Equal("a", col.ColumnName);
        Assert.Empty(model.ExpressionsMissingAlias);
    }

    [Fact]
    public void LiteralOpenParenBeforeRealParens_IsNotStrippedAsAWrappingParen()
    {
        var col = Single(ParseSql("SELECT '(' EXISTS (x) AS e FROM t"));

        Assert.Equal(string.Empty, col.InferredDbType);
    }

    [Fact]
    public void ExistsFollowedOnlyByOpenParen_StillInfersBoolean()
    {
        var col = Single(ParseSql("SELECT EXISTS ( AS e"));

        Assert.Equal("e", col.OutputAlias);
        Assert.Equal("boolean", col.InferredDbType);
    }

    [Fact]
    public void EmptyCountCall_InfersBigint()
    {
        var col = Single(ParseSql("SELECT count() AS n FROM t"));

        Assert.Equal("bigint", col.InferredDbType);
    }

    [Fact]
    public void ImplicitAliasFollowedByOperator_IsNotTakenAsAlias()
    {
        var model = ParseSql("SELECT a b + 1 AS c FROM t");

        Assert.Equal(2, model.Columns.Count);
        Assert.False(model.Columns[0].IsExpression);
        Assert.Equal("a", model.Columns[0].ColumnName);
        Assert.Equal(string.Empty, model.Columns[0].OutputAlias);
        Assert.True(model.Columns[1].IsExpression);
        Assert.Equal("b + 1", model.Columns[1].ExpressionSql);
        Assert.Equal("c", model.Columns[1].OutputAlias);
    }

    [Fact]
    public void ImplicitAliasFollowedByNonClauseKeyword_IsNotTakenAsAlias()
    {
        var model = ParseSql("SELECT a b AS c FROM t");

        Assert.Equal(2, model.Columns.Count);
        Assert.Equal("a", model.Columns[0].ColumnName);
        Assert.Equal(string.Empty, model.Columns[0].OutputAlias);
        Assert.False(model.Columns[1].IsExpression);
        Assert.Equal("b", model.Columns[1].ColumnName);
        Assert.Equal("c", model.Columns[1].OutputAlias);
    }

    [Fact]
    public void SelectIntoTarget_StringLiteralSpelledTemp_IsNotSkippedAsAModifier()
    {
        var model = ParseSql("SELECT a INTO 'TEMP' x FROM t");

        Assert.Contains("SELECT INTO", model.UnsupportedConstructs);
        Assert.Equal(new[] { "TEMP x" }, model.ExpressionsMissingAlias);
    }

    [Fact]
    public void SelectIntoTarget_StringLiteralHash_IsNotSkippedAsATempTableMarker()
    {
        var model = ParseSql("SELECT a INTO '#' x FROM t");

        Assert.Contains("SELECT INTO", model.UnsupportedConstructs);
        Assert.Equal(new[] { "# x" }, model.ExpressionsMissingAlias);
    }

    [Fact]
    public void IdentifierFollowedByQuotedClauseWord_IsAnExpressionNotAPlainColumn()
    {
        var model = ParseSql("SELECT a [WHERE] FROM t");

        var col = Single(model);
        Assert.True(col.IsExpression);
        Assert.Equal("a WHERE", col.ExpressionSql);
    }

    [Fact]
    public void ExpressionItemOfOnlyAsAlias_TakesTheAlias()
    {
        var model = ParseSql("SELECT AS b FROM t");

        var col = Single(model);
        Assert.Equal("b", col.OutputAlias);
        Assert.Equal(string.Empty, col.ExpressionSql);
        Assert.Empty(model.ExpressionsMissingAlias);
    }

    [Fact]
    public void MultiTokenExpression_RendersSpaceSeparatedWithoutLeadingSpace()
    {
        var model = ParseSql("SELECT a + b AS s FROM t");

        Assert.Equal("a + b", Single(model).ExpressionSql);
    }

    [Fact]
    public void ParenthesisedComparison_IsStrippedAndInfersBoolean()
    {
        var col = Single(ParseSql("SELECT (a = 1) AS b FROM t"));

        Assert.Equal("boolean", col.InferredDbType);
        Assert.True(col.InferredNotNull);
    }

    [Fact]
    public void ParenthesisedQuotedIdentifierNamedSelect_IsStillStripped()
    {
        var col = Single(ParseSql("SELECT ([SELECT] = 1) AS b FROM t"));

        Assert.Equal("boolean", col.InferredDbType);
    }

    [Fact]
    public void ParenthesisedCount_IsStrippedAndInfersBigint()
    {
        var col = Single(ParseSql("SELECT (count(*)) AS n FROM t"));

        Assert.Equal("bigint", col.InferredDbType);
        Assert.True(col.InferredNotNull);
    }

    [Fact]
    public void ParenthesisedSum_IsStrippedAndCapturesAggregate()
    {
        var col = Single(ParseSql("SELECT (SUM(amount)) AS total FROM orders"));

        Assert.Equal("SUM", col.AggregateFunction);
        Assert.Equal("amount", col.AggregateArgColumnName);
    }

    [Fact]
    public void ComparisonAfterCaseEnd_IsTopLevelAndInfersBoolean()
    {
        var col = Single(ParseSql("SELECT CASE WHEN x = 1 THEN 1 ELSE 0 END = 1 AS b FROM t"));

        Assert.Equal("boolean", col.InferredDbType);
    }

    [Fact]
    public void UnbalancedOuterParens_AreNotStripped()
    {
        var model = ParseSql("SELECT (a = 1 ()");

        var col = Single(model);
        Assert.True(col.IsExpression);
        Assert.Equal(string.Empty, col.InferredDbType);
    }

    [Fact]
    public void UnclosedCountCall_DoesNotInferBigint()
    {
        var col = Single(ParseSql("SELECT count(x"));

        Assert.Equal(string.Empty, col.InferredDbType);
    }

    [Fact]
    public void UnclosedCountCallContainingOverClause_DoesNotInferBigint()
    {
        var col = Single(ParseSql("SELECT count(OVER (x)"));

        Assert.Equal(string.Empty, col.InferredDbType);
    }

    [Fact]
    public void CountFollowedByBareOver_DoesNotInferBigint()
    {
        var col = Single(ParseSql("SELECT count(x) OVER"));

        Assert.Equal(string.Empty, col.InferredDbType);
    }

    [Theory]
    [InlineData("SELECT count(x) OVER + (y) AS n FROM t")]
    [InlineData("SELECT count(x) OVER '(' (y) AS n FROM t")]
    [InlineData("SELECT count(x) 'OVER' (y) AS n FROM t")]
    public void CountFollowedByMalformedWindow_DoesNotInferBigint(string sql)
    {
        var col = Single(ParseSql(sql));

        Assert.Equal(string.Empty, col.InferredDbType);
    }

    [Fact]
    public void QuotedCountIdentifier_IsACountHead()
    {
        var col = Single(ParseSql("SELECT \"count\"(*) AS n FROM t"));

        Assert.Equal("bigint", col.InferredDbType);
    }

    [Fact]
    public void BracketedSumIdentifier_IsASumHead()
    {
        var col = Single(ParseSql("SELECT [sum](amount) AS s FROM t"));

        Assert.Equal("SUM", col.AggregateFunction);
        Assert.Equal("amount", col.AggregateArgColumnName);
    }

    [Theory]
    [InlineData("SELECT MAX(amount) AS s FROM t")]
    [InlineData("SELECT foo(amount) AS s FROM t")]
    [InlineData("SELECT 'SUM'(amount) AS s FROM t")]
    public void NonSumAvgHead_CapturesNoAggregate(string sql)
    {
        var col = Single(ParseSql(sql));

        Assert.Equal(string.Empty, col.AggregateFunction);
        Assert.Equal(string.Empty, col.AggregateArgColumnName);
    }
}
