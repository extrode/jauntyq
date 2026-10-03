using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

public class TargetAliasTests
{
    [Theory]
    [InlineData("update orders o set note = @n where o.id = @id", "o")]
    [InlineData("update orders as o set note = @n", "o")]
    [InlineData("update orders set note = @n where id = @id", "")]
    [InlineData("delete from orders o where o.id = @id", "o")]
    [InlineData("delete from orders as o where o.id = @id", "o")]
    [InlineData("delete orders where id = @id", "")]
    [InlineData("delete from orders", "")]
    public void TheTargetAlias_IsRecordedOffTheTableRef(string sql, string alias)
    {
        var model = SqlParser.Parse(SqlTokenizer.Tokenize(sql), "Q");

        Assert.Equal(alias, model.TargetAlias);
        Assert.Equal(string.Empty, model.Tables[0].Alias);
    }

    [Fact]
    public void AFinalDeleteAfterACte_KeepsItsAlias()
    {
        var model = SqlParser.Parse(SqlTokenizer.Tokenize(
            "with gone as (select id from orders where id = @id) delete from orders o where o.id in (select id from gone)"), "Q");

        Assert.Equal("o", model.TargetAlias);
    }

    [Fact]
    public void ASelect_HasNoTargetAlias()
    {
        Assert.Equal(string.Empty, SqlParser.Parse(SqlTokenizer.Tokenize("select o.id from orders o"), "Q").TargetAlias);
    }

    [Fact]
    public void AnAsWithNothingAfterIt_GivesNoAlias()
    {
        Assert.Equal(string.Empty, SqlParser.Parse(SqlTokenizer.Tokenize("delete from orders as"), "Q").TargetAlias);
    }
}
