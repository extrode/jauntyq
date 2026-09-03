using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>
/// A schema/catalog-qualified table reference (e.g. "dbo.Products", routine on
/// SQL Server/Postgres) must resolve to the bare table name. The tokenizer
/// merges a dotted FROM/JOIN/INSERT INTO/UPDATE/DELETE FROM identifier into
/// one Identifier token (dots are part of an identifier's character class),
/// and DatabaseSchema.Tables is keyed by bare table name with no
/// schema/catalog concept — so "dbo.Products" once became the literal,
/// unmatchable table name "dbo.Products" instead of "Products".
/// </summary>
public class SchemaQualifiedNameTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    [Theory]
    [InlineData("select p.ProductId from dbo.Products p")]
    [InlineData("select p.ProductId from public.Products p")]
    [InlineData("select p.ProductId from server.dbo.Products p")]
    public void SchemaQualifiedFromTable_ResolvesToBareTableName(string sql)
    {
        var model = ParseSql(sql);

        var table = Assert.Single(model.Tables);
        Assert.Equal("Products", table.TableName);
    }

    [Fact]
    public void SchemaQualifiedJoinTable_ResolvesToBareTableName()
    {
        var model = ParseSql(@"
select p.ProductId
from dbo.Products p
join dbo.Categories c on p.CategoryId = c.CategoryId");

        Assert.Equal(2, model.Tables.Count);
        Assert.Equal("Products", model.Tables[0].TableName);
        Assert.Equal("Categories", model.Tables[1].TableName);

        var join = Assert.Single(model.Joins);
        Assert.Equal("p", join.LeftTable);
        Assert.Equal("CategoryId", join.LeftColumn);
        Assert.Equal("c", join.RightTable);
        Assert.Equal("CategoryId", join.RightColumn);
    }

    [Theory]
    [InlineData(StatementType.Insert, "insert into dbo.Products (ProductId) values (@id)")]
    [InlineData(StatementType.Update, "update dbo.Products set ProductName = @name where ProductId = @id")]
    [InlineData(StatementType.Delete, "delete from dbo.Products where ProductId = @id")]
    public void SchemaQualifiedTargetTable_ResolvesToBareTableName(StatementType expectedType, string sql)
    {
        var model = ParseSql(sql);

        Assert.Equal(expectedType, model.StatementType);
        Assert.Equal("Products", model.TargetTable);
        var table = Assert.Single(model.Tables);
        Assert.Equal("Products", table.TableName);
    }

    [Fact]
    public void ThreePartColumnReference_ResolvesAliasToInnermostSegment()
    {
        // No table alias given, so the column is qualified with the full
        // schema-qualified name exactly as written.
        var model = ParseSql("select dbo.Products.ProductId from dbo.Products");

        var col = Assert.Single(model.Columns);
        Assert.False(col.IsExpression);
        Assert.Equal("Products", col.TableAlias);
        Assert.Equal("ProductId", col.ColumnName);
    }
}
