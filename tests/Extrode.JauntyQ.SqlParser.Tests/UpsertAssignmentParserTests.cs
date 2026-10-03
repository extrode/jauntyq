using System.Linq;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class UpsertAssignmentParserTests
{
    private static ParameterRef Param(string sql, string name) =>
        SqlParser.Parse(SqlTokenizer.Tokenize(sql), "Test").Parameters.Single(p => p.Name == name);

    [Theory]
    [InlineData("insert into t (id) values (@id) on conflict (id) do update set status = @s", "", "status")]
    [InlineData("insert into t (id) values (@id) on conflict (id) do update set t.status = @s", "t", "status")]
    [InlineData("insert into t (id) values (@id) on conflict (id) do update set note = 'x', status = @s where t.id > 0", "", "status")]
    [InlineData("insert into t (id) values (@id) ON DUPLICATE KEY UPDATE status = @s", "", "status")]
    [InlineData("insert into t (id) select id from u on conflict (id) do update set status = @s", "", "status")]
    public void AnAssignmentInTheUpdateBranch_BindsAsAnUpsertWrite(string sql, string alias, string column)
    {
        var p = Param(sql, "s");

        Assert.Equal(alias, p.BoundTableAlias);
        Assert.Equal(column, p.BoundColumnName);
        Assert.True(p.IsWriteTarget);
        Assert.True(p.IsUpsertAssignment);
        Assert.Equal("=", p.ComparisonOp);
    }

    [Fact]
    public void AComparisonInTheUpsertWhere_BindsAsAComparison()
    {
        var p = Param("insert into t (id) values (@id) on conflict (id) do update set status = 'x' where t.owner = @s", "s");

        Assert.Equal("owner", p.BoundColumnName);
        Assert.False(p.IsWriteTarget);
        Assert.False(p.IsUpsertAssignment);
    }

    [Theory]
    [InlineData("insert into t (id) values (@id) on conflict (id) do update set status = (select s from u where k = @s)")]
    [InlineData("insert into t (id) values (@id) on conflict (id) do update set status = (case when k = @s then 'a' end)")]
    [InlineData("insert into t (id) values (@id) on conflict (id) do update set status = case when k = @s then 'a' end")]
    [InlineData("insert into t (id) values (@id) on duplicate key update status = if(k = @s, 'a', 'b')")]
    public void AComparisonInsideAnAssignedValue_IsNotAnUpsertWrite(string sql)
    {
        var p = Param(sql, "s");

        Assert.Equal("k", p.BoundColumnName);
        Assert.False(p.IsUpsertAssignment);
    }

    [Fact]
    public void AValuesSlotKeepsItsInsertBinding()
    {
        var p = Param("insert into t (id, status) values (@id, @s) on conflict (id) do update set note = @s", "s");

        Assert.Equal("status", p.BoundColumnName);
        Assert.True(p.IsWriteTarget);
        Assert.False(p.IsUpsertAssignment);
    }

    [Fact]
    public void AnUpdateStatementIsNotAnUpsert()
    {
        var p = Param("update t set status = @s where id = 1", "s");

        Assert.True(p.IsWriteTarget);
        Assert.False(p.IsUpsertAssignment);
    }

    [Fact]
    public void AnUpsertInAFinalStatement_KeepsTheFlag()
    {
        var p = Param("with src as (select 1 as id) insert into t (id) select id from src on conflict (id) do update set status = @s", "s");

        Assert.True(p.IsUpsertAssignment);
    }
}
