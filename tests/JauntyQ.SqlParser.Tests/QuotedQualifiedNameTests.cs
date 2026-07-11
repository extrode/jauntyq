using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

/// <summary>
/// Quoted qualified references ("u"."col", [u].[col], mixed) must parse as
/// plain columns exactly like bare p.product_id. They once tokenized as
/// identifier-dot-identifier and demoted to an expression demanding a
/// spurious AS alias.
/// </summary>
public class QuotedQualifiedNameTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    [Theory]
    [InlineData("select \"u\".\"first_name\" from users \"u\"")]
    [InlineData("select [u].[first_name] from users [u]")]
    [InlineData("select \"u\".first_name from users \"u\"")]
    [InlineData("select u.\"first_name\" from users u")]
    public void QuotedQualifiedColumn_IsPlainColumn_NotExpression(string sql)
    {
        var model = ParseSql(sql);

        Assert.Empty(model.ExpressionsMissingAlias);
        var col = Assert.Single(model.Columns);
        Assert.False(col.IsExpression);
        Assert.Equal("u", col.TableAlias);
        Assert.Equal("first_name", col.ColumnName);
    }
}
