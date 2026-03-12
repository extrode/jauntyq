using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

public class ParserTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        return SqlParser.Parse(tokens, name);
    }

    [Fact]
    public void SimpleSelect_ExtractsTableAndColumn()
    {
        var model = ParseSql("select product_id from products");

        Assert.Equal("TestQuery", model.Name);
        Assert.Single(model.Tables);
        Assert.Equal("products", model.Tables[0].TableName);
        Assert.Empty(model.Tables[0].Alias);

        Assert.Single(model.Columns);
        Assert.Equal("product_id", model.Columns[0].ColumnName);
        Assert.Empty(model.Columns[0].TableAlias);

        Assert.Empty(model.Joins);
        Assert.Empty(model.Parameters);
    }

    [Fact]
    public void AliasedSelect_ExtractsTableAliasAndColumnAlias()
    {
        var model = ParseSql("select p.product_name as name from products p");

        Assert.Single(model.Tables);
        Assert.Equal("products", model.Tables[0].TableName);
        Assert.Equal("p", model.Tables[0].Alias);

        Assert.Single(model.Columns);
        Assert.Equal("p", model.Columns[0].TableAlias);
        Assert.Equal("product_name", model.Columns[0].ColumnName);
        Assert.Equal("name", model.Columns[0].OutputAlias);
    }

    [Fact]
    public void JoinQuery_ExtractsTablesColumnsAndJoin()
    {
        var sql = @"
select p.product_id, p.product_name, c.category_name
from products p
join categories c on p.category_id = c.category_id";

        var model = ParseSql(sql);

        Assert.Equal(2, model.Tables.Count);
        Assert.Equal("products", model.Tables[0].TableName);
        Assert.Equal("p", model.Tables[0].Alias);
        Assert.Equal("categories", model.Tables[1].TableName);
        Assert.Equal("c", model.Tables[1].Alias);

        Assert.Equal(3, model.Columns.Count);
        Assert.Equal("product_id", model.Columns[0].ColumnName);
        Assert.Equal("p", model.Columns[0].TableAlias);
        Assert.Equal("product_name", model.Columns[1].ColumnName);
        Assert.Equal("category_name", model.Columns[2].ColumnName);
        Assert.Equal("c", model.Columns[2].TableAlias);

        Assert.Single(model.Joins);
        Assert.Equal("p", model.Joins[0].LeftTable);
        Assert.Equal("category_id", model.Joins[0].LeftColumn);
        Assert.Equal("c", model.Joins[0].RightTable);
        Assert.Equal("category_id", model.Joins[0].RightColumn);
    }

    [Fact]
    public void MultipleParameters_AllExtracted()
    {
        var sql = @"
select p.product_id, p.product_name
from products p
where p.price > @minPrice and p.price < @maxPrice";

        var model = ParseSql(sql);

        Assert.Equal(2, model.Parameters.Count);
        Assert.Equal("minPrice", model.Parameters[0].Name);
        Assert.Equal("maxPrice", model.Parameters[1].Name);
    }

    [Fact]
    public void StarSelect_ProducesStarColumnRef()
    {
        var model = ParseSql("select * from products");

        Assert.Single(model.Columns);
        Assert.Equal("*", model.Columns[0].ColumnName);
        Assert.Empty(model.Columns[0].TableAlias);
    }

    [Fact]
    public void ReferenceQuery_FullParse()
    {
        var sql = @"
select p.product_id, p.product_name
from products p
join categories c on p.category_id = c.category_id
where p.category_id = @categoryId";

        var model = ParseSql(sql, "GetProductsByCategory");

        Assert.Equal("GetProductsByCategory", model.Name);
        Assert.Equal(2, model.Tables.Count);
        Assert.Equal(2, model.Columns.Count);
        Assert.Single(model.Joins);
        Assert.Single(model.Parameters);
        Assert.Equal("categoryId", model.Parameters[0].Name);
    }

    [Fact]
    public void LeftJoin_ParsedCorrectly()
    {
        var sql = @"
select p.product_id, c.category_name
from products p
left join categories c on p.category_id = c.category_id";

        var model = ParseSql(sql);

        Assert.Equal(2, model.Tables.Count);
        Assert.Single(model.Joins);
        Assert.Equal("p", model.Joins[0].LeftTable);
        Assert.Equal("c", model.Joins[0].RightTable);
    }

    [Fact]
    public void MultipleColumns_AllExtracted()
    {
        var sql = "select product_id, product_name, category_id, unit_price from products";
        var model = ParseSql(sql);

        Assert.Equal(4, model.Columns.Count);
        Assert.Equal("product_id", model.Columns[0].ColumnName);
        Assert.Equal("product_name", model.Columns[1].ColumnName);
        Assert.Equal("category_id", model.Columns[2].ColumnName);
        Assert.Equal("unit_price", model.Columns[3].ColumnName);
    }

    [Fact]
    public void DuplicateParameters_DeduplicatedInModel()
    {
        var sql = @"
select product_id from products
where category_id = @categoryId and supplier_id = @supplierId and active = @categoryId";

        var model = ParseSql(sql);

        // @categoryId appears twice but should only be in the model once
        Assert.Equal(2, model.Parameters.Count);
        Assert.Equal("categoryId", model.Parameters[0].Name);
        Assert.Equal("supplierId", model.Parameters[1].Name);
    }

    [Fact]
    public void ParameterBinding_QualifiedColumn()
    {
        var sql = @"
select p.product_id from products p
where p.category_id = @categoryId";

        var model = ParseSql(sql);

        Assert.Single(model.Parameters);
        Assert.Equal("p", model.Parameters[0].BoundTableAlias);
        Assert.Equal("category_id", model.Parameters[0].BoundColumnName);
    }

    [Fact]
    public void ParameterBinding_UnqualifiedColumn()
    {
        var sql = "select product_id from products where category_id = @categoryId";
        var model = ParseSql(sql);

        Assert.Single(model.Parameters);
        Assert.Empty(model.Parameters[0].BoundTableAlias);
        Assert.Equal("category_id", model.Parameters[0].BoundColumnName);
    }

    [Fact]
    public void ParameterBinding_MultipleParams()
    {
        var sql = @"
select p.product_id from products p
where p.category_id = @categoryId and p.unit_price > @minPrice";

        var model = ParseSql(sql);

        Assert.Equal(2, model.Parameters.Count);
        Assert.Equal("category_id", model.Parameters[0].BoundColumnName);
        Assert.Equal("unit_price", model.Parameters[1].BoundColumnName);
    }

    [Fact]
    public void ParameterBinding_ReversedOrder()
    {
        // @param = column (reversed)
        var sql = "select product_id from products where @categoryId = category_id";
        var model = ParseSql(sql);

        Assert.Single(model.Parameters);
        Assert.Equal("category_id", model.Parameters[0].BoundColumnName);
    }

    [Fact]
    public void ParameterBinding_LikeOperator()
    {
        var sql = "select product_id from products where product_name like @searchTerm";
        var model = ParseSql(sql);

        Assert.Single(model.Parameters);
        Assert.Equal("product_name", model.Parameters[0].BoundColumnName);
    }

    [Fact]
    public void UnsupportedConstructs_UnionDetected()
    {
        var sql = "select product_id from products union select category_id from categories";
        var model = ParseSql(sql);

        Assert.Contains("UNION", model.UnsupportedConstructs);
    }

    [Fact]
    public void UnsupportedConstructs_CteDetected()
    {
        var sql = "with cte as (select product_id from products) select product_id from cte";
        var model = ParseSql(sql);

        Assert.Contains("CTE", model.UnsupportedConstructs);
    }

    // ── Statement type detection ──────────────────────────

    [Fact]
    public void Select_DetectedAsSelectType()
    {
        var model = ParseSql("select product_id from products");
        Assert.Equal(StatementType.Select, model.StatementType);
    }

    [Fact]
    public void Insert_DetectedAsInsertType()
    {
        var model = ParseSql("INSERT INTO Products (ProductName) VALUES (@ProductName)");
        Assert.Equal(StatementType.Insert, model.StatementType);
    }

    [Fact]
    public void Update_DetectedAsUpdateType()
    {
        var model = ParseSql("UPDATE Products SET ProductName = @ProductName WHERE ProductID = @ProductID");
        Assert.Equal(StatementType.Update, model.StatementType);
    }

    [Fact]
    public void Delete_DetectedAsDeleteType()
    {
        var model = ParseSql("DELETE FROM Products WHERE ProductID = @ProductID");
        Assert.Equal(StatementType.Delete, model.StatementType);
    }

    // ── INSERT parsing ────────────────────────────────────

    [Fact]
    public void Insert_ExtractsTargetTable()
    {
        var model = ParseSql("INSERT INTO Products (ProductName, UnitPrice) VALUES (@ProductName, @UnitPrice)");

        Assert.Equal("Products", model.TargetTable);
        Assert.Single(model.Tables);
        Assert.Equal("Products", model.Tables[0].TableName);
    }

    [Fact]
    public void Insert_ExtractsParameters()
    {
        var model = ParseSql("INSERT INTO Products (ProductName, UnitPrice) VALUES (@ProductName, @UnitPrice)");

        Assert.Equal(2, model.Parameters.Count);
        Assert.Equal("ProductName", model.Parameters[0].Name);
        Assert.Equal("UnitPrice", model.Parameters[1].Name);
    }

    [Fact]
    public void Insert_BindsParametersToColumnsPositionally()
    {
        var model = ParseSql("INSERT INTO Products (ProductName, CategoryID, UnitPrice) VALUES (@ProductName, @CategoryID, @UnitPrice)");

        Assert.Equal(3, model.Parameters.Count);
        Assert.Equal("ProductName", model.Parameters[0].BoundColumnName);
        Assert.Equal("CategoryID", model.Parameters[1].BoundColumnName);
        Assert.Equal("UnitPrice", model.Parameters[2].BoundColumnName);
    }

    [Fact]
    public void Insert_NoColumnsInSelectProjection()
    {
        var model = ParseSql("INSERT INTO Products (ProductName) VALUES (@ProductName)");
        Assert.Empty(model.Columns); // INSERT has no SELECT projection
    }

    // ── UPDATE parsing ────────────────────────────────────

    [Fact]
    public void Update_ExtractsTargetTable()
    {
        var model = ParseSql("UPDATE Products SET ProductName = @ProductName WHERE ProductID = @ProductID");

        Assert.Equal("Products", model.TargetTable);
        Assert.Single(model.Tables);
        Assert.Equal("Products", model.Tables[0].TableName);
    }

    [Fact]
    public void Update_BindsSetParameters()
    {
        var model = ParseSql("UPDATE Products SET ProductName = @ProductName, UnitPrice = @UnitPrice WHERE ProductID = @ProductID");

        Assert.Equal(3, model.Parameters.Count);

        var nameParam = model.Parameters.First(p => p.Name == "ProductName");
        Assert.Equal("ProductName", nameParam.BoundColumnName);

        var priceParam = model.Parameters.First(p => p.Name == "UnitPrice");
        Assert.Equal("UnitPrice", priceParam.BoundColumnName);

        var idParam = model.Parameters.First(p => p.Name == "ProductID");
        Assert.Equal("ProductID", idParam.BoundColumnName);
    }

    [Fact]
    public void Update_ExtractsWhereParameters()
    {
        var model = ParseSql("UPDATE Products SET ProductName = @ProductName WHERE ProductID = @ProductID");

        var idParam = model.Parameters.First(p => p.Name == "ProductID");
        Assert.Equal("ProductID", idParam.BoundColumnName);
    }

    // ── DELETE parsing ────────────────────────────────────

    [Fact]
    public void Delete_ExtractsTargetTable()
    {
        var model = ParseSql("DELETE FROM Products WHERE ProductID = @ProductID");

        Assert.Equal("Products", model.TargetTable);
        Assert.Single(model.Tables);
        Assert.Equal("Products", model.Tables[0].TableName);
    }

    [Fact]
    public void Delete_ExtractsWhereParameterBinding()
    {
        var model = ParseSql("DELETE FROM Products WHERE ProductID = @ProductID");

        Assert.Single(model.Parameters);
        Assert.Equal("ProductID", model.Parameters[0].Name);
        Assert.Equal("ProductID", model.Parameters[0].BoundColumnName);
    }

    [Fact]
    public void Delete_WithoutFrom_StillWorks()
    {
        var model = ParseSql("DELETE Products WHERE ProductID = @ProductID");

        Assert.Equal(StatementType.Delete, model.StatementType);
        Assert.Equal("Products", model.TargetTable);
    }

    // ── Parameter case preservation ───────────────────────

    [Fact]
    public void ParameterName_PreservesOriginalCase()
    {
        var model = ParseSql("SELECT * FROM Products WHERE CategoryID = @CategoryID");
        Assert.Single(model.Parameters);
        Assert.Equal("CategoryID", model.Parameters[0].Name);
    }
}
