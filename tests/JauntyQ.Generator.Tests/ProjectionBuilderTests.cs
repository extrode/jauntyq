using JauntyQ.Generator;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class ProjectionBuilderTests
{
    private static DatabaseSchema CreateTestSchema()
    {
        return new DatabaseSchema
        {
            Tables = new Dictionary<string, TableSchema>
            {
                ["products"] = new TableSchema
                {
                    Name = "products",
                    Columns = new Dictionary<string, ColumnSchema>
                    {
                        ["product_id"] = new ColumnSchema { Name = "product_id", DbType = "int", IsNullable = false },
                        ["product_name"] = new ColumnSchema { Name = "product_name", DbType = "varchar", IsNullable = false },
                        ["category_id"] = new ColumnSchema { Name = "category_id", DbType = "int", IsNullable = true },
                        ["unit_price"] = new ColumnSchema { Name = "unit_price", DbType = "decimal", IsNullable = false },
                        ["discontinued"] = new ColumnSchema { Name = "discontinued", DbType = "bool", IsNullable = false }
                    }
                },
                ["categories"] = new TableSchema
                {
                    Name = "categories",
                    Columns = new Dictionary<string, ColumnSchema>
                    {
                        ["category_id"] = new ColumnSchema { Name = "category_id", DbType = "int", IsNullable = false },
                        ["category_name"] = new ColumnSchema { Name = "category_name", DbType = "varchar", IsNullable = false }
                    }
                }
            }
        };
    }

    private static QueryModel ParseSql(string sql, string name = "TestQuery")
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        return SqlParser.SqlParser.Parse(tokens, name);
    }

    [Fact]
    public void Build_SimpleQuery_ProducesCorrectProjection()
    {
        var query = ParseSql("select p.product_id, p.product_name from products p", "GetProducts");
        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Equal("GetProducts", projection.Name);
        Assert.Equal(2, projection.Columns.Count);

        Assert.Equal("ProductId", projection.Columns[0].Name);
        Assert.Equal("int", projection.Columns[0].Type);

        Assert.Equal("ProductName", projection.Columns[1].Name);
        Assert.Equal("string", projection.Columns[1].Type);
    }

    [Fact]
    public void Build_NullableColumn_ProducesNullableType()
    {
        var query = ParseSql("select p.category_id from products p", "GetProducts");
        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Single(projection.Columns);
        Assert.Equal("int?", projection.Columns[0].Type);
    }

    [Fact]
    public void Build_ColumnWithAlias_UsesPascalCaseAlias()
    {
        var query = ParseSql("select p.product_name as name from products p", "GetProducts");
        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Single(projection.Columns);
        Assert.Equal("Name", projection.Columns[0].Name);
        Assert.Equal("string", projection.Columns[0].Type);
    }

    [Fact]
    public void Build_JoinQuery_ProjectsFromMultipleTables()
    {
        var query = ParseSql(@"
select p.product_id, p.product_name, c.category_name
from products p
join categories c on p.category_id = c.category_id", "GetProductsByCategory");

        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Equal("GetProductsByCategory", projection.Name);
        Assert.Equal(3, projection.Columns.Count);
        Assert.Equal("ProductId", projection.Columns[0].Name);
        Assert.Equal("ProductName", projection.Columns[1].Name);
        Assert.Equal("CategoryName", projection.Columns[2].Name);
    }

    [Fact]
    public void Build_OrdinalsAreSequential()
    {
        var query = ParseSql("select p.product_id, p.product_name, p.unit_price from products p", "GetProducts");
        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Equal(0, projection.Columns[0].Ordinal);
        Assert.Equal(1, projection.Columns[1].Ordinal);
        Assert.Equal(2, projection.Columns[2].Ordinal);
    }

    [Fact]
    public void PascalCase_SnakeCaseInput_ConvertsCorrectly()
    {
        Assert.Equal("ProductName", DialectMapper.ToPascalCase("product_name"));
        Assert.Equal("CategoryId", DialectMapper.ToPascalCase("category_id"));
        Assert.Equal("Id", DialectMapper.ToPascalCase("id"));
        Assert.Equal("OrderDate", DialectMapper.ToPascalCase("order_date"));
    }

    [Fact]
    public void TypeMapping_CommonTypes()
    {
        Assert.Equal("int", DialectMapper.MapDbTypeToCSharp("int", false));
        Assert.Equal("int?", DialectMapper.MapDbTypeToCSharp("int", true));
        Assert.Equal("string", DialectMapper.MapDbTypeToCSharp("varchar", false));
        Assert.Equal("string", DialectMapper.MapDbTypeToCSharp("varchar", true)); // string is always nullable ref type
        Assert.Equal("bool", DialectMapper.MapDbTypeToCSharp("bool", false));
        Assert.Equal("decimal", DialectMapper.MapDbTypeToCSharp("decimal", false));
        Assert.Equal("double", DialectMapper.MapDbTypeToCSharp("float", false));
        Assert.Equal("System.DateTime", DialectMapper.MapDbTypeToCSharp("datetime", false));
        Assert.Equal("System.Guid", DialectMapper.MapDbTypeToCSharp("uuid", false));
        Assert.Equal("long", DialectMapper.MapDbTypeToCSharp("bigint", false));
    }

    [Fact]
    public void TypeMapping_WithLength_StripsParens()
    {
        Assert.Equal("string", DialectMapper.MapDbTypeToCSharp("varchar(255)", false));
        Assert.Equal("decimal", DialectMapper.MapDbTypeToCSharp("decimal(10,2)", false));
    }

    [Fact]
    public void Build_DecimalColumn_MapsToDecimal()
    {
        var query = ParseSql("select p.unit_price from products p", "GetProducts");
        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Single(projection.Columns);
        Assert.Equal("decimal", projection.Columns[0].Type);
    }

    [Fact]
    public void Build_BoolColumn_MapsToBool()
    {
        var query = ParseSql("select p.discontinued from products p", "GetProducts");
        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Single(projection.Columns);
        Assert.Equal("bool", projection.Columns[0].Type);
    }
}
