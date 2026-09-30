using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class SqlParserDisableAuditSqlParserBTests
{
    private static QueryModel ParseSql(string sql) => SqlParser.Parse(SqlTokenizer.Tokenize(sql), "q");

    private static QueryModel ParseWithoutEnd(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        Assert.Equal(TokenType.End, tokens[^1].Type);
        tokens.RemoveAt(tokens.Count - 1);
        return SqlParser.Parse(tokens, "q");
    }

    private static string Shape(QueryModel m) => string.Join("\n",
        $"type={m.StatementType} target={m.TargetTable}",
        "cols=" + string.Join(";", m.Columns.Select(c => $"{c.TableAlias}|{c.ColumnName}|{c.OutputAlias}")),
        "tables=" + string.Join(";", m.Tables.Select(t => $"{t.TableName}|{t.Alias}")),
        "params=" + string.Join(";", m.Parameters.Select(p => $"{p.Name}|{p.BoundTableAlias}|{p.BoundColumnName}|{p.ComparisonOp}")),
        "literals=" + string.Join(";", m.Literals.Select(l => $"{l.Kind}|{l.Value}|{l.BoundColumnName}")),
        "ctes=" + string.Join(";", m.Ctes.Select(c => $"{c.Name}|{string.Join(",", c.VirtualColumns)}")),
        "unsupported=" + string.Join(";", m.UnsupportedConstructs));

    private static ParameterRef Param(QueryModel m, string name) => Assert.Single(m.Parameters, p => p.Name == name);

    [Theory]
    [InlineData("DELETE FROM t WHERE a =")]
    [InlineData("DELETE FROM t WHERE a IN (")]
    [InlineData("DELETE FROM t WHERE a LIKE")]
    [InlineData("DELETE FROM t WHERE a BETWEEN")]
    [InlineData("DELETE FROM t WHERE a BETWEEN @lo")]
    [InlineData("DELETE FROM t WHERE a BETWEEN @lo AND")]
    [InlineData("SELECT a FROM t WHERE b =")]
    [InlineData("SELECT a FROM t WHERE b = -")]
    [InlineData("SELECT a FROM t WHERE a IN")]
    [InlineData("SELECT a FROM t WHERE a IN ( -")]
    [InlineData("WITH")]
    [InlineData("WITH x")]
    [InlineData("WITH x AS")]
    [InlineData("WITH x AS ( SELECT 1 )")]
    public void TruncatedTokenListWithoutEnd_DoesNotThrow(string sql)
    {
        Assert.Null(Record.Exception(() => ParseWithoutEnd(sql)));
    }

    [Theory]
    [InlineData("WITH x (a")]
    [InlineData("WITH x AS (SELECT 1")]
    public void MalformedWith_DoesNotThrow(string sql)
    {
        var model = ParseSql(sql);

        Assert.Equal(StatementType.Select, model.StatementType);
        Assert.Empty(model.Ctes);
    }

    [Theory]
    [InlineData("DELETE FROM t WHERE a IN (@p)")]
    [InlineData("WITH x AS (SELECT 1 AS n) DELETE FROM t WHERE id = @id")]
    [InlineData("WITH d AS (DELETE FROM t WHERE id = @id RETURNING id) SELECT id FROM d")]
    [InlineData("WITH x (n) AS (SELECT 1), y AS (SELECT n FROM x WHERE n BETWEEN @lo AND @hi) SELECT n FROM y WHERE n LIKE @s")]
    public void TokenListWithoutEnd_ParsesAsTheEndTerminatedList(string sql)
    {
        Assert.Equal(Shape(ParseSql(sql)), Shape(ParseWithoutEnd(sql)));
    }

    [Fact]
    public void ComparisonBinding_BindsTheParameter()
    {
        var p = Param(ParseSql("DELETE FROM t WHERE id = @id"), "id");

        Assert.Equal("id", p.BoundColumnName);
        Assert.Equal("=", p.ComparisonOp);
    }

    [Fact]
    public void InBinding_WithoutEnd_BindsATrailingParameter()
    {
        var p = Param(ParseWithoutEnd("DELETE FROM t WHERE a IN ( @p"), "p");

        Assert.Equal("a", p.BoundColumnName);
        Assert.Equal("IN", p.ComparisonOp);
    }

    [Fact]
    public void BetweenBinding_BindsBothBounds()
    {
        var m = ParseSql("DELETE FROM t WHERE a BETWEEN @lo AND @hi");

        Assert.Equal("BETWEEN", Param(m, "lo").ComparisonOp);
        Assert.Equal("a", Param(m, "hi").BoundColumnName);
        Assert.Equal("BETWEEN", Param(m, "hi").ComparisonOp);
    }

    [Fact]
    public void BetweenBinding_UpperBoundNotAfterAnd_StaysUnbound()
    {
        var m = ParseSql("DELETE FROM t WHERE a BETWEEN @lo + @p AND 5");

        Assert.Equal("a", Param(m, "lo").BoundColumnName);
        Assert.Equal(string.Empty, Param(m, "p").BoundColumnName);
    }

    [Fact]
    public void BetweenBinding_AndThatIsNotAKeyword_StaysUnbound()
    {
        var tokens = new List<Token>
        {
            new(TokenType.Keyword, "DELETE"), new(TokenType.Keyword, "FROM"), new(TokenType.Identifier, "t"),
            new(TokenType.Keyword, "WHERE"), new(TokenType.Identifier, "a"), new(TokenType.Keyword, "BETWEEN"),
            new(TokenType.Parameter, "lo"), new(TokenType.Identifier, "AND"), new(TokenType.Parameter, "p"),
            new(TokenType.End, string.Empty)
        };

        var m = SqlParser.Parse(tokens, "q");

        Assert.Equal(string.Empty, Param(m, "p").BoundColumnName);
    }

    [Fact]
    public void LiteralBindings_CoverComparisonsAndInLists()
    {
        var m = ParseSql("SELECT a FROM t WHERE b = 5 AND c = -7 AND d IN (1, -3, 'x')");

        Assert.Equal(
            new[] { "Number|5|b", "Number|-7|c", "Number|1|d", "Number|-3|d", "String|x|d" },
            m.Literals.Select(l => $"{l.Kind}|{l.Value}|{l.BoundColumnName}"));
    }

    [Fact]
    public void SecondStatement_IsRecordedOnce()
    {
        var m = ParseSql("SELECT 1; SELECT 2");

        Assert.Equal(new[] { "MULTI_STATEMENT" }, m.UnsupportedConstructs);
    }

    [Fact]
    public void CteBodyBinding_IsCarriedToTheOuterParameter()
    {
        var p = Param(ParseSql("WITH d AS (DELETE FROM t WHERE id = @id RETURNING id) SELECT id FROM d"), "id");

        Assert.Equal("id", p.BoundColumnName);
        Assert.Equal("=", p.ComparisonOp);
    }

    [Fact]
    public void FinalStatementBinding_IsCarriedToTheOuterParameter()
    {
        var p = Param(ParseSql("WITH x AS (SELECT 1 AS n) DELETE FROM t WHERE id = @id"), "id");

        Assert.Equal("id", p.BoundColumnName);
        Assert.Equal("=", p.ComparisonOp);
    }

    [Fact]
    public void CteVirtualColumns_ComeFromSelectOrReturning()
    {
        var select = ParseSql("WITH x AS (SELECT id FROM t) SELECT id FROM x");
        var returning = ParseSql("WITH d AS (DELETE FROM t RETURNING id) SELECT id FROM d");

        Assert.Equal(new[] { "id" }, Assert.Single(select.Ctes).VirtualColumns);
        Assert.Equal(new[] { "id" }, Assert.Single(returning.Ctes).VirtualColumns);
    }

    [Fact]
    public void FinalStatementUnion_IsCarriedToTheOuterModel()
    {
        var m = ParseSql("WITH x AS (SELECT 1 AS n) SELECT n FROM x UNION SELECT n FROM x");

        Assert.Contains("UNION", m.UnsupportedConstructs);
    }

    [Theory]
    [InlineData("SELECT (a = 1) AS f FROM t", "boolean")]
    [InlineData("SELECT (EXISTS (SELECT 1 FROM u)) AS f FROM t", "boolean")]
    [InlineData("SELECT (SELECT b FROM u WHERE b = 2) AS f FROM t", "")]
    [InlineData("SELECT CASE WHEN a = 1 THEN 1 ELSE 0 END > 0 AS f FROM t", "boolean")]
    public void ExpressionType_IsInferredFromShape(string sql, string expected)
    {
        var col = Assert.Single(ParseSql(sql).Columns);

        Assert.Equal(expected, col.InferredDbType ?? string.Empty);
    }
}
