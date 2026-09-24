using System.Linq;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class SqlParserPart5SurvivorMutationCoverageTests
{
    private static QueryModel ParseSql(string sql)
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), "TestQuery");

    [Theory]
    [InlineData("INSERT")]
    [InlineData("INSERT INTO")]
    [InlineData("INSERT INTO t")]
    [InlineData("INSERT INTO t (a) SELECT @a")]
    [InlineData("INSERT INTO t (a) SELECT @a FROM u")]
    [InlineData("UPDATE")]
    public void Parse_TokenListWithoutEndMarker_DoesNotThrow(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        tokens.RemoveAt(tokens.Count - 1);
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.End);

        var ex = Record.Exception(() => SqlParser.Parse(tokens, "TestQuery"));

        Assert.Null(ex);
    }

    [Fact]
    public void Insert_QuotedTargetTableNamedInto_IsTheTargetTable()
    {
        var model = ParseSql("INSERT \"INTO\" (a) VALUES (@a)");

        Assert.Equal("INTO", model.TargetTable);
        Assert.Equal("a", model.Parameters.Single(p => p.Name == "a").BoundColumnName);
    }

    [Fact]
    public void InsertSelect_JoinedSource_JoinLandsOnInsertModel()
    {
        var model = ParseSql("insert into t (a) select u.x from u join v on v.id = u.id");

        Assert.Single(model.Joins);
        Assert.Contains(model.Tables, t => t.TableName == "v");
    }

    [Fact]
    public void InsertSelect_ParameterInsideArithmeticItem_IsNotBound()
    {
        var model = ParseSql("insert into t (a) select @p + 1 from u");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal(string.Empty, p.BoundColumnName);
        Assert.False(p.IsWriteTarget);
    }

    [Fact]
    public void InsertSelect_ScalarSubqueryItemBeforeParam_ParamBindsToSecondColumn()
    {
        var model = ParseSql("insert into t (a, b) select (select max(x) from u), @b from v");

        var b = model.Parameters.Single(x => x.Name == "b");
        Assert.Equal("b", b.BoundColumnName);
        Assert.True(b.IsWriteTarget);
    }

    [Fact]
    public void InsertSelect_SelectListWithoutFrom_BindsParam()
    {
        var model = ParseSql("insert into t (a) select @a");

        var a = model.Parameters.Single(x => x.Name == "a");
        Assert.Equal("a", a.BoundColumnName);
        Assert.True(a.IsWriteTarget);
    }

    [Theory]
    [InlineData("insert into t (a) select (@p")]
    [InlineData("insert into t (a) select @p)")]
    public void InsertSelect_UnbalancedParenAroundParam_DoesNotBind(string sql)
    {
        var model = ParseSql(sql);

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal(string.Empty, p.BoundColumnName);
        Assert.False(p.IsWriteTarget);
    }

    [Fact]
    public void InsertValues_MoreValuesThanColumns_ExtraParamStaysUnbound()
    {
        var model = ParseSql("INSERT INTO t (a) VALUES (@a, @b)");

        Assert.Equal("a", model.Parameters.Single(x => x.Name == "a").BoundColumnName);
        var b = model.Parameters.Single(x => x.Name == "b");
        Assert.Equal(string.Empty, b.BoundColumnName);
        Assert.False(b.IsWriteTarget);
    }

    [Fact]
    public void InsertValues_MoreLiteralsThanColumns_ExtraLiteralIsNotRecorded()
    {
        var model = ParseSql("INSERT INTO t (a) VALUES (1, 2)");

        var literal = Assert.Single(model.Literals);
        Assert.Equal("1", literal.Value);
        Assert.Equal("a", literal.BoundColumnName);
    }

    [Fact]
    public void InsertValues_SameParamInTwoSlots_KeepsFirstColumn()
    {
        var model = ParseSql("INSERT INTO t (a, b) VALUES (@p, @p)");

        Assert.Equal("a", model.Parameters.Single(x => x.Name == "p").BoundColumnName);
    }

    [Theory]
    [InlineData("INSERT INTO t (a) VALUES (1 + 2)")]
    [InlineData("INSERT INTO t (a) VALUES (NULL)")]
    [InlineData("INSERT INTO t (a) VALUES (+5)")]
    [InlineData("INSERT INTO t (a) VALUES (-5 + 1)")]
    [InlineData("INSERT INTO t (a) VALUES (-)")]
    public void InsertValues_SlotThatIsNotALoneLiteral_RecordsNoLiteral(string sql)
    {
        var model = ParseSql(sql);

        Assert.DoesNotContain(model.Literals, l => l.BoundColumnName == "a");
    }

    [Fact]
    public void Update_TargetTableRef_AliasIsEmptyString()
    {
        var model = ParseSql("UPDATE t SET a = @a WHERE id = @id");

        Assert.Equal(string.Empty, model.Tables.Single(t => t.TableName == "t").Alias);
    }

    [Fact]
    public void Update_MultiTableJoinConditionParam_IsNotWriteTarget()
    {
        var model = ParseSql("UPDATE t1 JOIN t2 ON t2.id = @p SET t1.a = @a");

        Assert.False(model.Parameters.Single(x => x.Name == "p").IsWriteTarget);
        Assert.True(model.Parameters.Single(x => x.Name == "a").IsWriteTarget);
    }

    [Fact]
    public void Update_ParamInSetSubqueryPredicate_IsNotWriteTarget()
    {
        var model = ParseSql("UPDATE t SET a = (SELECT x FROM u WHERE u.id = @p), b = @b");

        Assert.False(model.Parameters.Single(x => x.Name == "p").IsWriteTarget);
        Assert.True(model.Parameters.Single(x => x.Name == "b").IsWriteTarget);
    }

    [Fact]
    public void Update_StrayCloseParenBeforeSet_StillFlagsSetParam()
    {
        var model = ParseSql("UPDATE t) SET a = @a");

        Assert.True(model.Parameters.Single(x => x.Name == "a").IsWriteTarget);
    }

    [Fact]
    public void Update_SetAssignmentAsLastTokens_IsWriteTarget()
    {
        var model = ParseSql("UPDATE t SET a = @a");

        Assert.True(model.Parameters.Single(x => x.Name == "a").IsWriteTarget);
    }
}
