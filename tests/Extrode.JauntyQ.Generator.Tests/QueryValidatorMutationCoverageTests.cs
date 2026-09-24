using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class QueryValidatorMutationCoverageTests
{
    private static DatabaseSchema Schema(string dialect = "")
    {
        return new DatabaseSchema
        {
            Dialect = dialect,
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
                },
                ["tags"] = new TableSchema
                {
                    Name = "tags",
                    Columns = new Dictionary<string, ColumnSchema>
                    {
                        ["tag_id"] = new ColumnSchema { Name = "tag_id", DbType = "int", IsNullable = false }
                    }
                },
                ["product_view"] = new TableSchema
                {
                    Name = "product_view",
                    IsView = true,
                    Columns = new Dictionary<string, ColumnSchema>
                    {
                        ["product_name"] = new ColumnSchema { Name = "product_name", DbType = "varchar", IsNullable = false }
                    }
                }
            }
        };
    }

    private static QueryModel Parse(string sql)
        => SqlParser.SqlParser.Parse(SqlTokenizer.Tokenize(sql), "TestQuery");

    private static List<ValidationError> Validate(string sql, string dialect = "")
        => QueryValidator.Validate(Parse(sql), Schema(dialect));

    private static string MessageOf(List<ValidationError> errors, string code)
        => Assert.Single(errors, e => e.Code == code).Message;

    [Fact]
    public void NullSchema_ReportsTheExactPullHint()
    {
        var errors = QueryValidator.Validate(Parse("select product_id from products"), null);

        var error = Assert.Single(errors);
        Assert.Equal("JNT6001", error.Code);
        Assert.Equal("Schema snapshot not found. Run 'jaunty schema pull'", error.Message);
    }

    [Fact]
    public void UnknownDialect_ReportsTheExactRecognizedList()
    {
        var errors = Validate("select product_id from products", "oracle");

        var error = Assert.Single(errors);
        Assert.Equal("JNT7003", error.Code);
        Assert.Equal(
            "Unknown dialect 'oracle' in the schema snapshot. Recognized dialects: sqlserver, postgres, sqlite, mysql. " +
            "MariaDB is wire/SQL-compatible with MySQL for everything the generator emits, so a MariaDB snapshot should also declare \"mysql\".",
            error.Message);
    }

    [Fact]
    public void EmptySelect_StopsAfterTheEmptyQueryError()
    {
        var query = new QueryModel { StatementType = StatementType.Select };
        query.Tables.Add(new TableRef { TableName = "missing_table" });

        var errors = QueryValidator.Validate(query, Schema());

        var error = Assert.Single(errors);
        Assert.Equal("JNT3001", error.Code);
        Assert.Equal("SQL file is empty or contains no SELECT columns", error.Message);
    }

    [Fact]
    public void ExpressionWithoutAlias_ReportsTheExactMessage()
    {
        var query = Parse("select count(*) from products");
        string missing = Assert.Single(query.ExpressionsMissingAlias);

        var errors = QueryValidator.Validate(query, Schema());

        Assert.Equal(
            $"Expression projection '{missing}' requires an explicit alias: add 'AS <name>' so the generated result property has a stable name.",
            MessageOf(errors, "JNT3004"));
    }

    [Fact]
    public void CteWithoutColumns_UsedAsFromSource_IsReported()
    {
        var query = new QueryModel { StatementType = StatementType.Select };
        query.Ctes.Add(new CteRef { Name = "c", Body = new QueryModel { StatementType = StatementType.Delete, TargetTable = "products" } });
        query.Ctes[0].Body.Tables.Add(new TableRef { TableName = "products" });
        query.Tables.Add(new TableRef { TableName = "c" });
        query.Columns.Add(new ColumnRef { ColumnName = "x" });

        var errors = QueryValidator.Validate(query, Schema());

        Assert.Equal(
            "CTE 'c' returns no columns (its body has no RETURNING or SELECT projection); it cannot be used as a FROM source.",
            MessageOf(errors, "JNT2001"));
    }

    [Fact]
    public void UpdateOfAView_ReportsTheExactMessage()
    {
        var errors = Validate("update product_view set product_name = 'x'");

        Assert.Equal(
            "UPDATE targets 'product_view', which is a view. Views are captured read-only, so no write is generated for one. " +
            "Write to the underlying table instead, or remove the query.",
            MessageOf(errors, "JNT2021"));
    }

    [Fact]
    public void SelectCarryingAViewTarget_IsNotAViewWrite()
    {
        var query = new QueryModel { StatementType = StatementType.Select, TargetTable = "product_view" };
        query.Tables.Add(new TableRef { TableName = "product_view" });
        query.Columns.Add(new ColumnRef { ColumnName = "product_name" });

        var errors = QueryValidator.Validate(query, Schema());

        Assert.Empty(errors);
    }

    [Fact]
    public void QualifiedColumnMissingFromCte_NamesTheCte()
    {
        var errors = Validate("with c as (select product_id from products) select c.product_name from c");

        Assert.Equal("Column 'product_name' does not exist in CTE 'c'", MessageOf(errors, "JNT2002"));
    }

    [Fact]
    public void UnknownAlias_NamesTheAlias()
    {
        var errors = Validate("select x.product_id from products p");

        Assert.Equal("Unknown table alias 'x'", MessageOf(errors, "JNT2002"));
    }

    [Fact]
    public void AmbiguousColumn_ListsTablesCommaSeparated()
    {
        var errors = Validate("select category_id from products p join categories c on p.category_id = c.category_id");

        Assert.Equal(
            "Ambiguous column reference 'category_id' found in tables: products, categories",
            MessageOf(errors, "JNT2003"));
    }

    [Fact]
    public void MissingRightJoinColumn_IsReported()
    {
        var errors = Validate("select p.product_id from products p join categories c on p.category_id = c.nope");

        Assert.Equal("Column 'nope' does not exist in table 'categories'", MessageOf(errors, "JNT2002"));
    }

    [Fact]
    public void JoinSideOnCteShadowingARealTable_IsNotCheckedAgainstTheTable()
    {
        var errors = Validate(
            "with products as (select category_id from categories) " +
            "select p.category_id from products p join categories c on p.nope = c.category_id");

        Assert.DoesNotContain(errors, e => e.Message.Contains("nope"));
    }

    [Fact]
    public void InSubqueryWithTwoColumns_ReportsTheCount()
    {
        var errors = Validate(
            "select product_id from products where category_id in (select category_id, category_name from categories)");

        Assert.Equal(
            "An IN-subquery must project exactly one column, but it projects 2. Reduce the subquery's SELECT list to a single column.",
            MessageOf(errors, "JNT3007"));
    }

    [Fact]
    public void InSubqueryUnqualifiedStar_CountsEveryColumn()
    {
        var errors = Validate("select product_id from products where category_id in (select * from products)");

        Assert.Equal(
            "An IN-subquery must project exactly one column, but it projects 3. Reduce the subquery's SELECT list to a single column.",
            MessageOf(errors, "JNT3007"));
    }

    [Fact]
    public void InSubqueryTableNameQualifiedStar_ResolvesAnUnaliasedTable()
    {
        var errors = Validate("select product_id from products where category_id in (select products.* from products)");

        Assert.Equal(
            "An IN-subquery must project exactly one column, but it projects 3. Reduce the subquery's SELECT list to a single column.",
            MessageOf(errors, "JNT3007"));
    }

    [Fact]
    public void InSubqueryAliasQualifiedStarOverOneColumn_IsAccepted()
    {
        var errors = Validate("select product_id from products where product_id in (select t.* from tags t)");

        Assert.DoesNotContain(errors, e => e.Code == "JNT3007");
    }

    [Fact]
    public void InSubqueryStarOverCte_CountsTheCteColumns()
    {
        var errors = Validate(
            "with c as (select product_id, category_id from products) " +
            "select product_id from products where product_id in (select * from c)");

        Assert.Equal(
            "An IN-subquery must project exactly one column, but it projects 2. Reduce the subquery's SELECT list to a single column.",
            MessageOf(errors, "JNT3007"));
    }

    public static TheoryData<string, string, string> UnsupportedConstructMessages => new()
    {
        { "UNION", "JNT1006",
            "Set operations (UNION, UNION ALL, INTERSECT, EXCEPT) are not supported: the generator only models the first branch's " +
            "column shape, so a second branch with different nullability would silently generate code that reads NULL as " +
            "non-nullable and throws at runtime. Split into separate queries, or model the combined result as an application-level merge." },
        { "SUBQUERY", "JNT1007",
            "This subquery form is not supported: only WHERE-clause '[NOT] IN (SELECT ...)' and '[NOT] EXISTS (SELECT ...)' " +
            "predicates are modeled as their own scope. A scalar subquery in the projection list or a derived table in FROM " +
            "falls through to the enclosing statement's ordinary parsing, which can misread the outer query's shape. " +
            "Rewrite using a JOIN, a WHERE-clause IN/EXISTS predicate, or a CTE." },
        { "LATERAL", "JNT1009",
            "LATERAL joins are not supported: the parser has no grammar for them, and nothing about your schema is wrong. " +
            "Rewrite as a plain JOIN, a WHERE-clause IN/EXISTS predicate, or a GROUP BY with aggregates (which is supported, " +
            "including over a join). See docs/06-reference/supported-sql.md for the full accepted surface." },
        { "DISTINCT ON", "JNT1009",
            "PostgreSQL's 'DISTINCT ON (...)' is not supported: the parser has no grammar for it, and nothing about your schema " +
            "is wrong. Its expression list would otherwise be read as part of your first selected column. Rewrite as a window " +
            "function -- 'row_number() over (partition by <the DISTINCT ON columns> order by <your ORDER BY>)' in a CTE, " +
            "filtered to 1 in the outer query -- or select the distinct keys and their rows in two queries. " +
            "See docs/06-reference/supported-sql.md for the accepted surface." },
        { "APPLY", "JNT1009",
            "CROSS APPLY / OUTER APPLY is not supported: the parser has no grammar for it, and nothing about your schema is wrong. " +
            "It is SQL Server's spelling of a LATERAL join. Rewrite as a plain JOIN, a WHERE-clause IN/EXISTS predicate, or a " +
            "GROUP BY with aggregates (which is supported, including over a join). See docs/06-reference/supported-sql.md for " +
            "the full accepted surface." },
        { "SELECT INTO", "JNT1009",
            "'SELECT ... INTO <table>' is not supported: it creates a table rather than returning rows, so there is no result " +
            "shape for JauntyQ to generate a mapper from, and nothing about your schema is wrong. Move the table creation into " +
            "a migration under your migrations directory, and keep the SELECT here as a plain query -- or, if you meant " +
            "PL/pgSQL's 'SELECT ... INTO <variable>', that belongs in a stored procedure rather than a query file. " +
            "See docs/06-reference/supported-sql.md for the accepted surface." },
        { "MULTI_STATEMENT", "JNT1008",
            "This file contains more than one SQL statement. JauntyQ generates one method per file from one statement: a second " +
            "statement's tables and columns are merged into the first statement's model (SELECT) or dropped entirely " +
            "(INSERT/UPDATE/DELETE), so the generated code would not match either statement. Split each statement into its own .sql file." },
        { "PIVOT", "JNT1001", "Unsupported SQL construct: PIVOT" },
    };

    [Theory]
    [MemberData(nameof(UnsupportedConstructMessages))]
    public void UnsupportedConstruct_ReportsExactlyOneExactDiagnostic(string construct, string code, string message)
    {
        var query = new QueryModel { WithRecursive = true };
        query.UnsupportedConstructs.Add(construct);

        var errors = QueryValidator.Validate(query, Schema());

        var error = Assert.Single(errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void TwoWritableCtes_UnderSqlServer_ReportOnce()
    {
        var query = new QueryModel { StatementType = StatementType.Select };
        foreach (var name in new[] { "a", "b" })
        {
            var body = new QueryModel { StatementType = StatementType.Delete, TargetTable = "tags" };
            body.Tables.Add(new TableRef { TableName = "tags" });
            var cte = new CteRef { Name = name, Body = body };
            cte.VirtualColumns.Add("tag_id");
            query.Ctes.Add(cte);
        }
        query.Tables.Add(new TableRef { TableName = "a" });
        query.Columns.Add(new ColumnRef { ColumnName = "tag_id" });

        var errors = QueryValidator.Validate(query, Schema("sqlserver"));

        Assert.Equal(
            "Data-modifying CTEs (WITH ... INSERT/UPDATE/DELETE) are not supported under the sqlserver dialect; the SQL is emitted verbatim with no rewrite.",
            MessageOf(errors, "JNT7002"));
    }

    private static QueryModel Insert(bool hasReturning, bool returningColumn)
    {
        var query = new QueryModel { StatementType = StatementType.Insert, TargetTable = "tags", HasReturning = hasReturning };
        query.Tables.Add(new TableRef { TableName = "tags" });
        if (returningColumn)
            query.Returning.Add(new ColumnRef { ColumnName = "tag_id" });
        return query;
    }

    [Fact]
    public void ReturningFlagWithoutColumns_UnderSqlServer_AdvisesOutput()
    {
        var errors = QueryValidator.Validate(Insert(hasReturning: true, returningColumn: false), Schema("sqlserver"));

        Assert.Equal(
            "RETURNING is not supported under the sqlserver dialect (use OUTPUT); the SQL is emitted verbatim so it cannot be rewritten.",
            MessageOf(errors, "JNT7002"));
    }

    [Fact]
    public void ReturningColumnsWithoutFlag_UnderMySql_AdvisesLastInsertId()
    {
        var errors = QueryValidator.Validate(Insert(hasReturning: false, returningColumn: true), Schema("mysql"));

        Assert.Equal(
            "RETURNING is not supported under the mysql dialect (use LAST_INSERT_ID()/a follow-up SELECT, or MariaDB 10.5+/13.0 " +
            "if RETURNING is actually available on your server); the SQL is emitted verbatim so it cannot be rewritten.",
            MessageOf(errors, "JNT7002"));
    }

    [Fact]
    public void SelectStarFromMissingTable_SaysNoColumnsResolved()
    {
        var errors = Validate("select * from missing_table");

        Assert.Equal(
            "SELECT * is not allowed: the projection must be explicit so the generated result type is deterministic and " +
            "schema drift fails this build instead of failing at runtime. Replace * with: <no columns resolved>",
            MessageOf(errors, "JNT3002"));
    }

    [Fact]
    public void SelectStarOverAJoin_QualifiesAnUnaliasedTableByName()
    {
        var errors = Validate("select * from products join categories c on products.category_id = c.category_id");

        Assert.Equal(
            "SELECT * is not allowed: the projection must be explicit so the generated result type is deterministic and " +
            "schema drift fails this build instead of failing at runtime. Replace * with: " +
            "products.product_id, products.product_name, products.category_id, c.category_id, c.category_name",
            MessageOf(errors, "JNT3002"));
    }
}
