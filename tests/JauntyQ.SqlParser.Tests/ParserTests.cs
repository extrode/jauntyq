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

        // A plain (INNER) JOIN doesn't put either side on the optional half
        // of the row -- both tables are required for a match.
        Assert.Equal(JoinKind.None, model.Tables[1].Join);
    }

    [Fact]
    public void JoinUsing_NoExplicitAlias_TableAliasNotCorruptedByUsingKeyword()
    {
        // "USING" is not a tokenizer keyword, so it tokenizes as a plain
        // Identifier just like a real alias. Without excluding it, the
        // table's alias-detection swallowed the literal word "USING" as
        // this table's alias, corrupting later unqualified references.
        var sql = @"
select p.product_id, categories.category_name
from products p
join categories using (category_id)";

        var model = ParseSql(sql);

        Assert.Equal(2, model.Tables.Count);
        Assert.Equal("categories", model.Tables[1].TableName);
        Assert.Equal(string.Empty, model.Tables[1].Alias);

        Assert.Single(model.Joins);
        Assert.Equal("p", model.Joins[0].LeftTable);
        Assert.Equal("category_id", model.Joins[0].LeftColumn);
        Assert.Equal("categories", model.Joins[0].RightTable);
        Assert.Equal("category_id", model.Joins[0].RightColumn);
    }

    [Fact]
    public void JoinUsing_WithExplicitAlias_ParsesJoinKey()
    {
        var sql = @"
select p.product_id, c.category_name
from products p
join categories c using (category_id)";

        var model = ParseSql(sql);

        Assert.Equal(2, model.Tables.Count);
        Assert.Equal("c", model.Tables[1].Alias);

        Assert.Single(model.Joins);
        Assert.Equal("p", model.Joins[0].LeftTable);
        Assert.Equal("category_id", model.Joins[0].LeftColumn);
        Assert.Equal("c", model.Joins[0].RightTable);
        Assert.Equal("category_id", model.Joins[0].RightColumn);
    }

    [Fact]
    public void JoinUsing_MultipleColumns_ParsesOneJoinPerColumn()
    {
        var sql = @"
select o.id
from order_items oi
join orders o using (order_id, tenant_id)";

        var model = ParseSql(sql);

        Assert.Equal(2, model.Joins.Count);
        Assert.Equal("order_id", model.Joins[0].LeftColumn);
        Assert.Equal("tenant_id", model.Joins[1].LeftColumn);
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

        // The joined table (categories) is the one that can be all-NULL.
        Assert.Equal(JoinKind.None, model.Tables[0].Join);
        Assert.Equal(JoinKind.Left, model.Tables[1].Join);
    }

    [Fact]
    public void InnerJoin_ExplicitKeyword_ParsedSameAsPlainJoin()
    {
        // AUD-R11 (§2.1-r10-join-forms): every existing plain-JOIN test uses
        // bare "join", never the explicit "inner join" spelling. The
        // join-modifier skip loop (SqlParser.Part2.cs) recognizes "INNER"
        // as a keyword to skip over but never sets any of sawLeft/sawRight/
        // sawFull for it, so it should fall through to JoinKind.None
        // exactly like a bare JOIN -- confirmed live rather than just by
        // reading the flag logic.
        var model = ParseSql(@"
select p.product_id, c.category_name
from products p
inner join categories c on p.category_id = c.category_id");

        Assert.Single(model.Joins);
        Assert.Equal(JoinKind.None, model.Tables[0].Join);
        Assert.Equal(JoinKind.None, model.Tables[1].Join);
    }

    [Fact]
    public void LeftOuterJoin_ParsedAsLeft()
    {
        var model = ParseSql(@"
select p.product_id, c.category_name
from products p
left outer join categories c on p.category_id = c.category_id");

        Assert.Equal(JoinKind.Left, model.Tables[1].Join);
    }

    [Fact]
    public void RightJoin_ParsedCorrectly()
    {
        var model = ParseSql(@"
select p.product_id, c.category_name
from products p
right join categories c on p.category_id = c.category_id");

        Assert.Equal(JoinKind.None, model.Tables[0].Join);
        Assert.Equal(JoinKind.Right, model.Tables[1].Join);
    }

    [Fact]
    public void FullJoin_ParsedCorrectly()
    {
        var model = ParseSql(@"
select p.product_id, c.category_name
from products p
full join categories c on p.category_id = c.category_id");

        Assert.Equal(JoinKind.Full, model.Tables[1].Join);
    }

    [Fact]
    public void FullOuterJoin_ParsedAsFull()
    {
        var model = ParseSql(@"
select p.product_id, c.category_name
from products p
full outer join categories c on p.category_id = c.category_id");

        Assert.Equal(JoinKind.Full, model.Tables[1].Join);
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
    public void ParameterBinding_BetweenOperator_BindsBothLowerAndUpperBound()
    {
        // "col BETWEEN @lo AND @hi" previously only bound @lo -- @hi's
        // BoundColumnName stayed empty, which made
        // CodeEmitter.InferCrudParameterType fall back to "object" for the
        // upper-bound parameter instead of the column's real C# type.
        var sql = "select product_id from products where unit_price between @minPrice and @maxPrice";
        var model = ParseSql(sql);

        Assert.Equal(2, model.Parameters.Count);
        Assert.Equal("unit_price", model.Parameters[0].BoundColumnName);
        Assert.Equal("BETWEEN", model.Parameters[0].ComparisonOp);
        Assert.Equal("unit_price", model.Parameters[1].BoundColumnName);
        Assert.Equal("BETWEEN", model.Parameters[1].ComparisonOp);
    }

    [Fact]
    public void ImplicitCommaJoin_RegistersAllTables_NotJustTheFirst()
    {
        // Old-style "FROM a, b" implicit join: ParseFrom previously stopped
        // after the first table + optional alias and never checked for a
        // following comma, so "categories c" was silently dropped from
        // model.Tables entirely -- any qualified reference to it (c.category_name)
        // would then fail to resolve against the schema at all.
        var sql = "select p.product_name, c.category_name from products p, categories c where p.category_id = c.category_id";
        var model = ParseSql(sql);

        Assert.Equal(2, model.Tables.Count);
        Assert.Equal("products", model.Tables[0].TableName);
        Assert.Equal("p", model.Tables[0].Alias);
        Assert.Equal("categories", model.Tables[1].TableName);
        Assert.Equal("c", model.Tables[1].Alias);
    }

    [Fact]
    public void ImplicitCommaJoin_ThreeTables_AllRegistered()
    {
        var sql = "select a.x, b.y, c.z from t1 a, t2 b, t3 c";
        var model = ParseSql(sql);

        Assert.Equal(3, model.Tables.Count);
        Assert.Equal("t1", model.Tables[0].TableName);
        Assert.Equal("t2", model.Tables[1].TableName);
        Assert.Equal("t3", model.Tables[2].TableName);
    }

    [Fact]
    public void UnsupportedConstructs_UnionDetected()
    {
        var sql = "select product_id from products union select category_id from categories";
        var model = ParseSql(sql);

        Assert.Contains("UNION", model.UnsupportedConstructs);
    }

    [Fact]
    public void Cte_NoLongerUnsupported()
    {
        // Feature B: data-modifying / data-selecting CTEs now parse instead of
        // being flagged as an unsupported construct.
        var sql = "with cte as (select product_id from products) select product_id from cte";
        var model = ParseSql(sql);

        Assert.DoesNotContain("CTE", model.UnsupportedConstructs);
        Assert.Single(model.Ctes);
        Assert.Equal("cte", model.Ctes[0].Name);
        Assert.Contains("product_id", model.Ctes[0].VirtualColumns);
        Assert.Equal(StatementType.Select, model.StatementType);
    }

    [Fact]
    public void ExtractPerfHints_OuterWhereAfterProjectionExistsWithGroupBy_StillDetected()
    {
        // A projection-list EXISTS(...) (Feature A expression, InferExpressionType
        // -> boolean) is a fully supported construct whose own inner SELECT is
        // deliberately left in the token stream by DetectUnsupportedConstructs
        // (IsExistsSubquery excludes it from the generic nested-SELECT check, and
        // ExtractPredicateSubqueries only ever lifts WHERE-clause IN/EXISTS
        // predicates, never a projection-list one). ExtractPerfHints' own
        // WHERE-region-boundary scan (SqlParser.Part4.cs) must therefore find the
        // *outer* statement's own top-level WHERE/GROUP/ORDER/HAVING keywords,
        // not whichever keyword of these shapes happens to appear first in the
        // whole token stream regardless of paren depth -- otherwise a nested
        // EXISTS(...) subquery carrying its own WHERE ... GROUP BY makes the scan
        // start inside the subquery and stop at the subquery's own GROUP keyword,
        // silently excluding everything from there on, including the real outer
        // WHERE clause's own non-sargable predicate.
        var sql = "select product_id, " +
                  "exists(select 1 from order_items oi where oi.product_id = products.product_id " +
                  "group by oi.customer_id having count(*) > 1) as has_repeat_orders " +
                  "from products " +
                  "where upper(product_name) = @name";
        var model = ParseSql(sql);

        Assert.Contains(model.PerfHints, h =>
            h.Kind == PerfHintKind.FunctionOnColumn &&
            h.FunctionName == "upper" &&
            h.BoundColumnName == "product_name");
    }

    [Fact]
    public void WithRecursive_Rejected()
    {
        var sql = "with recursive t as (select product_id from products) select product_id from t";
        var model = ParseSql(sql);

        Assert.True(model.WithRecursive);
        Assert.Contains("WITH RECURSIVE", model.UnsupportedConstructs);
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
    // ── Feature A: expression projections ─────────────────

    [Fact]
    public void ExpressionProjection_CapturedWithAliasAndInference()
    {
        var model = ParseSql("select id, (hashed_passphrase is not null) as has_passphrase from users");

        Assert.Equal(2, model.Columns.Count);
        Assert.False(model.Columns[0].IsExpression);
        var expr = model.Columns[1];
        Assert.True(expr.IsExpression);
        Assert.Equal("has_passphrase", expr.OutputAlias);
        Assert.Equal("boolean", expr.InferredDbType);
        Assert.True(expr.InferredNotNull);
        Assert.Empty(model.ExpressionsMissingAlias);
    }

    [Fact]
    public void QualifiedStar_ParsesAsStarColumn_WithTableAlias()
    {
        // Regression: the tokenizer reads "u.*"'s embedded '.' as part of the
        // Identifier token itself (so it comes through as Identifier("u.") +
        // Symbol("*") rather than merging), which used to fall through to the
        // expression-item path and demand a nonsensical explicit alias
        // instead of being recognized as a qualified star-select.
        var model = ParseSql("select u.* from users u");

        Assert.Single(model.Columns);
        Assert.False(model.Columns[0].IsExpression);
        Assert.Equal("*", model.Columns[0].ColumnName);
        Assert.Equal("u", model.Columns[0].TableAlias);
        Assert.Empty(model.ExpressionsMissingAlias);
    }

    [Fact]
    public void CountStar_InferredBigint_NotStarSelect()
    {
        var model = ParseSql("select count(*) as n from users");

        Assert.Single(model.Columns);
        var expr = model.Columns[0];
        Assert.True(expr.IsExpression);
        Assert.Equal("bigint", expr.InferredDbType);
        Assert.True(expr.InferredNotNull);
        // count(*) must NOT register a star-select column.
        Assert.DoesNotContain(model.Columns, c => c.ColumnName == "*");
    }

    [Fact]
    public void CountConcatenatedWithLiteral_NotInferredBigint()
    {
        // count(...) only determines bigint/NOT NULL when the call IS the
        // entire expression body -- not merely its head. "count(*) || ' rows'"
        // is a string, not a bigint: the count(...) call is combined with a
        // literal via the concatenation operator, so the shape check must
        // fail closed (leave InferredDbType empty) rather than claim bigint
        // for the whole expression, exactly as the sibling sum(...)/avg(...)
        // check already does for "sum(x) || ' total'" (its stricter
        // exact-4-token check naturally excludes this shape).
        var model = ParseSql("select count(*) || ' rows' as msg from users");

        Assert.Single(model.Columns);
        var expr = model.Columns[0];
        Assert.True(expr.IsExpression);
        Assert.Empty(expr.InferredDbType);
        Assert.False(expr.InferredNotNull);
    }

    [Fact]
    public void ComparisonExpression_InferredBoolean()
    {
        var model = ParseSql("select count(*) > 0 as is_in_use from users");

        Assert.Single(model.Columns);
        Assert.True(model.Columns[0].IsExpression);
        Assert.Equal("boolean", model.Columns[0].InferredDbType);
        Assert.True(model.Columns[0].InferredNotNull);
    }

    [Fact]
    public void ExistsExpression_InferredBoolean()
    {
        var model = ParseSql("select exists(select 1 from orders) as has_orders from users");

        Assert.True(model.Columns[0].IsExpression);
        Assert.Equal("boolean", model.Columns[0].InferredDbType);
    }

    [Fact]
    public void ExpressionMissingAlias_RecordedForDiagnostic()
    {
        var model = ParseSql("select (hashed_passphrase is not null) from users");

        Assert.Single(model.Columns);
        Assert.True(model.Columns[0].IsExpression);
        Assert.Single(model.ExpressionsMissingAlias);
    }

    [Fact]
    public void UnknownShapeExpression_NoInference()
    {
        var model = ParseSql("select (first_name || last_name) as full_name from users");

        Assert.True(model.Columns[0].IsExpression);
        Assert.Equal("full_name", model.Columns[0].OutputAlias);
        Assert.Empty(model.Columns[0].InferredDbType);
    }

    [Fact]
    public void CaseExpression_ComparisonInWhenClause_NotInferredAsBoolean()
    {
        // The CASE's own result type is its THEN/ELSE branch type (a string
        // here), not the boolean shape of the comparison inside its WHEN
        // clause. Must stay unresolved so the generator requires -- @type.
        var model = ParseSql(
            "select case when status = 1 then 'active' else 'inactive' end as label from users");

        Assert.True(model.Columns[0].IsExpression);
        Assert.Equal("label", model.Columns[0].OutputAlias);
        Assert.Empty(model.Columns[0].InferredDbType);
        Assert.False(model.Columns[0].InferredNotNull);
    }

    [Fact]
    public void CaseExpression_IsNullInWhenClause_NotInferredAsBoolean()
    {
        var model = ParseSql(
            "select case when deleted_at is null then 0 else 1 end as active_flag from users");

        Assert.True(model.Columns[0].IsExpression);
        Assert.Empty(model.Columns[0].InferredDbType);
        Assert.False(model.Columns[0].InferredNotNull);
    }

    [Fact]
    public void CaseExpression_ThreeWhenBranchesPlusElse_IsOneExpressionItem_NotSplitOrTruncated()
    {
        // AUD-R11 (§2.1-r9-multi-branch-case): every prior CASE test here has
        // exactly one WHEN branch. ParseExpressionItem's token-consumption
        // loop (SqlParser.cs) breaks on a top-level (paren-depth 0) comma or
        // IsClauseKeyword hit -- CASE/WHEN/THEN/ELSE/END are deliberately
        // NOT in IsClauseKeyword's set (confirmed by reading
        // SqlParser.Part2.cs), so nothing should truncate the scan partway
        // through a longer branch chain, but this was never actually
        // exercised with more than one branch. Also covers the "searched
        // CASE" form (CASE <expr> WHEN <value> THEN ...) rather than the
        // boolean-WHEN form the existing two tests use.
        var model = ParseSql(
            "select case category_id " +
            "when 1 then 'Beverages' " +
            "when 2 then 'Condiments' " +
            "when 3 then 'Produce' " +
            "else 'Other' end as category_label " +
            "from products");

        var col = Assert.Single(model.Columns);
        Assert.True(col.IsExpression);
        Assert.Equal("category_label", col.OutputAlias);
        Assert.Empty(model.ExpressionsMissingAlias);
        // The whole CASE...END run must be captured, not truncated at the
        // first WHEN/THEN/ELSE: the rendered SQL must contain every branch.
        Assert.Contains("Beverages", col.ExpressionSql);
        Assert.Contains("Condiments", col.ExpressionSql);
        Assert.Contains("Produce", col.ExpressionSql);
        Assert.Contains("Other", col.ExpressionSql);
        Assert.Contains("END", col.ExpressionSql);
    }

    [Fact]
    public void ScalarSubqueryExpression_NotInferredAsBoolean()
    {
        // The WHERE clause's "=" inside the subquery is not a top-level
        // comparison on the outer expression; the subquery's own result type
        // (bigint here, from count(*)) can't be inferred from shape alone.
        var model = ParseSql(
            "select (select count(*) from orders o where o.user_id = u.id) as order_count from users u");

        Assert.True(model.Columns[0].IsExpression);
        Assert.Equal("order_count", model.Columns[0].OutputAlias);
        Assert.Empty(model.Columns[0].InferredDbType);
        Assert.False(model.Columns[0].InferredNotNull);
    }

    // ── Feature B: CTEs, RETURNING, INSERT...SELECT ───────

    [Fact]
    public void SingleCte_ParsesBodyAndVirtualColumns()
    {
        var model = ParseSql("with c as (select product_id, product_name from products) select product_id from c");

        Assert.Single(model.Ctes);
        Assert.Equal("c", model.Ctes[0].Name);
        Assert.Equal(StatementType.Select, model.Ctes[0].Body.StatementType);
        Assert.Contains("product_id", model.Ctes[0].VirtualColumns);
        Assert.Contains("product_name", model.Ctes[0].VirtualColumns);
        Assert.Equal(StatementType.Select, model.StatementType);
    }

    [Fact]
    public void CteDeclaredColumns_OverrideBodyNames()
    {
        var model = ParseSql("with c (a, b) as (select product_id, product_name from products) select a from c");

        Assert.Single(model.Ctes);
        Assert.Equal(new[] { "a", "b" }, model.Ctes[0].VirtualColumns);
    }

    [Fact]
    public void ChainedCtes_LaterMayReferenceEarlier()
    {
        var model = ParseSql(
            "with a as (select product_id from products), " +
            "b as (select product_id from a) " +
            "select product_id from b");

        Assert.Equal(2, model.Ctes.Count);
        Assert.Equal("a", model.Ctes[0].Name);
        Assert.Equal("b", model.Ctes[1].Name);
    }

    [Fact]
    public void CteInsertReturning_ExposesReturningColumnsAsVirtual()
    {
        var model = ParseSql(
            "with new_row as (insert into products (product_name) values (@n) returning product_id) " +
            "select product_id from new_row");

        Assert.Single(model.Ctes);
        Assert.Equal(StatementType.Insert, model.Ctes[0].Body.StatementType);
        Assert.True(model.Ctes[0].Body.HasReturning);
        Assert.Contains("product_id", model.Ctes[0].VirtualColumns);
    }

    [Fact]
    public void DeleteCte_WithoutReturning_HasNoVirtualColumns()
    {
        var model = ParseSql(
            "with gone as (delete from products where product_id = @id) " +
            "delete from categories where category_id = @cid");

        Assert.Single(model.Ctes);
        Assert.Equal(StatementType.Delete, model.Ctes[0].Body.StatementType);
        Assert.Empty(model.Ctes[0].VirtualColumns);
        Assert.Equal(StatementType.Delete, model.StatementType);
    }

    [Fact]
    public void ReturningOnInsert_ParsedAsProjection()
    {
        var model = ParseSql("insert into products (product_name) values (@n) returning product_id, product_name");

        Assert.Equal(StatementType.Insert, model.StatementType);
        Assert.True(model.HasReturning);
        Assert.Equal(2, model.Returning.Count);
        Assert.Equal("product_id", model.Returning[0].ColumnName);
        Assert.Equal("product_name", model.Returning[1].ColumnName);
    }

    [Fact]
    public void ReturningExpression_TypedByInference()
    {
        var model = ParseSql("update products set product_name = @n where product_id = @id returning (product_name is not null) as ok");

        Assert.True(model.HasReturning);
        Assert.Single(model.Returning);
        Assert.True(model.Returning[0].IsExpression);
        Assert.Equal("boolean", model.Returning[0].InferredDbType);
    }

    [Fact]
    public void InsertSelect_BindsLoneParamsPositionally()
    {
        var model = ParseSql(
            "insert into products (product_id, product_name) select category_id, @name from categories");

        Assert.Equal(StatementType.Insert, model.StatementType);
        var pName = model.Parameters.Single(p => p.Name == "name");
        Assert.Equal("product_name", pName.BoundColumnName);
        Assert.True(pName.IsWriteTarget);
    }

    [Fact]
    public void Update_ParamAfterSetClauseSubqueryWithNestedWhere_StillMarkedAsWriteTarget()
    {
        // A scalar subquery inside the SET list can carry its own WHERE
        // (e.g. a correlated "copy this value from another row" column).
        // That nested WHERE must not be mistaken for the UPDATE's own
        // top-level WHERE: the write-target scan has to keep scanning past
        // it and still catch "last_editor = @editor" as a genuine SET
        // assignment, not treat it as a WHERE-side comparison parameter.
        var model = ParseSql(
            "update products set " +
            "unit_price = (select max(p2.unit_price) from products p2 where p2.category_id = category_id), " +
            "last_editor = @editor " +
            "where product_id = @id");

        var pEditor = model.Parameters.Single(p => p.Name == "editor");
        Assert.Equal("last_editor", pEditor.BoundColumnName);
        Assert.True(pEditor.IsWriteTarget);
    }

    [Fact]
    public void SelectTopN_SkipsModifierAndResolvesPlainColumns()
    {
        var model = ParseSql("select top 10 product_id, product_name from products");

        Assert.Empty(model.ExpressionsMissingAlias);
        Assert.Equal(2, model.Columns.Count);
        Assert.False(model.Columns[0].IsExpression);
        Assert.Equal("product_id", model.Columns[0].ColumnName);
        Assert.False(model.Columns[1].IsExpression);
        Assert.Equal("product_name", model.Columns[1].ColumnName);
    }

    [Fact]
    public void SelectTopParenN_SkipsModifierAndResolvesPlainColumns()
    {
        var model = ParseSql("select top (5) product_id from products");

        Assert.Empty(model.ExpressionsMissingAlias);
        Assert.Single(model.Columns);
        Assert.False(model.Columns[0].IsExpression);
        Assert.Equal("product_id", model.Columns[0].ColumnName);
    }

    [Fact]
    public void SelectDistinct_SkipsModifierAndResolvesPlainColumns()
    {
        var model = ParseSql("select distinct product_id, product_name from products");

        Assert.Empty(model.ExpressionsMissingAlias);
        Assert.Equal(2, model.Columns.Count);
        Assert.False(model.Columns[0].IsExpression);
        Assert.Equal("product_id", model.Columns[0].ColumnName);
        Assert.False(model.Columns[1].IsExpression);
        Assert.Equal("product_name", model.Columns[1].ColumnName);
    }

    [Fact]
    public void SelectDistinct_QualifiedColumnWithAlias_ResolvesAsPlainColumn()
    {
        var model = ParseSql("select distinct a.actor_id as actor_id from actors a");

        Assert.Empty(model.ExpressionsMissingAlias);
        Assert.Single(model.Columns);
        Assert.False(model.Columns[0].IsExpression);
        Assert.Equal("a", model.Columns[0].TableAlias);
        Assert.Equal("actor_id", model.Columns[0].ColumnName);
        Assert.Equal("actor_id", model.Columns[0].OutputAlias);
    }

    [Fact]
    public void SelectDistinctTopN_SkipsBothModifiersAndResolvesPlainColumns()
    {
        // T-SQL allows DISTINCT before TOP: SELECT DISTINCT TOP 10 col FROM ...
        var model = ParseSql("select distinct top 10 product_id from products");

        Assert.Empty(model.ExpressionsMissingAlias);
        Assert.Single(model.Columns);
        Assert.False(model.Columns[0].IsExpression);
        Assert.Equal("product_id", model.Columns[0].ColumnName);
    }
}
