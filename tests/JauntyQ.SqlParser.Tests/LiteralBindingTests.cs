using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

public class LiteralBindingTests
{
    private static QueryModel Parse(string sql) =>
        SqlParser.Parse(SqlTokenizer.Tokenize(sql), "Test");

    [Fact]
    public void WhereComparison_StringLiteral_BindsToColumn()
    {
        var model = Parse("select name from t where t.name = 'abc'");

        var lit = Assert.Single(model.Literals);
        Assert.Equal(LiteralKind.String, lit.Kind);
        Assert.Equal("abc", lit.Value);
        Assert.Equal("t", lit.BoundTableAlias);
        Assert.Equal("name", lit.BoundColumnName);
    }

    [Fact]
    public void WhereComparison_NumericLiteral_BindsToColumn()
    {
        var model = Parse("select id from t where t.price >= 19.99");

        var lit = Assert.Single(model.Literals);
        Assert.Equal(LiteralKind.Number, lit.Kind);
        Assert.Equal("19.99", lit.Value);
        Assert.Equal("price", lit.BoundColumnName);
    }

    [Fact]
    public void InsertValues_MixedSlots_BindPositionally()
    {
        // Literals must advance the column index alongside parameters:
        // @a -> a, 'x' -> b, @b -> c.
        var model = Parse("insert into t (a, b, c) values (@a, 'x', @b)");

        var pa = model.Parameters.Single(p => p.Name == "a");
        var pb = model.Parameters.Single(p => p.Name == "b");
        Assert.Equal("a", pa.BoundColumnName);
        Assert.Equal("c", pb.BoundColumnName);
        Assert.True(pa.IsWriteTarget);
        Assert.True(pb.IsWriteTarget);

        var lit = Assert.Single(model.Literals);
        Assert.Equal(LiteralKind.String, lit.Kind);
        Assert.Equal("x", lit.Value);
        Assert.Equal("b", lit.BoundColumnName);
    }

    [Fact]
    public void InsertValues_NegativeNumber_Bound()
    {
        var model = Parse("insert into t (a) values (-5)");

        var lit = Assert.Single(model.Literals);
        Assert.Equal(LiteralKind.Number, lit.Kind);
        Assert.Equal("-5", lit.Value);
        Assert.Equal("a", lit.BoundColumnName);
    }

    [Fact]
    public void InList_EveryLiteralBound()
    {
        var model = Parse("select id from t where t.code in (1, 2, 3)");

        Assert.Equal(3, model.Literals.Count);
        Assert.All(model.Literals, l =>
        {
            Assert.Equal(LiteralKind.Number, l.Kind);
            Assert.Equal("code", l.BoundColumnName);
        });
    }

    [Fact]
    public void LikePattern_NotCaptured()
    {
        // Wildcards make length reasoning unsound; LIKE literals are skipped.
        var model = Parse("select id from t where t.name like 'abc%'");

        Assert.Empty(model.Literals);
    }

    [Fact]
    public void UpdateSet_ParamsAreWriteTargets_WhereParamsAreNot()
    {
        var model = Parse("update t set name = @name, price = @price where id = @id");

        Assert.True(model.Parameters.Single(p => p.Name == "name").IsWriteTarget);
        Assert.True(model.Parameters.Single(p => p.Name == "price").IsWriteTarget);
        Assert.False(model.Parameters.Single(p => p.Name == "id").IsWriteTarget);
    }

    [Fact]
    public void UpdateSet_Literal_Bound()
    {
        var model = Parse("update t set status = 'archived' where id = @id");

        var lit = Assert.Single(model.Literals);
        Assert.Equal(LiteralKind.String, lit.Kind);
        Assert.Equal("archived", lit.Value);
        Assert.Equal("status", lit.BoundColumnName);
    }

    [Fact]
    public void EscapedQuotes_KeptRawInValue()
    {
        var model = Parse("select id from t where t.name = 'it''s'");

        var lit = Assert.Single(model.Literals);
        Assert.Equal("it''s", lit.Value); // decoded to one char by the validator
    }
}
