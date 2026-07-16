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

    /// <summary>
    /// AUD-R11 (§2.1-r18 sibling-sweep): every prior case here has at least
    /// one dot -- either a two-part "u"."col" alias-qualified reference or a
    /// three/four-part catalog-qualified one. A bare, wholly-unqualified
    /// quoted/bracketed identifier with NO dot at all (e.g. a single-table
    /// query with no alias, or a WHERE-clause predicate column) was never
    /// independently probed. The tokenizer emits the same TokenType.Identifier
    /// regardless of whether the source used no quoting, "double quotes",
    /// `backticks`, or [brackets] -- so the select-list/WHERE-clause parsers,
    /// which only branch on TokenType and never re-inspect the source
    /// spelling, should treat all four forms identically. Confirmed correct:
    /// no fix needed, this proves the design's "quoting is a tokenizer-only
    /// concern" invariant actually holds for the trivial unqualified case.
    /// </summary>
    [Theory]
    [InlineData("select \"first_name\" from users")]
    [InlineData("select [first_name] from users")]
    [InlineData("select `first_name` from users")]
    [InlineData("select first_name from users")]
    public void UnqualifiedQuotedColumn_IsPlainColumn_NotExpression(string sql)
    {
        var model = ParseSql(sql);

        Assert.Empty(model.ExpressionsMissingAlias);
        var col = Assert.Single(model.Columns);
        Assert.False(col.IsExpression);
        Assert.Equal(string.Empty, col.TableAlias);
        Assert.Equal("first_name", col.ColumnName);
    }

    /// <summary>
    /// Same as above but in a WHERE-clause predicate position rather than
    /// the SELECT list, binding a parameter to an unqualified quoted/bracketed
    /// column -- WHERE-clause binding (ParameterRef.BoundTableAlias/
    /// BoundColumnName) is a completely separate code path from the
    /// projection-list parsing above, so this needed its own probe rather
    /// than assuming the SELECT-list result generalizes.
    /// </summary>
    [Theory]
    [InlineData("select id from users where \"first_name\" = @name")]
    [InlineData("select id from users where [first_name] = @name")]
    public void UnqualifiedQuotedWherePredicate_BindsParameterToPlainColumn(string sql)
    {
        var model = ParseSql(sql);

        var param = Assert.Single(model.Parameters);
        Assert.Equal(string.Empty, param.BoundTableAlias);
        Assert.Equal("first_name", param.BoundColumnName);
        Assert.Equal("=", param.ComparisonOp);
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
