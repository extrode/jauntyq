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

    /// <summary>
    /// A catalog/schema-qualified quoted reference (3+ dotted segments) used
    /// to stop merging after the first two: "dbo"."users"."first_name"
    /// tokenized as Identifier("dbo.users") '.' Identifier("first_name") and
    /// demoted to an expression demanding a spurious AS alias, instead of
    /// resolving to the same (TableAlias="users", ColumnName="first_name")
    /// that SplitQualifiedName already produces for the equivalent bare
    /// reference dbo.users.first_name.
    /// </summary>
    [Theory]
    [InlineData("select \"dbo\".\"users\".\"first_name\" from users \"u\"")]
    [InlineData("select [dbo].[users].[first_name] from users [u]")]
    [InlineData("select dbo.users.\"first_name\" from users u")]
    [InlineData("select \"server\".\"dbo\".\"users\".\"first_name\" from users \"u\"")]
    public void ThreeOrFourPartQuotedColumn_IsPlainColumn_NotExpression(string sql)
    {
        var model = ParseSql(sql);

        Assert.Empty(model.ExpressionsMissingAlias);
        var col = Assert.Single(model.Columns);
        Assert.False(col.IsExpression);
        Assert.Equal("users", col.TableAlias);
        Assert.Equal("first_name", col.ColumnName);
    }

    [Fact]
    public void QuotedNameContainingLiteralDot_FollowedByFurtherQualifier_IsLeftUnmerged()
    {
        // "weird.name"."col": the left operand is a single quoted token whose
        // dot is a LITERAL part of its own name, encountered fresh (not via
        // chain continuation from a prior merge). MergeQualifiedIdentifiers'
        // contains-a-dot guard on the left operand correctly blocks folding
        // the trailing ."col" onto it -- if it didn't, SplitQualifiedName
        // would silently (mis)treat the merged "weird.name.col" as
        // TableAlias="name", ColumnName="col", losing "weird" entirely. Left
        // unmerged, the select-list parser doesn't recognize the resulting
        // Identifier-Dot-Identifier shape as a plain qualified column and
        // demotes it to an expression requiring an AS alias -- a loud
        // compile-time signal instead of a silently wrong split.
        var model = ParseSql("select \"weird.name\".\"col\" from users \"u\"");

        var col = Assert.Single(model.Columns);
        Assert.True(col.IsExpression);
        Assert.NotEmpty(model.ExpressionsMissingAlias);
    }
}
