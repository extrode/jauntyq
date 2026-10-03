using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class ParameterBoundScopeTests
{
    private static QueryModel Parse(string sql) =>
        SqlParser.Parse(SqlTokenizer.Tokenize(sql), "Probe");

    private static ParameterRef Param(QueryModel model, string name) =>
        model.Parameters.Find(p => p.Name == name)!;

    [Theory]
    [InlineData("delete from orders where customer_id in (select id from customers where id = @Id)")]
    [InlineData("delete from orders where exists (select 1 from customers where id = @Id)")]
    [InlineData("select id from orders where customer_id not in (select id from customers where id = @Id)")]
    public void ABindingCarriedOutOfAPredicateSubquery_PointsAtThatSubquery(string sql)
    {
        var model = Parse(sql);

        var id = Param(model, "Id");
        Assert.Equal("id", id.BoundColumnName);
        Assert.Same(Assert.Single(model.Subqueries).Body, id.BoundScope);
    }

    [Fact]
    public void ANestedSubqueryBinding_PointsAtTheInnermostScope()
    {
        var model = Parse(
            "select id from orders where customer_id in " +
            "(select id from customers where region_id in (select id from regions where id = @Id))");

        var inner = Assert.Single(Assert.Single(model.Subqueries).Body.Subqueries).Body;
        Assert.Same(inner, Param(model, "Id").BoundScope);
    }

    [Theory]
    [InlineData("select o.id from orders o where exists (select 1 from items i where i.order_id = o.id and o.total > @Min)")]
    [InlineData("select id from orders where exists (select 1 from items i where i.order_id = orders.id and orders.total > @Min)")]
    public void ACorrelatedBindingToAnOuterAlias_HasNoScope(string sql)
    {
        var model = Parse(sql);

        var min = Param(model, "Min");
        Assert.Equal("total", min.BoundColumnName);
        Assert.Null(min.BoundScope);
    }

    [Theory]
    [InlineData("select id from orders where exists (select 1 from items i where i.qty > @Min)")]
    [InlineData("select id from orders where exists (select 1 from items where items.qty > @Min)")]
    [InlineData("select id from orders where exists (select 1 from items I where i.qty > @Min)")]
    public void ABindingQualifiedByTheSubquerysOwnTable_PointsAtThatSubquery(string sql)
    {
        var model = Parse(sql);

        Assert.Same(Assert.Single(model.Subqueries).Body, Param(model, "Min").BoundScope);
    }

    [Fact]
    public void ACorrelatedBindingToAMiddleAlias_PointsAtTheMiddleScope()
    {
        var model = Parse(
            "select id from orders where customer_id in " +
            "(select id from customers c where exists (select 1 from regions r where r.id = c.region_id and c.note = @Note))");

        Assert.Same(Assert.Single(model.Subqueries).Body, Param(model, "Note").BoundScope);
    }

    [Fact]
    public void ABindingInTheStatementItself_HasNoScope()
    {
        var model = Parse(
            "select id from orders where id = @Id and customer_id in (select id from customers where id = @Other)");

        Assert.Null(Param(model, "Id").BoundScope);
        Assert.Same(Assert.Single(model.Subqueries).Body, Param(model, "Other").BoundScope);
    }

    [Fact]
    public void ABindingCarriedOutOfACteBody_PointsAtThatBody()
    {
        var model = Parse("with x as (select id from customers where id = @Id) select id from x");

        Assert.Same(Assert.Single(model.Ctes).Body, Param(model, "Id").BoundScope);
    }

    [Fact]
    public void ASubqueryInsideACteBody_IsTheScope()
    {
        var model = Parse(
            "with x as (select id from customers where region_id in (select id from regions where id = @Id)) select id from x");

        var inner = Assert.Single(Assert.Single(model.Ctes).Body.Subqueries).Body;
        Assert.Same(inner, Param(model, "Id").BoundScope);
    }

    [Fact]
    public void ASubqueryInTheFinalStatementAfterACte_IsTheScope()
    {
        var model = Parse(
            "with x as (select 1 as one) delete from orders where customer_id in (select id from customers where id = @Id)");

        Assert.Same(Assert.Single(model.Subqueries).Body, Param(model, "Id").BoundScope);
    }

    [Fact]
    public void TheFinalStatementsOwnBindingAfterACte_HasNoScope()
    {
        var model = Parse("with x as (select 1 as one) delete from orders where id = @Id");

        var id = Param(model, "Id");
        Assert.Equal("id", id.BoundColumnName);
        Assert.Null(id.BoundScope);
    }
}
