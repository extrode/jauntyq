using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class SqlParserPart4MutationCoverageTests
{
    private static QueryModel ParseSql(string sql)
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), "TestQuery");

    private static QueryModel ParseWithoutEndSentinel(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        Assert.Equal(TokenType.End, tokens[^1].Type);
        tokens.RemoveAt(tokens.Count - 1);
        return SqlParser.Parse(tokens, "TestQuery");
    }

    [Fact]
    public void NoWhereClause_JoinOnColumnEquality_IsNotAPerfHint()
    {
        var model = ParseSql("SELECT a.id FROM a JOIN b ON a.id = b.a_id");

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void WhereAsTheFirstToken_StillBoundsTheRegion()
    {
        var model = ParseSql("WHERE UPPER(name) = 'X'");

        var hint = Assert.Single(model.PerfHints);
        Assert.Equal(PerfHintKind.FunctionOnColumn, hint.Kind);
        Assert.Equal("name", hint.BoundColumnName);
    }

    [Fact]
    public void RegionEndsAtTheFirstBoundaryKeyword_NotTheLast()
    {
        var model = ParseSql(
            "SELECT category_id FROM products WHERE active = 1 " +
            "GROUP BY category_id HAVING UPPER(category_id) = 'X' ORDER BY category_id");

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void FunctionInsideAParenthesisedComparison_IsStillMatchedToItsOwnParen()
    {
        var model = ParseSql("SELECT id FROM t WHERE (UPPER(name) = 'X')");

        var hint = Assert.Single(model.PerfHints);
        Assert.Equal(PerfHintKind.FunctionOnColumn, hint.Kind);
        Assert.Equal("UPPER", hint.FunctionName);
        Assert.Equal("name", hint.BoundColumnName);
    }

    [Fact]
    public void LiteralHoldingAComparisonOperator_AfterAFunction_IsNotAComparison()
    {
        var model = ParseSql("SELECT id FROM t WHERE UPPER(name) '='");

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void LiteralHoldingAComparisonOperator_BeforeAFunction_IsNotAComparison()
    {
        var model = ParseSql("SELECT id FROM t WHERE '=' UPPER(name)");

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void ComparisonOperatorImmediatelyAfterWhere_BeforeAFunction_IsAComparison()
    {
        var model = ParseSql("SELECT id FROM t WHERE = UPPER(name)");

        var hint = Assert.Single(model.PerfHints);
        Assert.Equal(PerfHintKind.FunctionOnColumn, hint.Kind);
        Assert.Equal("name", hint.BoundColumnName);
    }

    [Fact]
    public void LiteralHoldingLike_IsNotTheLikeKeyword()
    {
        var model = ParseSql("SELECT id FROM t WHERE name 'LIKE' '%x'");

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void IdentifierFollowedDirectlyByAWildcardLiteral_IsNotALike()
    {
        var model = ParseSql("SELECT id FROM t WHERE name '%x'");

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void TwoBooleanColumnsJoinedByAnd_AreNotAColumnComparison()
    {
        var model = ParseSql("SELECT id FROM t WHERE active AND deleted");

        Assert.Empty(model.PerfHints);
    }

    [Theory]
    [InlineData("SELECT id FROM t WHERE name")]
    [InlineData("SELECT id FROM t WHERE name LIKE")]
    [InlineData("SELECT id FROM t WHERE name NOT")]
    [InlineData("SELECT id FROM t WHERE name =")]
    [InlineData("SELECT id FROM t WHERE UPPER(name)")]
    [InlineData("SELECT id FROM t WHERE UPPER")]
    public void TokenListWithoutEndSentinel_TruncatedWhereRegion_ParsesWithoutHints(string sql)
    {
        var model = ParseWithoutEndSentinel(sql);

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void TokenListWithoutEndSentinel_CompleteHints_AreStillFound()
    {
        var model = ParseWithoutEndSentinel("SELECT id FROM t WHERE UPPER(name) = 'X' AND a.x = b.y");

        Assert.Equal(3, model.PerfHints.Count);
        Assert.Equal(PerfHintKind.FunctionOnColumn, model.PerfHints[0].Kind);
        Assert.Equal(PerfHintKind.ColumnComparedToColumn, model.PerfHints[1].Kind);
        Assert.Equal(PerfHintKind.ColumnComparedToColumn, model.PerfHints[2].Kind);
    }

    [Fact]
    public void OrInsideParentheses_IsNotTopLevel_SoAndStillSplits()
    {
        var model = ParseSql("SELECT id FROM t WHERE (a = 1 OR b = 2) AND c = 3");

        Assert.Equal(2, model.PredicateAtoms.Count);
    }

    [Fact]
    public void OrAfterAClosedParenthesis_IsTopLevel_SoNothingSplits()
    {
        var model = ParseSql("SELECT id FROM t WHERE (a = 1) AND b = 2 OR c = 3");

        Assert.Single(model.PredicateAtoms);
    }

    [Fact]
    public void LiteralOpenParen_DoesNotHideATopLevelOr()
    {
        var model = ParseSql("SELECT id FROM t WHERE a = '(' AND b = 1 OR c = 2");

        Assert.Single(model.PredicateAtoms);
    }

    [Fact]
    public void LiteralCloseParen_DoesNotExposeANestedOr()
    {
        var model = ParseSql("SELECT id FROM t WHERE (b = ')' OR a = 1) AND c = 3");

        Assert.Equal(2, model.PredicateAtoms.Count);
    }

    [Fact]
    public void BetweenInsideParentheses_DoesNotSwallowTheNextTopLevelAnd()
    {
        var model = ParseSql("SELECT id FROM t WHERE (a BETWEEN 1 AND 2) AND c = 3");

        Assert.Equal(2, model.PredicateAtoms.Count);
    }
}
