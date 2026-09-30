using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class SqlParserDisableAuditSqlParserATests
{
    private static QueryModel ParseSql(string sql)
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), "TestQuery");

    [Fact]
    public void PerfHints_ParenthesesBeforeWhere_StillScanWhereRegion()
    {
        var model = ParseSql("SELECT COUNT(*) FROM t WHERE upper(a) = @p");

        var hint = Assert.Single(model.PerfHints);
        Assert.Equal(PerfHintKind.FunctionOnColumn, hint.Kind);
        Assert.Equal("a", hint.BoundColumnName);
    }

    [Fact]
    public void PerfHints_JoinOnBeforeWhere_IsNotScanned()
    {
        var model = ParseSql("SELECT a FROM t JOIN u ON t.id = u.tid WHERE x = 1");

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void PerfHints_HavingAfterGroupBy_IsNotScanned()
    {
        var model = ParseSql("SELECT a FROM t WHERE a = 1 GROUP BY a HAVING upper(a) = 'x'");

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void PerfHints_NoWhere_IsEmpty()
    {
        var model = ParseSql("SELECT a FROM t");

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void PerfHints_UncomparedCall_ResumesScanAfterTheCall()
    {
        Assert.Empty(ParseSql("SELECT a FROM t WHERE upper(a) LIKE 'x' AND b = 1").PerfHints);
        Assert.Empty(ParseSql("SELECT a FROM t WHERE upper(a) IS NULL").PerfHints);
    }

    [Fact]
    public void PredicateAtoms_ParenthesesBeforeWhere_StillSplitConjuncts()
    {
        var model = ParseSql("SELECT COUNT(*) FROM t WHERE a = @a AND b = @b");

        Assert.Equal(2, model.PredicateAtoms.Count);
        Assert.Equal(new[] { "a", "=", "@a" }, model.PredicateAtoms[0].Terms.Select(t => t.Text));
        Assert.Equal(new[] { "b", "=", "@b" }, model.PredicateAtoms[1].Terms.Select(t => t.Text));
    }

    [Fact]
    public void PredicateAtoms_NoWhere_IsEmpty()
    {
        var model = ParseSql("SELECT a FROM t");

        Assert.Empty(model.PredicateAtoms);
    }

    [Fact]
    public void PredicateAtoms_OrderBy_EndsTheRegion()
    {
        var model = ParseSql("SELECT a FROM t WHERE a = @a ORDER BY a");

        var atom = Assert.Single(model.PredicateAtoms);
        Assert.Equal(new[] { "a", "=", "@a" }, atom.Terms.Select(t => t.Text));
    }

    [Fact]
    public void PredicateAtoms_ParameterTokenAlreadyCarryingAt_IsNotPrefixedTwice()
    {
        var tokens = new List<Token>
        {
            new(TokenType.Keyword, "SELECT"),
            new(TokenType.Identifier, "a"),
            new(TokenType.Keyword, "FROM"),
            new(TokenType.Identifier, "t"),
            new(TokenType.Keyword, "WHERE"),
            new(TokenType.Identifier, "a"),
            new(TokenType.Symbol, "="),
            new(TokenType.Parameter, "@a"),
            new(TokenType.End, string.Empty)
        };

        var atom = Assert.Single(SqlParser.Parse(tokens, "TestQuery").PredicateAtoms);

        Assert.Equal("@a", atom.Terms[2].Text);
    }

    [Fact]
    public void QualifiedNames_KeepOnlyTheInnermostQualifier()
    {
        var model = ParseSql("SELECT a FROM dbo.t WHERE dbo.t.c = @p AND d = @q");

        Assert.Equal("t", Assert.Single(model.Tables).TableName);
        Assert.Equal("c", model.PredicateAtoms[0].Terms[0].Text);
        Assert.Equal("t", model.PredicateAtoms[0].Terms[0].TableAlias);
        Assert.Equal("d", model.PredicateAtoms[1].Terms[0].Text);
        Assert.Equal(string.Empty, model.PredicateAtoms[1].Terms[0].TableAlias);
    }

    [Fact]
    public void Insert_QualifiedTarget_KeepsOnlyTheTableName()
    {
        var model = ParseSql("INSERT INTO dbo.t (a) VALUES (1)");

        Assert.Equal("t", model.TargetTable);
    }


    [Fact]
    public void Insert_ParenthesisedValue_DoesNotBind()
    {
        var model = ParseSql("INSERT INTO t (a, b) VALUES ((1), 2)");

        var literal = Assert.Single(model.Literals);
        Assert.Equal("2", literal.Value);
        Assert.Equal("b", literal.BoundColumnName);
    }
}