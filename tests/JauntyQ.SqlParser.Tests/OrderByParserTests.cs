using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

/// <summary>
/// ORDER BY capture into <see cref="QueryModel.OrderBy"/>: plain columns feed
/// JNT8007, while ordinals, expressions, and projected-alias references are
/// recorded with a kind that makes the analyzer skip them.
/// </summary>
public class OrderByParserTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    [Fact]
    public void SinglePlainColumn_Captured()
    {
        var model = ParseSql("select product_id from products order by launched_at");

        var item = Assert.Single(model.OrderBy);
        Assert.Equal(OrderByItemKind.PlainColumn, item.Kind);
        Assert.Equal("launched_at", item.BoundColumnName);
        Assert.Empty(item.BoundTableAlias);
    }

    [Fact]
    public void QualifiedColumn_CapturesAliasAndColumn()
    {
        var model = ParseSql("select p.product_id from products p order by p.launched_at");

        var item = Assert.Single(model.OrderBy);
        Assert.Equal(OrderByItemKind.PlainColumn, item.Kind);
        Assert.Equal("p", item.BoundTableAlias);
        Assert.Equal("launched_at", item.BoundColumnName);
    }

    [Fact]
    public void MultipleColumnsWithDirection_OrderPreserved_DirectionIgnored()
    {
        var model = ParseSql("select product_id from products order by category_id asc, launched_at desc");

        Assert.Equal(2, model.OrderBy.Count);
        Assert.Equal("category_id", model.OrderBy[0].BoundColumnName);
        Assert.Equal(OrderByItemKind.PlainColumn, model.OrderBy[0].Kind);
        Assert.Equal("launched_at", model.OrderBy[1].BoundColumnName);
        Assert.Equal(OrderByItemKind.PlainColumn, model.OrderBy[1].Kind);
    }

    [Fact]
    public void Ordinal_CapturedAsOrdinal()
    {
        var model = ParseSql("select product_id, product_name from products order by 2");

        var item = Assert.Single(model.OrderBy);
        Assert.Equal(OrderByItemKind.Ordinal, item.Kind);
        Assert.Empty(item.BoundColumnName);
    }

    [Fact]
    public void Expression_CapturedAsExpression()
    {
        var model = ParseSql("select product_id from products order by lower(product_name)");

        var item = Assert.Single(model.OrderBy);
        Assert.Equal(OrderByItemKind.Expression, item.Kind);
    }

    [Fact]
    public void ProjectedAlias_CapturedAsProjectedAlias()
    {
        var model = ParseSql("select count(*) as cnt from products order by cnt");

        var item = Assert.Single(model.OrderBy);
        Assert.Equal(OrderByItemKind.ProjectedAlias, item.Kind);
        Assert.Equal("cnt", item.BoundColumnName);
    }

    [Fact]
    public void OrderByListEndsAtLimit()
    {
        var model = ParseSql("select product_id from products order by launched_at limit 10");

        var item = Assert.Single(model.OrderBy);
        Assert.Equal(OrderByItemKind.PlainColumn, item.Kind);
        Assert.Equal("launched_at", item.BoundColumnName);
    }

    [Fact]
    public void NoOrderBy_EmptyList()
    {
        var model = ParseSql("select product_id from products where product_id = @id");

        Assert.Empty(model.OrderBy);
    }
}
