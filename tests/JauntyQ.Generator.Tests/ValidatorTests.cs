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
    public void TableNameCasingDiffersFromSchema_NoFalseJNT2001OrJNT2002()
    {
        // The schema snapshot's key is lowercase "products" (CreateTestSchema),
        // but the query spells it "Products". Alias resolution already matches
        // case-insensitively; the schema.Tables/Columns lookups QueryValidator
        // does afterward must too, or this legitimately valid query would be
        // rejected with a spurious "table does not exist" / "column does not
        // exist" error.
        var query = ParseSql(@"
select p.product_id, p.product_name
from Products p
where p.category_id = @categoryId");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.DoesNotContain(errors, e => e.Code == "JNT2001");
        Assert.DoesNotContain(errors, e => e.Code == "JNT2002");
    }

    [Fact]
    public void SchemaQualifiedTable_NoErrors()
    {
        // dbo.products must resolve against the bare "products" table in the
        // schema snapshot, same as the unqualified form above.
        var query = ParseSql(@"
select p.product_id, p.product_name
from dbo.products p
where p.category_id = @categoryId");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Empty(errors);
    }

    [Fact]
    public void MissingColumn_JNT2002()
    {
        var query = ParseSql(@"
select p.product_title
from products p");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Single(errors);
        Assert.Equal("JNT2002", errors[0].Code);
        Assert.Contains("product_title", errors[0].Message);
        Assert.Contains("products", errors[0].Message);
    }

    [Fact]
    public void MissingTable_JNT2001()
    {
        var query = ParseSql("select order_id from orders");
        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JNT2001");
        Assert.Contains(errors, e => e.Message.Contains("orders"));
    }

    [Fact]
    public void AmbiguousColumn_JNT2003()
    {
        // category_id exists in both products and categories
        var query = ParseSql(@"
select category_id
from products p
join categories c on p.category_id = c.category_id");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JNT2003");
        Assert.Contains(errors, e => e.Message.Contains("Ambiguous"));
    }

    [Fact]
    public void EmptyQuery_JNT3001()
    {
        var query = new QueryModel { Name = "EmptyQuery" };
        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Single(errors);
        Assert.Equal("JNT3001", errors[0].Code);
        Assert.Equal(ValidationSeverity.Warning, errors[0].Severity);
    }

    [Fact]
    public void NullSchema_JNT6001()
    {
        var query = ParseSql("select product_id from products");
        var errors = QueryValidator.Validate(query, null);

        Assert.Single(errors);
        Assert.Equal("JNT6001", errors[0].Code);
    }

    [Fact]
    public void InvalidJoinColumn_JNT2002()
    {
        var query = ParseSql(@"
select p.product_id, c.category_name
from products p
join categories c on p.cat_id = c.category_id");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JNT2002" && e.Message.Contains("cat_id"));
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
    public void DuplicateParameter_JNT4004()
    {
        // Manually construct a query with duplicate parameters
        var query = new QueryModel { Name = "DuplicateParamQuery" };
        query.Tables.Add(new TableRef { TableName = "products", Alias = "p" });
        query.Columns.Add(new ColumnRef { TableAlias = "p", ColumnName = "product_id", OutputAlias = "" });
        query.Parameters.Add(new ParameterRef { Name = "categoryId" });
        query.Parameters.Add(new ParameterRef { Name = "categoryId" });

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JNT4004");
        Assert.Contains(errors, e => e.Message.Contains("categoryId"));
    }

    [Fact]
    public void UnionQuery_JNT1001()
    {
        var query = ParseSql(@"
select p.product_id from products p
union
select c.category_id from categories c");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JNT1001");
        Assert.Contains(errors, e => e.Message.Contains("UNION"));
        Assert.Equal(ValidationSeverity.Warning, errors.First(e => e.Code == "JNT1001").Severity);
    }

    [Fact]
    public void InSubquery_SingleColumn_NoErrors()
    {
        // A WHERE-clause IN (SELECT ...) predicate with a single-column inner
        // SELECT is supported; it must not raise JNT1001 SUBQUERY.
        var query = ParseSql(@"
select p.product_id from products p
where p.category_id in (select c.category_id from categories c)");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.DoesNotContain(errors, e => e.Code == "JNT1001");
        Assert.Empty(errors);
    }

    [Fact]
    public void InSubquery_TwoColumns_JNT3007()
    {
        var query = ParseSql(@"
select p.product_id from products p
where p.category_id in (select c.category_id, c.category_name from categories c)");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JNT3007");
        Assert.DoesNotContain(errors, e => e.Code == "JNT1001");
    }

    [Fact]
    public void NotInSubquery_SingleColumn_NoErrors()
    {
        var query = ParseSql(@"
select p.product_id from products p
where p.category_id not in (select c.category_id from categories c)");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Empty(errors);
    }

    [Fact]
    public void ExistsSubquery_NoErrors()
    {
        var query = ParseSql(@"
select p.product_id from products p
where exists (select c.category_id from categories c where c.category_id = @cat)");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Empty(errors);
    }

    [Fact]
    public void NotExistsSubquery_NoErrors()
    {
        var query = ParseSql(@"
select p.product_id from products p
where not exists (select c.category_id from categories c)");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Empty(errors);
    }

    [Fact]
    public void Subquery_UnknownColumn_JNT2002()
    {
        // A column that does not exist inside the subquery's own table is a
        // normal JNT2002 against the subquery scope.
        var query = ParseSql(@"
select p.product_id from products p
where p.category_id in (select c.nonexistent from categories c)");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JNT2002" && e.Message.Contains("nonexistent"));
    }

    [Fact]
    public void Subquery_UnionInside_StillRejected_JNT1001()
    {
        var query = ParseSql(@"
select p.product_id from products p
where p.category_id in (
    select c.category_id from categories c
    union
    select c2.category_id from categories c2)");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JNT1001" && e.Message.Contains("UNION"));
    }

    [Fact]
    public void Subquery_ParamBindsInsideSubquery()
    {
        var query = ParseSql(@"
select p.product_id from products p
where p.product_id in (select c.category_id from categories c where c.category_id = @cat)");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Empty(errors);
        var cat = query.Parameters.Single(p => p.Name == "cat");
        Assert.Equal("category_id", cat.BoundColumnName);
    }

    [Fact]
    public void Cte_ResolvesVirtualColumns_NoErrors()
    {
        // Feature B: a CTE is an in-scope virtual table; its declared/projected
        // columns resolve for the final statement instead of raising JNT1001.
        var query = ParseSql(@"
with cte as (select product_id from products)
select product_id from cte");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.DoesNotContain(errors, e => e.Code == "JNT1001");
        Assert.DoesNotContain(errors, e => e.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Cte_UnknownVirtualColumn_JNT2002()
    {
        var query = ParseSql(@"
with cte as (select product_id from products)
select missing_col from cte");

        var errors = QueryValidator.Validate(query, CreateTestSchema());

        Assert.Contains(errors, e => e.Code == "JNT2002" && e.Message.Contains("missing_col"));
    }
}
