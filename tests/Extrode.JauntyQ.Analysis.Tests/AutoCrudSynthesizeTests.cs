using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

/// <summary>
/// Direct coverage for AutoCrud.Synthesize and its DescribeIdentifier/
/// DescribeUnusableTable helpers. Extrode.JauntyQ.Generator.Tests'
/// AutoCrudTests.cs exercises AutoCrud too, but only through the full
/// generator pipeline (CSharpGeneratorDriver/CodeEmitter) -- since it lives
/// in a different test project, none of that coverage is visible to a
/// Stryker run scoped to Extrode.JauntyQ.Analysis.Tests. These tests call
/// AutoCrud.Synthesize directly so the mutation-testing job can see it.
/// </summary>
[Trait("Category", "AuditRegression")]
public class AutoCrudSynthesizeTests
{
    private static ColumnSchema Col(string name, bool pk = false, bool identity = false, bool rowVersion = false, bool computed = false) =>
        new() { Name = name, DbType = "int", IsPrimaryKey = pk, IsIdentity = identity, IsRowVersion = rowVersion, IsComputed = computed };

    private static TableSchema Table(string name, params ColumnSchema[] cols)
    {
        var t = new TableSchema { Name = name };
        foreach (var c in cols)
            t.Columns[c.Name] = c;
        return t;
    }

    private static DatabaseSchema Schema(string dialect, params TableSchema[] tables)
    {
        var s = new DatabaseSchema { Dialect = dialect };
        foreach (var t in tables)
            s.Tables[t.Name] = t;
        return s;
    }

    [Fact]
    public void GetAll_AlwaysSynthesized_ColumnListJoinedWithCommaSpace()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true), Col("name"), Col("price")));

        var queries = AutoCrud.Synthesize(schema);

        var getAll = queries.Single(q => q.MethodName == "GetAll");
        Assert.Equal("widgets", getAll.TableName);
        Assert.Equal("SELECT id, name, price\nFROM widgets", getAll.Sql);
    }

    [Fact]
    public void ForeignKeyLoader_Synthesized_ForNonPkForeignKeyColumn()
    {
        var schema = Schema("sqlserver", Table("orders", Col("id", pk: true), Col("customer_id"), Col("note")));
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "customer_id", ToTable = "customers", ToColumn = "id" });

        var queries = AutoCrud.Synthesize(schema);

        var loader = queries.Single(q => q.MethodName == "GetByCustomerId");
        Assert.Equal("customer_id", loader.FilterColumn);
        Assert.Equal(
            "SELECT id, customer_id, note\nFROM orders\nWHERE orders.customer_id = @customer_id",
            loader.Sql);
    }

    [Fact]
    public void ForeignKeyLoader_Skipped_WhenColumnIsTheSolePrimaryKey()
    {
        // GetById already covers this case -- a redundant GetBy<Fk> loader
        // for the sole PK column would just duplicate it.
        var schema = Schema("sqlserver", Table("orders", Col("id", pk: true), Col("note")));
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "id", ToTable = "parents", ToColumn = "id" });

        var queries = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(queries, q => q.FilterColumn == "id");
    }

    [Fact]
    public void ForeignKeyLoader_Skipped_ForDuplicateColumn()
    {
        // Two FK rows on the same (table, column) pair (e.g. a composite FK
        // to two different parent tables sharing the lead column) must
        // synthesize only one GetBy<Fk> loader.
        var schema = Schema("sqlserver", Table("orders", Col("id", pk: true), Col("customer_id"), Col("note")));
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "customer_id", ToTable = "customers", ToColumn = "id" });
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "customer_id", ToTable = "customer_archive", ToColumn = "id" });

        var queries = AutoCrud.Synthesize(schema);

        Assert.Single(queries, q => q.MethodName == "GetByCustomerId");
    }

    [Fact]
    public void ForeignKeyLoader_Skipped_WhenFromTableDoesNotMatch()
    {
        var schema = Schema("sqlserver", Table("orders", Col("id", pk: true), Col("customer_id"), Col("note")));
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "invoices", FromColumn = "customer_id", ToTable = "customers", ToColumn = "id" });

        var queries = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(queries, q => q.MethodName.StartsWith("GetBy", StringComparison.Ordinal) && q.MethodName != "GetById");
    }

    [Fact]
    public void ForeignKeyLoader_Skipped_WhenColumnNotOnTable()
    {
        // A stale/inconsistent snapshot could list a FK column that no
        // longer exists on the table -- must not synthesize a loader that
        // references a nonexistent column.
        var schema = Schema("sqlserver", Table("orders", Col("id", pk: true), Col("note")));
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "customer_id", ToTable = "customers", ToColumn = "id" });

        var queries = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(queries, q => q.FilterColumn == "customer_id");
    }

    [Fact]
    public void View_OnlyGetAllAndFkLoaders_NoWrites()
    {
        var view = Table("active_orders", Col("id", pk: true), Col("note"));
        view.IsView = true;
        var schema = Schema("sqlserver", view);

        var queries = AutoCrud.Synthesize(schema);

        Assert.Equal(new[] { "GetAll" }, queries.ConvertAll(q => q.MethodName));
    }

    [Fact]
    public void HeapTable_NoPrimaryKey_OnlyGetAll_NoWrites()
    {
        var schema = Schema("sqlserver", Table("audit_log", Col("event"), Col("detail")));

        var queries = AutoCrud.Synthesize(schema);

        Assert.Equal(new[] { "GetAll" }, queries.ConvertAll(q => q.MethodName));
    }

    [Fact]
    public void GetById_CompositeKey_JoinsWhereWithAnd()
    {
        var schema = Schema("sqlserver", Table("order_items", Col("order_id", pk: true), Col("line_no", pk: true), Col("qty")));

        var queries = AutoCrud.Synthesize(schema);

        var byId = queries.Single(q => q.MethodName == "GetById");
        Assert.Equal(
            "-- @first\nSELECT order_id, line_no, qty\nFROM order_items\nWHERE order_items.order_id = @order_id AND order_items.line_no = @line_no",
            byId.Sql);
    }

    [Fact]
    public void Insert_Skipped_WhenNoInsertableColumns()
    {
        // Every column is either the identity PK or computed -- nothing left
        // to insert.
        var schema = Schema("sqlserver", Table("counters", Col("id", pk: true, identity: true), Col("computed_total", computed: true)));

        var queries = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(queries, q => q.MethodName == "Insert");
    }

    [Fact]
    public void Insert_ReturnsIdentity_OnlyWhenDialectKnownAndSingleIdentityColumn()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true, identity: true), Col("name")));

        var insert = AutoCrud.Synthesize(schema).Single(q => q.MethodName == "Insert");

        Assert.StartsWith("-- @identity\n", insert.Sql);
        Assert.Equal("-- @identity\nINSERT INTO widgets (name)\nVALUES (@name)", insert.Sql);
    }

    [Fact]
    public void Insert_NoIdentityPrefix_WhenDialectUnknown()
    {
        var schema = Schema("", Table("widgets", Col("id", pk: true, identity: true), Col("name")));

        var insert = AutoCrud.Synthesize(schema).Single(q => q.MethodName == "Insert");

        Assert.Equal("INSERT INTO widgets (name)\nVALUES (@name)", insert.Sql);
    }

    [Fact]
    public void Insert_NoIdentityPrefix_WhenNoIdentityColumn()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true), Col("name")));

        var insert = AutoCrud.Synthesize(schema).Single(q => q.MethodName == "Insert");

        Assert.Equal("INSERT INTO widgets (id, name)\nVALUES (@id, @name)", insert.Sql);
    }

    [Fact]
    public void Update_Skipped_WhenNoUpdatableColumns()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true)));

        var queries = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(queries, q => q.MethodName == "Update");
    }

    [Fact]
    public void Update_SetsNonKeyColumns_WhereFullPrimaryKeyPlusRowVersion()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true), Col("name"), Col("row_ver", rowVersion: true)));

        var update = AutoCrud.Synthesize(schema).Single(q => q.MethodName == "Update");

        Assert.Equal(
            "UPDATE widgets\nSET name = @name\nWHERE id = @id AND row_ver = @row_ver",
            update.Sql);
    }

    [Fact]
    public void Update_MultipleSetColumns_JoinedWithCommaSpace()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true), Col("name"), Col("price")));

        var update = AutoCrud.Synthesize(schema).Single(q => q.MethodName == "Update");

        Assert.Equal("UPDATE widgets\nSET name = @name, price = @price\nWHERE id = @id", update.Sql);
    }

    [Fact]
    public void Delete_AlwaysSynthesized_WhenTableHasPrimaryKey()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true), Col("row_ver", rowVersion: true)));

        var delete = AutoCrud.Synthesize(schema).Single(q => q.MethodName == "Delete");

        Assert.Equal("DELETE FROM widgets\nWHERE id = @id AND row_ver = @row_ver", delete.Sql);
    }

    [Fact]
    public void Upsert_Skipped_WhenNoUsableKey()
    {
        // PK is entirely identity (database-assigned) and there's no
        // secondary UNIQUE index to fall back on -- UpsertKeyResolver.Resolve
        // returns null, so there's no value to match an existing row on
        // before deciding insert-vs-update.
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true, identity: true), Col("name")));

        var queries = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(queries, q => q.MethodName == "Upsert");
    }

    [Fact]
    public void Upsert_Skipped_WhenNoNonKeyColumnsToSet()
    {
        // Only non-key column is itself a non-key identity -- EmitUpsert
        // would otherwise emit a dangling "set" clause.
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true), Col("seq", identity: true)));

        var queries = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(queries, q => q.MethodName == "Upsert");
    }

    [Fact]
    public void Upsert_Skipped_WhenDialectUnknown()
    {
        var schema = Schema("", Table("widgets", Col("id", pk: true), Col("name")));

        var queries = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(queries, q => q.MethodName == "Upsert");
    }

    [Fact]
    public void Upsert_Synthesized_WhenKeyBindableAndHasNonKeyColumnsAndDialectKnown()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true), Col("name")));

        var upsert = AutoCrud.Synthesize(schema).Single(q => q.MethodName == "Upsert");

        Assert.True(upsert.IsUpsert);
        Assert.Equal(string.Empty, upsert.Sql);
    }

    [Fact]
    public void Upsert_Skipped_WhenKeyContainsUnbindableComputedColumn()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true, computed: true), Col("id2", pk: true), Col("name")));

        var queries = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(queries, q => q.MethodName == "Upsert");
    }

    [Fact]
    public void DescribeUnusableTable_JauntyQReservedKeywordColumn_ReportedBeforeDialectCheck()
    {
        // "select" is one of JauntyQ's own ~55 tokenizer keywords, so this
        // must be refused even for a dialect (or no dialect at all) that
        // doesn't itself reserve the word.
        var table = Table("widgets", Col("id", pk: true), Col("select"));

        var why = AutoCrud.DescribeUnusableTable(table, null);

        Assert.Equal(
            "its column 'select' is a keyword JauntyQ's own SQL parser reserves, so the synthesized statement would not parse",
            why);
    }

    [Theory]
    [InlineData(SqlIdentifierPosition.Column, "as a column name")]
    [InlineData(SqlIdentifierPosition.Object, "as an object name")]
    public void DescribeIdentifier_ReservedWordMessage_MatchesPosition(SqlIdentifierPosition position, string expectedPhrase)
    {
        // "user" is reserved by SQL Server in both column and object
        // position, but is NOT one of JauntyQ's own ~55 tokenizer keywords
        // (unlike "group"), so it reaches the dialect-reserved-word check
        // this test targets rather than short-circuiting on the JauntyQ-
        // keyword check first. Exercises DescribeUnusableTable's column path
        // (which always calls with Column) and confirms the wording differs
        // by position, since the position argument is what Synthesize's own
        // Object-position table-name check relies on.
        string? why = position == SqlIdentifierPosition.Column
            ? Extrode.JauntyQ.Analysis.AutoCrud.DescribeUnusableTable(
                new TableSchema { Name = "t", Columns = { ["user"] = Col("user") } }, "sqlserver")
            : Extrode.JauntyQ.Analysis.AutoCrud.DescribeUnusableTable(
                new TableSchema { Name = "user", Columns = { ["id"] = Col("id", pk: true) } }, "sqlserver");

        Assert.NotNull(why);
        Assert.Contains(expectedPhrase, why);
    }

    [Fact]
    public void DescribeUnusableTable_Null_WhenTableIsUsable()
    {
        var table = Table("widgets", Col("id", pk: true), Col("name"));

        Assert.Null(AutoCrud.DescribeUnusableTable(table, "sqlserver"));
    }

    [Fact]
    public void DescribeUnusableTable_EmptyTableName()
    {
        var table = Table("", Col("id", pk: true));

        var why = AutoCrud.DescribeUnusableTable(table, "sqlserver");

        Assert.Equal("its name is empty", why);
    }

    [Fact]
    public void DescribeUnusableTable_NameStartsWithDigit()
    {
        var table = Table("1widgets", Col("id", pk: true));

        var why = AutoCrud.DescribeUnusableTable(table, "sqlserver");

        Assert.Contains("does not begin with a letter or underscore", why);
    }

    [Fact]
    public void DescribeUnusableTable_NameContainsInvalidCharacter()
    {
        // Second character invalid -- specifically exercises the loop's
        // i=1 boundary (a single-character name never enters this loop at
        // all, so a 2-character name is the minimal case that does).
        var table = Table("a$", Col("id", pk: true));

        var why = AutoCrud.DescribeUnusableTable(table, "sqlserver");

        Assert.Contains("contains '$'", why);
    }

    [Fact]
    public void DescribeUnusableTable_NoColumns()
    {
        var table = Table("widgets");

        var why = AutoCrud.DescribeUnusableTable(table, "sqlserver");

        Assert.Equal("has no columns, so there is nothing to select, insert or update", why);
    }

    [Fact]
    public void DescribeUnusableTable_ColumnRequiresQuotingForCase()
    {
        // Postgres-only: an uppercase-containing identifier is unsafe to
        // emit bare because unquoted references fold to lower case.
        var table = Table("widgets", Col("id", pk: true), Col("OrderId"));

        var why = AutoCrud.DescribeUnusableTable(table, "postgres");

        Assert.Contains("PostgreSQL folds an unquoted reference to lower case", why);
    }

    [Fact]
    public void Synthesize_SkipsTable_WhenNoColumnsAtAll()
    {
        var schema = Schema("sqlserver", Table("widgets"));

        var queries = AutoCrud.Synthesize(schema);

        Assert.Empty(queries);
    }

    [Fact]
    public void Synthesize_SkipsTable_WhenAnyColumnIsUnusable()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true), Col("select")));

        var queries = AutoCrud.Synthesize(schema);

        Assert.Empty(queries);
    }

    [Fact]
    public void Synthesize_SkipsEntireTable_WhenColumnCollidesAfterPascalCase()
    {
        var schema = Schema("sqlserver", Table("widgets", Col("id", pk: true), Col("order_number"), Col("OrderNumber")));

        var queries = AutoCrud.Synthesize(schema);

        Assert.Empty(queries);
    }
}
