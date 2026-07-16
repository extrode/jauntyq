using JauntyQ.Analysis;
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
        Assert.Equal("string?", DialectMapper.MapDbTypeToCSharp("varchar", true)); // nullable column -> nullable reference type
        Assert.Equal("string", DialectMapper.MapDbTypeToCSharp("citext", false));
        Assert.Equal("string?", DialectMapper.MapDbTypeToCSharp("citext", true)); // case-insensitive text -> string
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

    [Fact]
    public void Build_CountExpression_DefaultsToLongOnNonSqlServerDialects()
    {
        var query = ParseSql("select count(*) as total from products", "GetCount");
        var schema = CreateTestSchema();
        schema.Dialect = "postgres";
        var projection = ProjectionBuilder.Build(query, schema);

        Assert.Single(projection.Columns);
        Assert.Equal("long", projection.Columns[0].Type);
    }

    [Fact]
    public void Build_CountExpression_MapsToIntOnSqlServer()
    {
        var query = ParseSql("select count(*) as total from products", "GetCount");
        var schema = CreateTestSchema();
        schema.Dialect = "sqlserver";
        var projection = ProjectionBuilder.Build(query, schema);

        Assert.Single(projection.Columns);
        Assert.Equal("int", projection.Columns[0].Type);
    }

    [Fact]
    public void Build_CountExpression_ExplicitTypeDirectiveOverridesSqlServerDefault()
    {
        var query = ParseSql("select count(*) as total from products", "GetCount");
        var schema = CreateTestSchema();
        schema.Dialect = "sqlserver";
        var directives = new Directives.DirectiveModel
        {
            TypeDirectives = new List<Directives.TypeDirective>
            {
                new("total", "bigint")
            }
        };
        var projection = ProjectionBuilder.Build(query, schema, directives);

        Assert.Single(projection.Columns);
        Assert.Equal("long", projection.Columns[0].Type);
    }

    [Fact]
    public void Build_CaseExpression_WithTypeDirective_MapsToNullable()
    {
        // Regression: the parser's shape inference used to mistake the "> 0"
        // inside the WHEN clause for a top-level comparison on the whole
        // expression, poisoning InferredNotNull=true even though an explicit
        // -- @type directive was present and the ELSE arm can be NULL. That
        // must map to a nullable type, not "int" with an unguarded reader
        // call that throws on the NULL arm.
        var query = ParseSql(
            "select case when unit_price > 0 then 1 else null end as label from products", "GetLabel");
        var directives = new Directives.DirectiveModel
        {
            TypeDirectives = new List<Directives.TypeDirective>
            {
                new("label", "int")
            }
        };
        var projection = ProjectionBuilder.Build(query, CreateTestSchema(), directives);

        Assert.Single(projection.Columns);
        Assert.Equal("int?", projection.Columns[0].Type);
    }

    [Fact]
    public void Build_SumExpression_DecimalArgument_MapsToNullableDecimal()
    {
        var query = ParseSql("select sum(unit_price) as total from products", "GetTotal");
        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Single(projection.Columns);
        Assert.Equal("decimal?", projection.Columns[0].Type);
    }

    [Fact]
    public void Build_AvgExpression_DecimalArgument_MapsToNullableDecimal()
    {
        var query = ParseSql("select avg(unit_price) as avg_price from products", "GetAvg");
        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Single(projection.Columns);
        Assert.Equal("decimal?", projection.Columns[0].Type);
    }

    [Fact]
    public void Build_SumExpression_IntArgument_StaysUnresolved()
    {
        // Integer/float promotion rules vary by dialect and aren't guessed here —
        // still requires an explicit -- @type directive.
        var query = ParseSql("select sum(product_id) as total from products", "GetTotal");
        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Single(projection.Columns);
        Assert.Equal("object", projection.Columns[0].Type);
        Assert.Equal("total", projection.Columns[0].UnresolvedExpressionAlias);
    }

    [Fact]
    public void Build_SumExpression_WrappedInOuterCall_StaysUnresolved()
    {
        // round(sum(...), 2) is not a bare sum(<col>) shape, so it isn't inferred.
        var query = ParseSql("select round(sum(unit_price), 2) as total from products", "GetTotal");
        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Single(projection.Columns);
        Assert.Equal("object", projection.Columns[0].Type);
        Assert.Equal("total", projection.Columns[0].UnresolvedExpressionAlias);
    }

    [Fact]
    public void Build_InnerJoin_KeepsNotNullColumnsNonNullable()
    {
        // Baseline: an INNER (or plain) JOIN requires a match on both sides,
        // so the schema's own NOT NULL constraint holds for the projection.
        var query = ParseSql(@"
select p.product_id, c.category_name
from products p
join categories c on p.category_id = c.category_id", "GetProductsByCategory");

        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Equal("string", projection.Columns[1].Type);
    }

    [Fact]
    public void Build_LeftJoin_ForcesJoinedTableColumnsNullable()
    {
        // categories.category_name is NOT NULL in the schema, but a LEFT JOIN
        // with no match produces NULL for every column on the joined side --
        // the projection must reflect that or a plain reader.GetString()
        // throws at runtime the first time a product has no category.
        var query = ParseSql(@"
select p.product_id, c.category_name
from products p
left join categories c on p.category_id = c.category_id", "GetProductsByCategory");

        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Equal("int", projection.Columns[0].Type);
        Assert.Equal("string?", projection.Columns[1].Type);
    }

    [Fact]
    public void Build_LeftJoin_StarExpansion_ForcesJoinedTableColumnsNullable()
    {
        var query = ParseSql(@"
select *
from products p
left join categories c on p.category_id = c.category_id", "GetProductsByCategory");

        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        // products (the non-optional side) keeps its own nullability
        var productName = Assert.Single(projection.Columns, c => c.Name == "ProductName");
        Assert.Equal("string", productName.Type);

        // categories (the LEFT-joined, optional side) is forced nullable,
        // even though the schema itself declares it NOT NULL
        var categoryName = Assert.Single(projection.Columns, c => c.Name == "CategoryName");
        Assert.Equal("string?", categoryName.Type);
    }

    [Fact]
    public void Build_RightJoin_ForcesPreservedSideTableColumnsNullable()
    {
        // RIGHT JOIN flips which side is optional: products (the preceding,
        // "left" table in the FROM/JOIN chain) is now the side that can be
        // all-NULL when no matching category exists on the right.
        var query = ParseSql(@"
select p.product_name, c.category_name
from products p
right join categories c on p.category_id = c.category_id", "GetCategoriesWithProducts");

        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Equal("string?", projection.Columns[0].Type);
        Assert.Equal("string", projection.Columns[1].Type);
    }

    [Fact]
    public void Build_FullJoin_ForcesBothSidesTableColumnsNullable()
    {
        var query = ParseSql(@"
select p.product_name, c.category_name
from products p
full join categories c on p.category_id = c.category_id", "GetAllProductsAndCategories");

        var projection = ProjectionBuilder.Build(query, CreateTestSchema());

        Assert.Equal("string?", projection.Columns[0].Type);
        Assert.Equal("string?", projection.Columns[1].Type);
    }

    [Fact]
    public void Build_SumExpression_ExplicitTypeDirectiveOverridesInference()
    {
        var query = ParseSql("select sum(unit_price) as total from products", "GetTotal");
        var schema = CreateTestSchema();
        var directives = new Directives.DirectiveModel
        {
            TypeDirectives = new List<Directives.TypeDirective>
            {
                new("total", "float")
            }
        };
        var projection = ProjectionBuilder.Build(query, schema, directives);

        Assert.Single(projection.Columns);
        Assert.Equal("double?", projection.Columns[0].Type);
    }
}
