using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>
/// Parser-model coverage for WHERE-clause predicate subqueries (lifted out of
/// the enclosing statement) and the RETURNING statement-terminator handling.
/// </summary>
public class SubqueryParserTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        return SqlParser.Parse(tokens, name);
    }

    [Fact]
    public void InSubquery_IsLifted_NotSubqueryConstruct()
    {
        var model = ParseSql(
            "select p.product_id from products p " +
            "where p.category_id in (select c.category_id from categories c)");

        // Outer statement keeps only its own table/column; the inner SELECT's
        // table/column are attached to the subquery, not the outer model.
        Assert.Single(model.Tables);
        Assert.Equal("products", model.Tables[0].TableName);
        Assert.DoesNotContain("SUBQUERY", model.UnsupportedConstructs);

        var sub = Assert.Single(model.Subqueries);
        Assert.Equal(SubqueryKind.In, sub.Kind);
        Assert.Equal("categories", sub.Body.Tables[0].TableName);
        Assert.Single(sub.Body.Columns);
    }

    [Fact]
    public void NotInSubquery_IsLifted()
    {
        var model = ParseSql(
            "select p.product_id from products p " +
            "where p.category_id not in (select c.category_id from categories c)");

        Assert.DoesNotContain("SUBQUERY", model.UnsupportedConstructs);
        var sub = Assert.Single(model.Subqueries);
        Assert.Equal(SubqueryKind.In, sub.Kind);
    }

    [Fact]
    public void ExistsSubquery_IsLifted()
    {
        var model = ParseSql(
            "select p.product_id from products p where exists (select 1 from categories c)");

        Assert.Single(model.Tables);
        Assert.DoesNotContain("SUBQUERY", model.UnsupportedConstructs);
        var sub = Assert.Single(model.Subqueries);
        Assert.Equal(SubqueryKind.Exists, sub.Kind);
        Assert.Equal("categories", sub.Body.Tables[0].TableName);
    }

    [Fact]
    public void NotExistsSubquery_IsLifted()
    {
        var model = ParseSql(
            "select p.product_id from products p where not exists (select 1 from categories c)");

        var sub = Assert.Single(model.Subqueries);
        Assert.Equal(SubqueryKind.Exists, sub.Kind);
    }

    [Fact]
    public void Subquery_ParamBindsToInnerColumn()
    {
        var model = ParseSql(
            "select p.product_id from products p " +
            "where exists (select 1 from categories c where c.category_id = @cat)");

        var cat = model.Parameters.Single(x => x.Name == "cat");
        Assert.Equal("category_id", cat.BoundColumnName);
    }

    [Fact]
    public void ScalarSubqueryInProjection_StillUnsupported()
    {
        // A nested SELECT that is NOT an IN/EXISTS predicate stays rejected.
        var model = ParseSql(
            "select p.product_id, (select c.category_id from categories c) as cat from products p");

        Assert.Contains("SUBQUERY", model.UnsupportedConstructs);
    }

    [Fact]
    public void InValueList_NotTreatedAsSubquery()
    {
        // A plain IN (literal, ...) value list is not a subquery.
        var model = ParseSql("select p.product_id from products p where p.category_id in (1, 2, 3)");

        Assert.Empty(model.Subqueries);
        Assert.DoesNotContain("SUBQUERY", model.UnsupportedConstructs);
    }

    [Fact]
    public void Returning_PlainColumn_TrailingSemicolon()
    {
        var model = ParseSql("delete from products where product_id = @id returning product_id;");

        Assert.True(model.HasReturning);
        var col = Assert.Single(model.Returning);
        Assert.False(col.IsExpression);
        Assert.Equal("product_id", col.ColumnName);
        Assert.Empty(model.ExpressionsMissingAlias);
    }

    [Fact]
    public void Returning_Expression_TrailingSemicolon()
    {
        var model = ParseSql(
            "delete from products where product_id = @id returning (product_id is not null) as present;");

        var col = Assert.Single(model.Returning);
        Assert.True(col.IsExpression);
        Assert.Equal("present", col.OutputAlias);
        Assert.Empty(model.ExpressionsMissingAlias);
    }

    [Fact]
    public void CteChain_TrailingSemicolon_FinalStatementIntact()
    {
        var model = ParseSql(
            "with cte as (select product_id from products) select product_id from cte;");

        Assert.Equal(StatementType.Select, model.StatementType);
        Assert.Single(model.Ctes);
        Assert.Contains(model.Tables, t => t.TableName == "cte");
    }
}
