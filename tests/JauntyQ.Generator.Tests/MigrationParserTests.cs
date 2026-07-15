using JauntyQ.Analysis;
using JauntyQ.Analysis.Migrations;
using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class MigrationParserTests
{
    [Fact]
    public void CreateTable_ColumnsFacetsFlags()
    {
        var statements = MigrationParser.Parse(@"
create table gadgets (
    gadget_id int not null primary key identity(1,1),
    name nvarchar(40) not null,
    price decimal(10,2) null,
    notes nvarchar(max),
    row_version rowversion not null
)");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.CreateTable, stmt.Kind);
        Assert.Equal("gadgets", stmt.TableName);
        Assert.Equal(5, stmt.Columns.Count);

        var id = stmt.Columns[0];
        Assert.Equal("gadget_id", id.Name);
        Assert.True(id.IsPrimaryKey);
        Assert.True(id.IsIdentity);
        Assert.False(id.IsNullable);

        var name = stmt.Columns[1];
        Assert.Equal(40, name.MaxLength);
        Assert.True(name.IsUnicode);
        Assert.False(name.IsNullable);

        var price = stmt.Columns[2];
        Assert.Equal(10, price.Precision);
        Assert.Equal(2, price.Scale);
        Assert.True(price.IsNullable);

        var notes = stmt.Columns[3];
        Assert.Equal(-1, notes.MaxLength);
    }

    [Fact]
    public void CreateTable_TableLevelPrimaryKey_MarksColumns()
    {
        var statements = MigrationParser.Parse(@"
create table order_items (
    order_id int not null,
    item_no int not null,
    qty int not null,
    primary key (order_id, item_no)
)");

        var stmt = Assert.Single(statements);
        Assert.True(stmt.Columns.Single(c => c.Name == "order_id").IsPrimaryKey);
        Assert.True(stmt.Columns.Single(c => c.Name == "item_no").IsPrimaryKey);
        Assert.False(stmt.Columns.Single(c => c.Name == "qty").IsPrimaryKey);
    }

    [Fact]
    public void DropTable_IfExists()
    {
        var statements = MigrationParser.Parse("drop table if exists gadgets");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.DropTable, stmt.Kind);
        Assert.Equal("gadgets", stmt.TableName);
        Assert.True(stmt.IfExists);
    }

    [Fact]
    public void AlterAdd_MultipleColumns()
    {
        var statements = MigrationParser.Parse("alter table products add discontinued_at datetime null, reorder_note nvarchar(100)");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.AddColumn, stmt.Kind);
        Assert.Equal(2, stmt.Columns.Count);
        Assert.Equal("discontinued_at", stmt.Columns[0].Name);
        Assert.Equal(100, stmt.Columns[1].MaxLength);
    }

    [Fact]
    public void AlterDropColumn()
    {
        var statements = MigrationParser.Parse("alter table products drop column reorder_level");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.DropColumn, stmt.Kind);
        Assert.Equal("reorder_level", Assert.Single(stmt.ColumnNames));
    }

    [Theory]
    [InlineData("alter table products alter column product_name nvarchar(10) not null")] // sqlserver
    [InlineData("alter table products modify column product_name nvarchar(10) not null")] // mysql
    [InlineData("alter table products alter column product_name type nvarchar(10)")]      // postgres
    public void AlterColumn_AllDialectForms(string sql)
    {
        var statements = MigrationParser.Parse(sql);

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.AlterColumn, stmt.Kind);
        var col = Assert.Single(stmt.Columns);
        Assert.Equal("product_name", col.Name);
        Assert.Equal(10, col.MaxLength);
    }

    [Theory]
    [InlineData("alter table products add constraint pk_products primary key (product_id)")]
    [InlineData("alter table products add primary key (product_id)")]
    public void AddConstraintPrimaryKey_ParsesAsAddPrimaryKey(string sql)
    {
        var statements = MigrationParser.Parse(sql);

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.AddPrimaryKey, stmt.Kind);
        Assert.Equal("products", stmt.TableName);
        Assert.Equal(new[] { "product_id" }, stmt.ColumnNames);
    }

    [Fact]
    public void AddConstraintPrimaryKey_CompositeKey_CapturesAllColumns()
    {
        var statements = MigrationParser.Parse(
            "alter table order_items add constraint pk_order_items primary key (order_id, line_no)");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.AddPrimaryKey, stmt.Kind);
        Assert.Equal(new[] { "order_id", "line_no" }, stmt.ColumnNames);
    }

    [Theory]
    [InlineData("alter table products add constraint uq_products_name unique (product_name)")]
    [InlineData("alter table products add constraint fk_products_category foreign key (category_id) references categories (category_id)")]
    [InlineData("alter table products add constraint chk_products_price check (unit_price > 0)")]
    [InlineData("alter table products drop constraint pk_products")]
    public void AddOrDropOtherConstraints_Unsupported_NotSilentlyIgnored(string sql)
    {
        // These aren't modeled (no secondary-index/FK population from
        // migrations), so they must surface as JNT9001 (Unsupported) rather
        // than vanish as Ignored — the effective schema silently missing a
        // uniqueness/FK/check constraint with no diagnostic at all is worse
        // than a warning that says "not simulated".
        var statements = MigrationParser.Parse(sql);

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.Unsupported, stmt.Kind);
    }

    [Fact]
    public void IndexStatements_Ignored_RenameUnsupported()
    {
        var statements = MigrationParser.Parse(@"
create index ix_products_name on products (product_name);
exec sp_rename 'products.old_name', 'new_name', 'COLUMN';
");

        Assert.Equal(2, statements.Count);
        Assert.Equal(MigrationStatementKind.Ignored, statements[0].Kind);
        Assert.Equal(MigrationStatementKind.Unsupported, statements[1].Kind);
    }

    [Fact]
    public void GoAndSemicolons_SplitStatements()
    {
        var statements = MigrationParser.Parse(@"
create table a (x int not null primary key)
GO
create table b (y int not null primary key);
drop table if exists c
");

        Assert.Equal(3, statements.Count);
        Assert.Equal("a", statements[0].TableName);
        Assert.Equal("b", statements[1].TableName);
        Assert.Equal("c", statements[2].TableName);
    }

    [Fact]
    public void SchemaQualifiedAndBracketedNames_Stripped()
    {
        var statements = MigrationParser.Parse("create table [dbo].[Gadgets] (id int not null primary key)");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.CreateTable, stmt.Kind);
        Assert.Equal("Gadgets", stmt.TableName);
    }

    [Fact]
    public void DefaultExpressions_Skipped()
    {
        var statements = MigrationParser.Parse("alter table products add created_at datetime not null default (getdate())");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.AddColumn, stmt.Kind);
        var col = Assert.Single(stmt.Columns);
        Assert.Equal("created_at", col.Name);
        Assert.False(col.IsNullable);
    }
}

public class SchemaSimulatorTests
{
    private static DatabaseSchema BaseSchema()
    {
        var schema = new DatabaseSchema { Dialect = "sqlserver" };
        schema.Tables["products"] = new TableSchema
        {
            Name = "products",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["product_id"] = new() { Name = "product_id", DbType = "int", IsPrimaryKey = true, IsIdentity = true },
                ["product_name"] = new() { Name = "product_name", DbType = "nvarchar", MaxLength = 40, IsUnicode = true },
                ["unit_price"] = new() { Name = "unit_price", DbType = "decimal", Precision = 10, Scale = 2 }
            }
        };
        return schema;
    }

    private static (DatabaseSchema Schema, List<AnalysisDiagnostic> Errors) Apply(DatabaseSchema snapshot, string sql, string file = "0001_test.sql")
    {
        var errors = new List<AnalysisDiagnostic>();
        var effective = SchemaSimulator.Apply(snapshot,
            new[] { (file, MigrationParser.Parse(sql)) }, errors);
        return (effective, errors);
    }

    [Fact]
    public void CreateTable_AppearsInEffectiveSchema_RowVersionFlagged()
    {
        var (schema, errors) = Apply(BaseSchema(),
            "create table gadgets (gadget_id int not null primary key identity, name nvarchar(20) not null, row_version rowversion not null)");

        Assert.Empty(errors);
        var gadgets = schema.Tables["gadgets"];
        Assert.True(gadgets.Columns["row_version"].IsRowVersion);
        Assert.True(gadgets.Columns["gadget_id"].IsIdentity);
    }

    [Fact]
    public void Timestamp_FlaggedAsRowVersion_OnlyOnSqlServer()
    {
        var pg = BaseSchema();
        pg.Dialect = "postgres";
        var (schema, _) = Apply(pg, "create table logs (id int not null primary key, at timestamp not null)");

        Assert.False(schema.Tables["logs"].Columns["at"].IsRowVersion);

        var (schema2, _) = Apply(BaseSchema(), "create table logs (id int not null primary key, at timestamp not null)");
        Assert.True(schema2.Tables["logs"].Columns["at"].IsRowVersion);
    }

    [Fact]
    public void DropColumn_PreservesRemainingOrder_EvenAfterLaterAdd()
    {
        var (schema, errors) = Apply(BaseSchema(), @"
alter table products drop column product_name;
alter table products add supplier_note nvarchar(50) null;
");

        Assert.Empty(errors);
        var names = schema.Tables["products"].Columns.Values.Select(c => c.Name).ToList();
        Assert.Equal(new[] { "product_id", "unit_price", "supplier_note" }, names);
    }

    [Fact]
    public void AlterColumn_ChangesFacets_KeepsKeyAndIdentity()
    {
        var (schema, errors) = Apply(BaseSchema(), "alter table products alter column product_name nvarchar(10) not null");

        Assert.Empty(errors);
        var col = schema.Tables["products"].Columns["product_name"];
        Assert.Equal(10, col.MaxLength);
        Assert.False(col.IsNullable);
        Assert.False(col.IsPrimaryKey);

        var (schema2, _) = Apply(BaseSchema(), "alter table products alter column product_id bigint not null");
        var id = schema2.Tables["products"].Columns["product_id"];
        Assert.Equal("bigint", id.DbType);
        Assert.True(id.IsPrimaryKey);   // key status survives the type change
        Assert.True(id.IsIdentity);
    }

    [Fact]
    public void AddConstraintPrimaryKey_MarksColumnPrimaryKeyAndNonNullable()
    {
        // The common create-then-constrain pattern: a table created without
        // an inline PK, keyed by a later migration.
        var (schema, errors) = Apply(BaseSchema(),
            "alter table products add constraint pk_products_name primary key (product_name)");

        Assert.Empty(errors);
        var col = schema.Tables["products"].Columns["product_name"];
        Assert.True(col.IsPrimaryKey);
        Assert.False(col.IsNullable);
        // Unrelated facets (type, length, unicode) are untouched — only key
        // status changes, unlike AlterColumn which redefines the column.
        Assert.Equal("nvarchar", col.DbType);
        Assert.Equal(40, col.MaxLength);
    }

    [Fact]
    public void AddConstraintPrimaryKey_CompositeKey_MarksBothColumns()
    {
        var (schema, errors) = Apply(BaseSchema(),
            "alter table products add constraint pk_products_composite primary key (product_name, unit_price)");

        Assert.Empty(errors);
        Assert.True(schema.Tables["products"].Columns["product_name"].IsPrimaryKey);
        Assert.True(schema.Tables["products"].Columns["unit_price"].IsPrimaryKey);
    }

    [Fact]
    public void AddConstraintPrimaryKey_MissingTableOrColumn_JNT9002()
    {
        var (_, missingTable) = Apply(BaseSchema(),
            "alter table missing_table add constraint pk_x primary key (id)");
        var tableError = Assert.Single(missingTable);
        Assert.Equal("JNT9002", tableError.Code);

        var (_, missingColumn) = Apply(BaseSchema(),
            "alter table products add constraint pk_x primary key (no_such_column)");
        var columnError = Assert.Single(missingColumn);
        Assert.Equal("JNT9002", columnError.Code);
    }

    [Fact]
    public void AddOtherConstraint_JNT9001Warning_NotSilentlyDropped()
    {
        var (_, errors) = Apply(BaseSchema(),
            "alter table products add constraint uq_products_name unique (product_name)");

        var error = Assert.Single(errors);
        Assert.Equal("JNT9001", error.Code);
        Assert.Equal(AnalysisSeverity.Warning, error.Severity);
    }

    [Fact]
    public void InvalidOperations_JNT9002_SimulationContinues()
    {
        var (schema, errors) = Apply(BaseSchema(), @"
create table products (x int not null primary key);
drop table missing_table;
alter table products drop column no_such_column;
alter table products add supplier_note nvarchar(50) null;
");

        Assert.Equal(3, errors.Count(e => e.Code == "JNT9002"));
        // best-effort: the valid trailing statement still applied
        Assert.True(schema.Tables["products"].Columns.ContainsKey("supplier_note"));
    }

    [Fact]
    public void DropTableIfExists_MissingTable_NoError()
    {
        var (_, errors) = Apply(BaseSchema(), "drop table if exists missing_table");
        Assert.Empty(errors);
    }

    [Fact]
    public void UnsupportedStatement_JNT9001Warning()
    {
        var (_, errors) = Apply(BaseSchema(), "exec sp_rename 'products.a', 'b', 'COLUMN'");

        var error = Assert.Single(errors);
        Assert.Equal("JNT9001", error.Code);
        Assert.Equal(AnalysisSeverity.Warning, error.Severity);
    }

    [Fact]
    public void Snapshot_IsNeverMutated()
    {
        var snapshot = BaseSchema();
        Apply(snapshot, "alter table products drop column product_name; drop table products;");

        Assert.True(snapshot.Tables.ContainsKey("products"));
        Assert.True(snapshot.Tables["products"].Columns.ContainsKey("product_name"));
    }
}
