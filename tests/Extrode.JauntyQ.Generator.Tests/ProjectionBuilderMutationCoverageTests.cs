using Extrode.JauntyQ.Generator.Directives;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class ProjectionBuilderMutationCoverageTests
{
    private static ColumnSchema Col(string name, string dbType, int? precision = null, int? maxLength = null) =>
        new() { Name = name, DbType = dbType, IsNullable = false, Precision = precision, MaxLength = maxLength };

    private static TableSchema Table(string name, params ColumnSchema[] columns)
    {
        var table = new TableSchema { Name = name };
        foreach (var c in columns)
            table.Columns[c.Name] = c;
        return table;
    }

    private static DatabaseSchema Schema(string dialect = "postgres")
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        schema.Tables["t"] = Table("t", Col("id", "int"), Col("name", "varchar"), Col("price", "decimal"));
        schema.Tables["u"] = Table("u", Col("id", "int"), Col("name", "int"), Col("score", "int"),
            Col("tiny", "tinyint", precision: 1, maxLength: 3), Col("bits", "bit", maxLength: 8));
        return schema;
    }

    private static ProjectionModel Build(string sql, DatabaseSchema? schema = null, DirectiveModel? directives = null) =>
        ProjectionBuilder.Build(SqlParser.SqlParser.Parse(SqlTokenizer.Tokenize(sql), "Q"), schema ?? Schema(), directives);

    private static string TypeOf(string sql, DatabaseSchema? schema = null, DirectiveModel? directives = null) =>
        Assert.Single(Build(sql, schema, directives).Columns).Type;

    [Fact]
    public void Ordinals_CountUpAcrossPlainAndExpressionColumns()
    {
        var columns = Build("select id, count(*) as n, name from t").Columns;

        Assert.Equal(new[] { 0, 1, 2 }, columns.Select(c => c.Ordinal));
    }

    [Fact]
    public void Star_ExpandsToTheTableColumnsOnlyWithAscendingOrdinals()
    {
        var columns = Build("select * from t").Columns;

        Assert.Equal(new[] { "Id", "Name", "Price" }, columns.Select(c => c.Name));
        Assert.Equal(new[] { 0, 1, 2 }, columns.Select(c => c.Ordinal));
    }

    [Fact]
    public void UnresolvedPlainColumn_IsObject()
    {
        var column = Assert.Single(Build("select nosuch from t").Columns);

        Assert.Equal("Nosuch", column.Name);
        Assert.Equal("object", column.Type);
        Assert.False(column.IsExpression);
    }

    [Fact]
    public void SourceName_IsTheOutputAliasWhenThereIsOne()
    {
        var columns = Build("select name as n, id from t").Columns;

        Assert.Equal(new[] { "n", "id" }, columns.Select(c => c.SourceName));
    }

    [Fact]
    public void StarOverAnUnaliasedLeftJoinedTable_MakesOnlyItsColumnsNullable()
    {
        var columns = Build("select * from t left join u on u.id = t.id").Columns;

        Assert.Equal(("id", "int"), (columns[0].SourceName, columns[0].Type));
        Assert.Equal(("id", "int?"), (columns[3].SourceName, columns[3].Type));
    }

    [Theory]
    [InlineData("select score from t left join u on u.id = t.id")]
    [InlineData("select score from t left join u x on x.id = t.id")]
    [InlineData("with c as (select id as score from t) select score from t left join c on c.score = t.id")]
    [InlineData("with c as (select id as score from t) select score from t left join c cc on cc.score = t.id")]
    public void UnqualifiedColumnOfALeftJoinedTable_IsNullable(string sql)
    {
        Assert.Equal("int?", TypeOf(sql));
    }

    [Theory]
    [InlineData("select x.tiny from t left join u x on x.id = t.id", "bool?")]
    [InlineData("select x.bits from t left join u x on x.id = t.id", "object?")]
    public void LeftJoinedColumn_MapsWithPrecisionBeforeMaxLength(string sql, string expected)
    {
        Assert.Equal(expected, TypeOf(sql, Schema("mysql")));
    }

    private static string CteChain(int length)
    {
        var ctes = Enumerable.Range(1, length)
            .Select(i => i == 1 ? "c1 as (select id from t)" : $"c{i} as (select id from c{i - 1})");
        return $"with {string.Join(", ", ctes)} select id from c{length}";
    }

    [Fact]
    public void CteChainNineDeep_ResolvesToTheBaseColumn()
    {
        Assert.Equal("int", TypeOf(CteChain(9)));
    }

    [Fact]
    public void CteChainTenDeep_StopsAtTheDepthGuard()
    {
        Assert.Equal("object", TypeOf(CteChain(10)));
    }

    [Fact]
    public void CteLookup_SkipsCtesWithOtherNames()
    {
        Assert.Equal("string", TypeOf("with a as (select id as v from t), b as (select name as v from t) select v from b"));
    }

    [Fact]
    public void CteWithReturningBody_ResolvesThroughTheReturningList()
    {
        Assert.Equal("int", TypeOf("with ins as (insert into t (name) values (@n) returning id) select id from ins"));
    }

    [Theory]
    [InlineData("with c (a) as (select id, name from t) select b from c")]
    [InlineData("with c (a, b) as (select id from t) select b from c")]
    public void DeclaredColumnWithNoMatchingOutput_IsObject(string sql)
    {
        Assert.Equal("object", TypeOf(sql));
    }

    [Theory]
    [InlineData("with c (a, a) as (select id, name from t) select a from c")]
    [InlineData("with c as (select id as v, name as v from t) select v from c")]
    public void RepeatedCteColumnName_ResolvesToTheFirst(string sql)
    {
        Assert.Equal("int", TypeOf(sql));
    }

    [Theory]
    [InlineData("with c as (select name from t) select name from c", "string")]
    [InlineData("with c as (select t.name from t) select name from c", "string")]
    [InlineData("with c as (select x.name from t x) select name from c", "string")]
    [InlineData("with c as (select y.name from t x join u y on y.id = x.id) select name from c", "int")]
    public void CteBodyColumn_ResolvesAgainstTheTableItIsQualifiedWith(string sql, string expected)
    {
        Assert.Equal(expected, TypeOf(sql));
    }

    [Fact]
    public void FirstMatchingTypeDirective_Wins()
    {
        var directives = new DirectiveModel
        {
            TypeDirectives = new List<TypeDirective> { new("total", "int"), new("total", "bigint") }
        };

        Assert.Equal("int?", TypeOf("select id + 1 as total from t", directives: directives));
    }

    [Fact]
    public void TypeDirective_WinsOverSumArgumentInference()
    {
        var directives = new DirectiveModel { TypeDirectives = new List<TypeDirective> { new("total", "int") } };

        Assert.Equal("int?", TypeOf("select sum(price) as total from t", directives: directives));
    }

    [Fact]
    public void ExpressionColumns_AreFlaggedWhetherOrNotTheyResolve()
    {
        var columns = Build("select count(*) as n, id + 1 as m from t").Columns;

        Assert.All(columns, c => Assert.True(c.IsExpression));
        Assert.Equal("object", columns[1].Type);
        Assert.Equal("m", columns[1].UnresolvedExpressionAlias);
    }
}
