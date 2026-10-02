using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

public class OnAtomTests
{
    private static QueryModel ParseSql(string sql)
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), "TestQuery");

    private static List<string> Rendered(TableRef table)
    {
        var result = new List<string>();
        foreach (var atom in table.OnAtoms)
        {
            var parts = new List<string>();
            foreach (var term in atom.Terms)
            {
                parts.Add(term.Kind == AtomTermKind.Column && !string.IsNullOrEmpty(term.TableAlias)
                    ? term.TableAlias + "." + term.Text
                    : term.Text);
            }
            result.Add(string.Join(" ", parts));
        }
        return result;
    }

    [Fact]
    public void TheFromTable_HasNoOnAtoms()
    {
        var model = ParseSql("select o.id from orders o join lines l on l.order_id = o.id");

        Assert.Empty(model.Tables[0].OnAtoms);
    }

    [Fact]
    public void EachConjunct_IsAnAtom_AndWhereIsNotPartOfIt()
    {
        var model = ParseSql(
            "select o.id from orders o left join lines l on l.order_id = o.id and l.tenant_id = @tenantId where o.id = @id");

        Assert.Equal(new[] { "l.order_id = o.id", "l.tenant_id = @tenantId" }, Rendered(model.Tables[1]));
        Assert.Equal(JoinKind.Left, model.Tables[1].Join);
    }

    [Fact]
    public void ATopLevelOr_KeepsTheClauseWhole()
    {
        var model = ParseSql(
            "select o.id from orders o join lines l on l.order_id = o.id or l.tenant_id = @tenantId");

        Assert.Equal(new[] { "l.order_id = o.id OR l.tenant_id = @tenantId" }, Rendered(model.Tables[1]));
    }

    [Fact]
    public void EachJoin_KeepsItsOwnClause()
    {
        var model = ParseSql(
            "select o.id from orders o inner join lines l on l.order_id = o.id and l.tenant_id = @t " +
            "right join items i on i.id = l.item_id order by o.id");

        Assert.Equal(new[] { "l.order_id = o.id", "l.tenant_id = @t" }, Rendered(model.Tables[1]));
        Assert.Equal(new[] { "i.id = l.item_id" }, Rendered(model.Tables[2]));
    }

    [Fact]
    public void ParenthesesInsideTheClause_DoNotEndIt()
    {
        var model = ParseSql(
            "select o.id from orders o join lines l on (l.order_id = o.id) and l.qty in (1, 2) group by o.id");

        Assert.Equal(new[] { "( l.order_id = o.id )", "l.qty IN ( 1 , 2 )" }, Rendered(model.Tables[1]));
    }

    [Theory]
    [InlineData("select o.id from orders o join lines l on l.order_id = o.id, items i")]
    [InlineData("select o.id from orders o join lines l on l.order_id = o.id natural join items")]
    [InlineData("select o.id from orders o join lines l on l.order_id = o.id cross join items i")]
    [InlineData("select o.id from orders o join lines l on l.order_id = o.id having count(*) > 1")]
    [InlineData("select o.id from orders o join lines l on l.order_id = o.id limit 5")]
    [InlineData("select o.id from orders o join lines l on l.order_id = o.id;")]
    public void TheClauseEnds_AtTheNextClauseOrJoin(string sql)
    {
        var model = ParseSql(sql);

        Assert.Equal(new[] { "l.order_id = o.id" }, Rendered(model.Tables[1]));
    }

    [Fact]
    public void AJoinInsideASubquery_EndsAtTheSubqueryBody()
    {
        var model = ParseSql(
            "select o.id from orders o where exists (select 1 from lines l join items i on i.id = l.item_id and i.tenant_id = @t)");

        Assert.Equal(new[] { "i.id = l.item_id", "i.tenant_id = @t" }, Rendered(model.Subqueries[0].Body.Tables[1]));
    }

    [Fact]
    public void AClauseKeywordInsideParentheses_DoesNotEndIt()
    {
        var model = ParseSql(
            "select o.id from orders o join lines l on l.order_id = o.id and o.codes = string_agg(l.code, ',' order by l.code) and l.tenant_id = @t");

        Assert.Equal(3, model.Tables[1].OnAtoms.Count);
        Assert.Equal("l.tenant_id = @t", Rendered(model.Tables[1])[2]);
    }

    [Fact]
    public void LeftAndRightFunctionCalls_DoNotEndIt()
    {
        var model = ParseSql(
            "select o.id from orders o join lines l on l.order_id = o.id and left(l.code, 2) = right(o.code, 2) and l.tenant_id = @t");

        Assert.Equal(3, model.Tables[1].OnAtoms.Count);
        Assert.Equal("l.tenant_id = @t", Rendered(model.Tables[1])[2]);
    }

    [Fact]
    public void AUsingJoin_HasNoOnAtoms()
    {
        var model = ParseSql("select o.id from orders o join lines l using (order_id)");

        Assert.Empty(model.Tables[1].OnAtoms);
    }
}
