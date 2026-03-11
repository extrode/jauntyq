using JauntyQ.Generator;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class ValidatorTests
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
                        ["category_id"] = new ColumnSchema { Name = "category_id", DbType = "int", IsNullable = true }
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
            },
            ForeignKeys = new List<ForeignKeySchema>
            {
                new ForeignKeySchema
                {
                    FromTable = "products", FromColumn = "category_id",
                    ToTable = "categories", ToColumn = "category_id"
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
    public void ValidQuery_NoErrors()
    {
        var query = ParseSql(@"
select p.product_id, p.product_name
from products p
where p.category_id = @categoryId");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Empty(errors);
    }

    [Fact]
    public void MissingColumn_JAUNTY001()
    {
        var query = ParseSql(@"
select p.product_title
from products p");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Single(errors);
        Assert.Equal("JAUNTY001", errors[0].Code);
        Assert.Contains("product_title", errors[0].Message);
        Assert.Contains("products", errors[0].Message);
    }

    [Fact]
    public void MissingTable_JAUNTY002()
    {
        var query = ParseSql("select order_id from orders");
        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JAUNTY002");
        Assert.Contains(errors, e => e.Message.Contains("orders"));
    }

    [Fact]
    public void AmbiguousColumn_JAUNTY003()
    {
        // category_id exists in both products and categories
        var query = ParseSql(@"
select category_id
from products p
join categories c on p.category_id = c.category_id");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JAUNTY003");
        Assert.Contains(errors, e => e.Message.Contains("Ambiguous"));
    }

    [Fact]
    public void EmptyQuery_JAUNTY004()
    {
        var query = new QueryModel { Name = "EmptyQuery" };
        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Single(errors);
        Assert.Equal("JAUNTY004", errors[0].Code);
        Assert.Equal(ValidationSeverity.Warning, errors[0].Severity);
    }

    [Fact]
    public void NullSchema_JAUNTY006()
    {
        var query = ParseSql("select product_id from products");
        var errors = QueryValidator.Validate(query, null);

        Assert.Single(errors);
        Assert.Equal("JAUNTY006", errors[0].Code);
    }

    [Fact]
    public void InvalidJoinColumn_JAUNTY001()
    {
        var query = ParseSql(@"
select p.product_id, c.category_name
from products p
join categories c on p.cat_id = c.category_id");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JAUNTY001" && e.Message.Contains("cat_id"));
    }

    [Fact]
    public void ValidJoinQuery_NoErrors()
    {
        var query = ParseSql(@"
select p.product_id, p.product_name, c.category_name
from products p
join categories c on p.category_id = c.category_id
where p.category_id = @categoryId");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Empty(errors);
    }

    [Fact]
    public void DuplicateParameter_JAUNTY005()
    {
        // Manually construct a query with duplicate parameters
        var query = new QueryModel { Name = "DuplicateParamQuery" };
        query.Tables.Add(new TableRef { TableName = "products", Alias = "p" });
        query.Columns.Add(new ColumnRef { TableAlias = "p", ColumnName = "product_id", OutputAlias = "" });
        query.Parameters.Add(new ParameterRef { Name = "categoryId" });
        query.Parameters.Add(new ParameterRef { Name = "categoryId" });

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JAUNTY005");
        Assert.Contains(errors, e => e.Message.Contains("categoryId"));
    }

    [Fact]
    public void UnionQuery_JAUNTY007()
    {
        var query = ParseSql(@"
select p.product_id from products p
union
select c.category_id from categories c");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JAUNTY007");
        Assert.Contains(errors, e => e.Message.Contains("UNION"));
        Assert.Equal(ValidationSeverity.Warning, errors.First(e => e.Code == "JAUNTY007").Severity);
    }

    [Fact]
    public void SubqueryDetected_JAUNTY007()
    {
        var query = ParseSql(@"
select p.product_id from products p
where p.category_id in (select c.category_id from categories c)");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JAUNTY007" && e.Message.Contains("SUBQUERY"));
    }

    [Fact]
    public void CteDetected_JAUNTY007()
    {
        var query = ParseSql(@"
with cte as (select product_id from products)
select product_id from cte");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JAUNTY007" && e.Message.Contains("CTE"));
    }
}
