using System.Collections.Generic;
using System.Linq;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class SqlParserPart6Part7TokenizerSurvivorTests
{
    private static QueryModel ParseSql(string sql, string name = "Q")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    private static QueryModel ParseRaw(params Token[] tokens)
        => SqlParser.Parse(new List<Token>(tokens), "Q");

    private static Token Kw(string v) => new(TokenType.Keyword, v);
    private static Token Id(string v) => new(TokenType.Identifier, v);

    [Fact]
    public void DeleteOfATableQuotedAsFrom_KeepsItAsTheTarget()
    {
        var model = ParseSql("DELETE [FROM] WHERE id = @id");

        Assert.Equal("FROM", model.TargetTable);
        Assert.Equal("FROM", Assert.Single(model.Tables).TableName);
    }

    [Fact]
    public void DeleteAsTheLastToken_WithNoEndSentinel_ParsesWithoutATable()
    {
        var model = ParseRaw(Kw("DELETE"));

        Assert.Equal(StatementType.Delete, model.StatementType);
        Assert.Null(model.TargetTable);
        Assert.Empty(model.Tables);
    }

    [Fact]
    public void DeleteFromAsTheLastTokens_WithNoEndSentinel_ParsesWithoutATable()
    {
        var model = ParseRaw(Kw("DELETE"), Kw("FROM"));

        Assert.Null(model.TargetTable);
        Assert.Empty(model.Tables);
    }

    [Fact]
    public void ReturningAsTheLastToken_WithNoEndSentinel_RecordsAnEmptyReturningList()
    {
        var model = ParseRaw(Kw("DELETE"), Kw("FROM"), Id("t"), Kw("RETURNING"));

        Assert.Equal("t", model.TargetTable);
        Assert.True(model.HasReturning);
        Assert.Empty(model.Returning);
    }

    [Theory]
    [InlineData("SELECT 1; ()")]
    [InlineData("SELECT 1; )")]
    [InlineData("SELECT 1; (")]
    public void ParensAfterTheTerminator_AreASecondStatement(string sql)
    {
        Assert.Contains("MULTI_STATEMENT", ParseSql(sql).UnsupportedConstructs);
    }

    [Theory]
    [InlineData("SELECT (1);")]
    [InlineData("SELECT (1);;")]
    public void ParensBeforeTheTerminator_AreNotASecondStatement(string sql)
    {
        Assert.DoesNotContain("MULTI_STATEMENT", ParseSql(sql).UnsupportedConstructs);
    }

    [Fact]
    public void ACteQuotedAsRecursive_IsAnOrdinaryCte()
    {
        var model = ParseSql("WITH [RECURSIVE] AS (SELECT 1 AS x) SELECT x FROM [RECURSIVE]");

        Assert.False(model.WithRecursive);
        Assert.DoesNotContain("WITH RECURSIVE", model.UnsupportedConstructs);
        Assert.Equal("RECURSIVE", Assert.Single(model.Ctes).Name);
    }

    [Fact]
    public void AQuotedParenAfterTheCteName_IsNotADeclaredColumnList()
    {
        var model = ParseSql("WITH c [(] a ) AS (SELECT 1 AS x) SELECT x FROM c");

        Assert.Empty(model.Ctes);
    }

    [Fact]
    public void AQuotedParenAfterAs_IsNotACteBody()
    {
        var model = ParseSql("WITH c AS [(] SELECT 1 AS x) SELECT x FROM c");

        Assert.Empty(model.Ctes);
    }

    [Fact]
    public void AQuotedCommaAfterACteBody_DoesNotStartAnotherCte()
    {
        var model = ParseSql("WITH a AS (SELECT 1 AS x) [,] b AS (SELECT 2 AS y) SELECT x FROM a");

        Assert.Equal("a", Assert.Single(model.Ctes).Name);
    }

    [Fact]
    public void ACteBodyModel_IsNamedAfterTheQueryAndTheCte()
    {
        var model = ParseSql("WITH recent AS (SELECT id FROM orders) SELECT id FROM recent", "GetRecent");

        Assert.Equal("GetRecent_recent", Assert.Single(model.Ctes).Body.Name);
    }

    [Theory]
    [InlineData("SELECT a OVER")]
    [InlineData("SELECT a FROM t")]
    public void ACteBody_ParsesTheSameAsTheStandaloneStatement(string body)
    {
        var standalone = ParseSql(body);
        var cte = Assert.Single(ParseSql($"WITH c AS ({body}) SELECT a FROM c").Ctes);

        Assert.Equal(
            standalone.Columns.Select(c => (c.ColumnName, c.OutputAlias, c.IsExpression)),
            cte.Body.Columns.Select(c => (c.ColumnName, c.OutputAlias, c.IsExpression)));
        Assert.Equal(standalone.Tables.Select(t => t.TableName), cte.Body.Tables.Select(t => t.TableName));
    }

    [Fact]
    public void ATrailingTerminatorAfterTheFinalStatement_IsNotPartOfItsLastColumn()
    {
        var model = ParseSql("WITH c AS (SELECT a FROM t) SELECT a;");

        var column = Assert.Single(model.Columns);
        Assert.Equal("a", column.ColumnName);
        Assert.False(column.IsExpression);
        Assert.Empty(model.ExpressionsMissingAlias);
    }

    [Fact]
    public void ABlockCommentEndingInAStarAtEndOfInput_IsUnterminated()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT 1 /* x *");

        Assert.Equal(TokenType.Unterminated, tokens[^2].Type);
        Assert.Equal(TokenType.End, tokens[^1].Type);
    }
}
