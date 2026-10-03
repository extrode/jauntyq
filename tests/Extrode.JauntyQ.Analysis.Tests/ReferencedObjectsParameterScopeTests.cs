using System.Linq;
using Extrode.JauntyQ.Analysis.Impact;
using Extrode.JauntyQ.SqlParser;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class ReferencedObjectsParameterScopeTests
{
    [Fact]
    public void AnUnqualifiedParameterInsideASubquery_IsAttributedToTheSubquerysAndTheOuterTables()
    {
        var model = SqlParser.SqlParser.Parse(
            SqlTokenizer.Tokenize("delete from orders where exists (select 1 from customers where email = @Email)"), "q");

        var refs = ReferencedObjects.Resolve(model);

        Assert.Contains(refs.Columns, c => c.Table == "customers" && c.Column == "email");
        Assert.Contains(refs.Columns, c => c.Table == "orders" && c.Column == "email");
    }

    [Fact]
    public void AnUnqualifiedColumnInANestedSubquery_IsAttributedToEveryEnclosingTable()
    {
        var model = SqlParser.SqlParser.Parse(
            SqlTokenizer.Tokenize("select id from orders where exists (select 1 from items i where i.qty > 0 and exists (select 1 from notes where status = @S))"), "q");

        var refs = ReferencedObjects.Resolve(model);

        Assert.Contains(refs.Columns, c => c.Table == "notes" && c.Column == "status");
        Assert.Contains(refs.Columns, c => c.Table == "items" && c.Column == "status");
        Assert.Contains(refs.Columns, c => c.Table == "orders" && c.Column == "status");
    }

    [Fact]
    public void AQualifiedParameterInsideASubquery_IsAttributedOnlyToItsTable()
    {
        var model = SqlParser.SqlParser.Parse(
            SqlTokenizer.Tokenize("delete from orders where exists (select 1 from customers c where c.email = @Email)"), "q");

        var refs = ReferencedObjects.Resolve(model);

        Assert.Contains(refs.Columns, c => c.Table == "customers" && c.Column == "email");
        Assert.DoesNotContain(refs.Columns, c => c.Table == "orders" && c.Column == "email");
    }

    [Fact]
    public void ACorrelatedParameterOnAnOuterAlias_IsAttributedToTheOuterTable()
    {
        var model = SqlParser.SqlParser.Parse(
            SqlTokenizer.Tokenize("select o.id from orders o where exists (select 1 from items i where i.order_id = o.id and o.total > @Min)"), "q");

        var refs = ReferencedObjects.Resolve(model);

        Assert.Contains(refs.Columns, c => c.Table == "orders" && c.Column == "total");
        Assert.DoesNotContain(refs.Columns, c => c.Table == "o");
    }

    [Fact]
    public void ASubqueryAliasShadowingAnOuterAlias_ResolvesToTheSubquerysTable()
    {
        var model = SqlParser.SqlParser.Parse(
            SqlTokenizer.Tokenize("select o.id from orders o where exists (select 1 from items o where o.qty > @Min)"), "q");

        var refs = ReferencedObjects.Resolve(model);

        Assert.Contains(refs.Columns, c => c.Table == "items" && c.Column == "qty");
        Assert.DoesNotContain(refs.Columns, c => c.Table == "orders" && c.Column == "qty");
    }
}
