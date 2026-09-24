using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class SqlParserCoreSliceAMutationCoverageTests
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
        $"type={m.StatementType} limit={m.HasRowLimit} group={m.HasGroupBy}",
        "cols=" + string.Join(";", m.Columns.Select(c => $"{c.TableAlias}|{c.ColumnName}|{c.OutputAlias}|{c.IsExpression}|{c.ExpressionSql}")),
        "order=" + string.Join(";", m.OrderBy.Select(o => $"{o.Kind}|{o.BoundTableAlias}|{o.BoundColumnName}|{string.Join(",", o.ReferencedColumns)}")),
        "tables=" + string.Join(";", m.Tables.Select(t => $"{t.TableName}|{t.Alias}")),
        "unsupported=" + string.Join(";", m.UnsupportedConstructs),
        "missingAlias=" + string.Join(";", m.ExpressionsMissingAlias));

    [Theory]
    [InlineData("SELECT a FROM t")]
    [InlineData("SELECT a FROM t ORDER")]
    [InlineData("SELECT a FROM t ORDER BY a")]
    [InlineData("SELECT a FROM t ORDER BY a + b")]
    [InlineData("SELECT")]
    [InlineData("SELECT DISTINCT")]
    [InlineData("SELECT DISTINCT ON")]
    [InlineData("SELECT DISTINCT ON (a")]
    [InlineData("SELECT ALL")]
    [InlineData("SELECT TOP")]
    [InlineData("SELECT TOP (")]
    [InlineData("SELECT TOP (5")]
    [InlineData("SELECT TOP 5")]
    [InlineData("SELECT TOP 5 WITH")]
    [InlineData("SELECT TOP 5 PERCENT")]
    [InlineData("SELECT u.")]
    [InlineData("SELECT a")]
    [InlineData("SELECT a AS")]
    public void TokenListWithoutTrailingEnd_ParsesExactlyAsTheEndTerminatedList(string sql)
    {
        Assert.Equal(Shape(ParseSql(sql)), Shape(ParseWithoutEnd(sql)));
    }

    [Fact]
    public void EmptyTokenList_ParsesToAnEmptySelect()
    {
        var model = SqlParser.Parse(new List<Token>(), "q");

        Assert.Equal(StatementType.Select, model.StatementType);
        Assert.Empty(model.Columns);
        Assert.Empty(model.Ctes);
    }

    [Fact]
    public void SelectForUpdate_StaysASelectOfItsOwnColumns()
    {
        var model = ParseSql("SELECT id FROM t FOR UPDATE");

        Assert.Equal(StatementType.Select, model.StatementType);
        Assert.Equal("id", Assert.Single(model.Columns).ColumnName);
        Assert.Null(model.TargetTable);
    }

    [Fact]
    public void OrderByLeadingMinus_IsOneExpressionItem()
    {
        var item = Assert.Single(ParseSql("SELECT a FROM t ORDER BY -a").OrderBy);

        Assert.Equal(OrderByItemKind.Expression, item.Kind);
    }

    [Fact]
    public void OrderByNull_IsOneExpressionItem()
    {
        var item = Assert.Single(ParseSql("SELECT a FROM t ORDER BY NULL").OrderBy);

        Assert.Equal(OrderByItemKind.Expression, item.Kind);
    }

    [Fact]
    public void OrderByBracketQuotedBy_IsNotTakenForTheByKeyword()
    {
        var item = Assert.Single(ParseSql("SELECT a FROM t ORDER [BY] x").OrderBy);

        Assert.Equal(OrderByItemKind.Expression, item.Kind);
    }

    [Fact]
    public void OrderByQualifiedColumnSharingAnAliasName_StaysAPlainColumn()
    {
        var item = Assert.Single(ParseSql("SELECT a AS x FROM t ORDER BY t.x").OrderBy);

        Assert.Equal(OrderByItemKind.PlainColumn, item.Kind);
        Assert.Equal("t", item.BoundTableAlias);
        Assert.Equal("x", item.BoundColumnName);
    }

    [Fact]
    public void OrderByColumnFollowedByIsNull_IsOneExpressionItem()
    {
        var item = Assert.Single(ParseSql("SELECT a FROM t ORDER BY a IS NULL").OrderBy);

        Assert.Equal(OrderByItemKind.Expression, item.Kind);
    }

    [Fact]
    public void OrderByExpressionBeforeLimit_StopsAtLimitSoTheRowLimitIsSeen()
    {
        var model = ParseSql("SELECT a FROM t ORDER BY a + b LIMIT 5");

        Assert.True(model.HasRowLimit);
        var item = Assert.Single(model.OrderBy);
        Assert.Equal(OrderByItemKind.Expression, item.Kind);
        Assert.Equal(new[] { ("", "a"), ("", "b") }, item.ReferencedColumns);
    }

    [Fact]
    public void OrderByColumnNotMatchingAnyAlias_StaysAPlainColumnWhenOtherColumnsAreAliased()
    {
        var item = Assert.Single(ParseSql("SELECT a AS x FROM t ORDER BY b").OrderBy);

        Assert.Equal(OrderByItemKind.PlainColumn, item.Kind);
    }

    [Fact]
    public void DistinctOnInBothUnionBranches_IsRecordedOnce()
    {
        var model = ParseSql("SELECT DISTINCT ON (a) a FROM t UNION SELECT DISTINCT ON (b) b FROM u");

        Assert.Single(model.UnsupportedConstructs, c => c == "DISTINCT ON");
    }

    [Fact]
    public void SelectIntoInBothUnionBranches_IsRecordedOnce()
    {
        var model = ParseSql("SELECT a INTO x FROM t UNION SELECT b INTO y FROM u");

        Assert.Single(model.UnsupportedConstructs, c => c == "SELECT INTO");
    }

    [Theory]
    [InlineData("SELECT DISTINCT DISTINCT a FROM t")]
    [InlineData("SELECT ALL DISTINCT a FROM t")]
    [InlineData("SELECT TOP 5 DISTINCT a FROM t")]
    public void ModifierFollowedByAnotherModifier_BothAreSkipped(string sql)
    {
        var col = Assert.Single(ParseSql(sql).Columns);

        Assert.False(col.IsExpression);
        Assert.Equal("a", col.ColumnName);
    }

    [Fact]
    public void TopPercentAsTheLastProjectionToken_IsTheColumn()
    {
        var col = Assert.Single(ParseSql("SELECT TOP 10 percent").Columns);

        Assert.Equal("percent", col.ColumnName);
    }

    [Fact]
    public void DistinctOnFollowedByANonParenSymbol_ConsumesNothingAfterOn()
    {
        var model = ParseSql("SELECT DISTINCT ON * FROM t");

        Assert.Equal("*", Assert.Single(model.Columns).ColumnName);
        Assert.Equal("t", Assert.Single(model.Tables).TableName);
    }

    [Fact]
    public void DistinctOnListWithAComma_IsSkippedToItsClosingParen()
    {
        var col = Assert.Single(ParseSql("SELECT DISTINCT ON (a, b) c FROM t").Columns);

        Assert.Equal("c", col.ColumnName);
    }

    [Fact]
    public void TrailingCommaBeforeFrom_IsNotTakenAsAnAlias()
    {
        var col = Assert.Single(ParseSql("SELECT a, FROM t").Columns);

        Assert.Equal("a", col.ColumnName);
        Assert.Equal(string.Empty, col.OutputAlias);
    }
}
