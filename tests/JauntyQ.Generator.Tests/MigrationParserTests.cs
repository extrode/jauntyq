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

    [Theory]
    [InlineData("serial")]
    [InlineData("bigserial")]
    [InlineData("smallserial")]
    public void CreateTable_SerialColumn_RecognizedAsIdentity(string serialType)
    {
        // Postgres SERIAL/BIGSERIAL/SMALLSERIAL are sugar for an integer
        // column with a nextval() sequence default -- the type name IS the
        // identity signal, unlike SQL Server's "int IDENTITY" (a separate
        // trailing keyword after the type). ParseColumnDef's flag-scanning
        // loop already checked for a literal "SERIAL" token, but that can
        // never match here: "serial" is consumed as def[1] (the column's
        // DbType itself) at position 1, never revisited by the pos=2+ flag
        // loop. The live PostgresExtractor detects this correctly (its
        // is_identity check matches column_default LIKE 'nextval(%'), so a
        // hand-authored migration disagreed with a live pull of the same
        // schema -- AutoCrud's synthesized Insert would then wrongly include
        // the serial column instead of leaving it database-assigned.
        var statements = MigrationParser.Parse($"create table widgets (id {serialType} primary key, name varchar(40) not null)");

        var stmt = Assert.Single(statements);
        var id = stmt.Columns.Single(c => c.Name == "id");
        Assert.Equal(serialType, id.DbType);
        Assert.True(id.IsIdentity);
        Assert.True(id.IsPrimaryKey);
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

    [Fact]
    public void AlterDropColumn_MultipleNames_CommaSeparated()
    {
        var statements = MigrationParser.Parse("alter table products drop column reorder_level, discontinued");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.DropColumn, stmt.Kind);
        Assert.Equal(new[] { "reorder_level", "discontinued" }, stmt.ColumnNames);
    }

    [Fact]
    public void AlterTable_MixedAddDropAdd_SplitsIntoThreeActions()
    {
        // ADD/DROP/COLUMN aren't tokenizer keywords (they tokenize as plain
        // identifiers), so a second action clause used to satisfy
        // ParseColumnDef's "identifier identifier" shape and get misparsed
        // as a phantom column (e.g. "DROP COLUMN b" read as a column named
        // "DROP" of type "column") instead of its own DropColumn action.
        var statements = MigrationParser.Parse(
            "alter table products add discontinued_at datetime null, drop column reorder_level, add note nvarchar(50)");

        Assert.Equal(3, statements.Count);

        Assert.Equal(MigrationStatementKind.AddColumn, statements[0].Kind);
        var added1 = Assert.Single(statements[0].Columns);
        Assert.Equal("discontinued_at", added1.Name);
        Assert.Equal("datetime", added1.DbType);

        Assert.Equal(MigrationStatementKind.DropColumn, statements[1].Kind);
        Assert.Equal("reorder_level", Assert.Single(statements[1].ColumnNames));

        Assert.Equal(MigrationStatementKind.AddColumn, statements[2].Kind);
        var added2 = Assert.Single(statements[2].Columns);
        Assert.Equal("note", added2.Name);
        Assert.Equal(50, added2.MaxLength);

        foreach (var stmt in statements)
            Assert.Equal("products", stmt.TableName);
    }

    [Fact]
    public void AlterTable_RepeatedAddColumnClauses_EachOwnAction()
    {
        var statements = MigrationParser.Parse(
            "alter table products add column a int, add column b varchar(10)");

        Assert.Equal(2, statements.Count);
        Assert.Equal(MigrationStatementKind.AddColumn, statements[0].Kind);
        Assert.Equal("a", Assert.Single(statements[0].Columns).Name);
        Assert.Equal(MigrationStatementKind.AddColumn, statements[1].Kind);
        Assert.Equal("b", Assert.Single(statements[1].Columns).Name);
    }

    [Fact]
    public void AlterTable_MultipleAlterColumnClauses_EachOwnAction()
    {
        var statements = MigrationParser.Parse(
            "alter table products alter column product_name nvarchar(20) not null, alter column unit_price decimal(12,4) null");

        Assert.Equal(2, statements.Count);
        Assert.Equal(MigrationStatementKind.AlterColumn, statements[0].Kind);
        Assert.Equal("product_name", Assert.Single(statements[0].Columns).Name);
        Assert.Equal(MigrationStatementKind.AlterColumn, statements[1].Kind);
        Assert.Equal("unit_price", Assert.Single(statements[1].Columns).Name);
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
    [InlineData("alter table products alter column product_name drop not null", true)]
    [InlineData("alter table products alter column product_name set not null", false)]
    public void AlterColumnNullability_PostgresSetDropNotNull(string sql, bool expectedNullableAfter)
    {
        // DROP tokenizes as a plain Identifier and SET as a Keyword in this
        // tokenizer, so these two forms used to be misparsed in opposite
        // ways by falling through to ParseColumnDef's "name type ..." shape:
        // DROP NOT NULL was read as DbType "drop" narrowed to NOT NULL (the
        // reverse of dropping the constraint), and SET NOT NULL failed
        // outright since "SET" is never an identifier. Neither form has a
        // type token at all, so they get their own statement kind.
        var statements = MigrationParser.Parse(sql);

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.AlterColumnNullability, stmt.Kind);
        Assert.Equal("product_name", Assert.Single(stmt.ColumnNames));
        Assert.Equal(expectedNullableAfter, stmt.NullableAfter);
        Assert.Empty(stmt.Columns);
    }

    [Theory]
    [InlineData("alter table products alter column product_name drop default")]
    [InlineData("alter table products alter column product_name set default 'n/a'")]
    public void AlterColumnDefault_Postgres_IgnoredNotMisparsed(string sql)
    {
        // ColumnSchema tracks no default-value field, so these are a true
        // no-op for the effective schema -- but they still need to be
        // recognized directly rather than falling through to the same
        // misparse as the NOT NULL forms above.
        var statements = MigrationParser.Parse(sql);

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.Ignored, stmt.Kind);
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

    [Theory]
    [InlineData("amount double precision not null", "double precision")]
    [InlineData("created_at timestamp with time zone not null", "timestamp with time zone")]
    [InlineData("created_at timestamp without time zone not null", "timestamp without time zone")]
    [InlineData("start_time time with time zone not null", "time with time zone")]
    [InlineData("start_time time without time zone not null", "time without time zone")]
    public void MultiWordType_CapturedInFull_NotTruncatedToFirstWord(string columnDef, string expectedDbType)
    {
        var statements = MigrationParser.Parse($"alter table t add {columnDef}");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.AddColumn, stmt.Kind);
        var col = Assert.Single(stmt.Columns);
        Assert.Equal(expectedDbType, col.DbType);
    }

    [Fact]
    public void CharacterVarying_CapturesMaxLength_NotLostBehindTheContinuationWord()
    {
        // The facet paren follows "varying", not "character" -- without
        // absorbing "varying" into DbType first, the parser looked for "("
        // immediately after "character" and never found "(40)", silently
        // losing the length entirely (a live pull of the same column
        // reports MaxLength=40).
        var statements = MigrationParser.Parse("alter table t add name character varying(40) not null");

        var stmt = Assert.Single(statements);
        var col = Assert.Single(stmt.Columns);
        Assert.Equal("character varying", col.DbType);
        Assert.Equal(40, col.MaxLength);
    }

    [Fact]
    public void BitVarying_CapturesMaxLength()
    {
        var statements = MigrationParser.Parse("alter table t add flags bit varying(10) not null");

        var stmt = Assert.Single(statements);
        var col = Assert.Single(stmt.Columns);
        Assert.Equal("bit varying", col.DbType);
        Assert.Equal(10, col.MaxLength);
    }

    [Theory]
    [InlineData("flag bit not null", null)]
    [InlineData("flags bit(1) not null", 1)]
    [InlineData("flags bit(5) not null", 5)]
    public void Bit_CapturesLengthFacet_NotAlwaysNull(string columnDef, int? expectedMaxLength)
    {
        // Bare "bit" or "bit(1)" (bool-shaped) vs. "bit(5)" (a genuine
        // multi-bit string DialectMapper must NOT mistype as bool) are
        // distinguished only by this MaxLength value -- before this fix
        // ApplyFacets had no case for "bit" at all, so every migration-
        // declared bit column always reported MaxLength null regardless of
        // its actual declared length, incorrectly satisfying DialectMapper's
        // "length is null" bool guard for bit(5) too.
        var statements = MigrationParser.Parse($"alter table t add {columnDef}");

        var stmt = Assert.Single(statements);
        var col = Assert.Single(stmt.Columns);
        Assert.Equal("bit", col.DbType);
        Assert.Equal(expectedMaxLength, col.MaxLength);
    }

    [Theory]
    [InlineData("qty int unsigned not null", "int unsigned")]
    [InlineData("qty bigint(20) unsigned not null", "bigint unsigned")]
    [InlineData("qty int(10) unsigned zerofill not null", "int unsigned")]
    [InlineData("qty smallint unsigned null", "smallint unsigned")]
    public void MySqlUnsigned_LandsInDbType_MatchingLivePullFormat(string columnDef, string expectedDbType)
    {
        // DialectMapper.MapDbTypeToCSharp only widens to uint/ulong/ushort
        // when dbType.Contains("unsigned") -- exactly the format
        // MySqlExtractor's live pull already produces (dataType + "
        // unsigned"). Before this fix, UNSIGNED fell through to the
        // generic "unknown flag, skip token" case and never reached
        // DbType, so a migration-declared "int unsigned" column simulated
        // here silently disagreed with the same column live-pulled after
        // the migration: the simulated schema mapped it to plain (signed)
        // int, reintroducing the overflow bug the unsigned mapping fix
        // exists to prevent. ZEROFILL always rides with UNSIGNED and is
        // consumed but not appended, matching MySqlExtractor's format
        // exactly (it never captures ZEROFILL either).
        var statements = MigrationParser.Parse($"alter table t add {columnDef}");

        var stmt = Assert.Single(statements);
        var col = Assert.Single(stmt.Columns);
        Assert.Equal(expectedDbType, col.DbType);
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
    public void MixedAddDropAdd_AppliesAllThreeActions()
    {
        var (schema, errors) = Apply(BaseSchema(),
            "alter table products add discontinued_at datetime null, drop column unit_price, add note nvarchar(50)");

        Assert.Empty(errors);
        var table = schema.Tables["products"];
        Assert.True(table.Columns.ContainsKey("discontinued_at"));
        Assert.False(table.Columns.ContainsKey("unit_price"));
        Assert.True(table.Columns.ContainsKey("note"));
        // the untouched column survives all three actions
        Assert.True(table.Columns.ContainsKey("product_name"));
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
    public void DropColumn_RemovesSingleColumnIndex_AndPrunesDroppedColumnFromComposite()
    {
        var snapshot = BaseSchema();
        snapshot.Tables["products"].Indexes.Add(new IndexSchema { Name = "ix_unit_price", Columns = new List<string> { "unit_price" }, IsUnique = false });
        snapshot.Tables["products"].Indexes.Add(new IndexSchema { Name = "ix_name_price", Columns = new List<string> { "product_name", "unit_price" }, IsUnique = true });
        snapshot.Tables["products"].Indexes.Add(new IndexSchema { Name = "ix_name", Columns = new List<string> { "product_name" }, IsUnique = false });

        var (schema, errors) = Apply(snapshot, "alter table products drop column unit_price");

        Assert.Empty(errors);
        var indexes = schema.Tables["products"].Indexes;
        // single-column index on the dropped column is gone entirely
        Assert.DoesNotContain(indexes, ix => ix.Name == "ix_unit_price");
        // composite index survives, pruned down to its surviving column
        var composite = Assert.Single(indexes, ix => ix.Name == "ix_name_price");
        Assert.Equal(new[] { "product_name" }, composite.Columns);
        Assert.True(composite.IsUnique);
        // unrelated index is untouched
        var untouched = Assert.Single(indexes, ix => ix.Name == "ix_name");
        Assert.Equal(new[] { "product_name" }, untouched.Columns);
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
    public void MySqlModify_HonorsDeclaredIdentity_UnlikeOtherDialectsAlterColumn()
    {
        // MySQL's MODIFY fully REDEFINES the column (unlike SQL Server/
        // Postgres/SQLite's ALTER COLUMN, which cannot touch identity at
        // all): a MODIFY that adds AUTO_INCREMENT really does add it, and
        // one that omits it on an already-identity column really does drop
        // it. Previously ApplyAlterColumn always forced back the
        // pre-migration IsIdentity regardless of dialect, so a
        // MODIFY ... AUTO_INCREMENT silently no-opped in the simulated
        // schema even though the parser correctly read it.
        var mysqlSchema = BaseSchema();
        mysqlSchema.Dialect = "mysql";
        mysqlSchema.Tables["products"].Columns["product_id"].IsIdentity = false;

        var (added, errors) = Apply(mysqlSchema, "alter table products modify product_id int not null auto_increment");
        Assert.Empty(errors);
        Assert.True(added.Tables["products"].Columns["product_id"].IsIdentity);

        var mysqlSchema2 = BaseSchema();
        mysqlSchema2.Dialect = "mysql";
        mysqlSchema2.Tables["products"].Columns["product_id"].IsIdentity = true;

        var (removed, errors2) = Apply(mysqlSchema2, "alter table products modify product_id int not null");
        Assert.Empty(errors2);
        Assert.False(removed.Tables["products"].Columns["product_id"].IsIdentity);

        // Control: SQL Server's ALTER COLUMN still preserves identity
        // regardless of what the statement's own type token says, matching
        // AlterColumn_ChangesFacets_KeepsKeyAndIdentity above.
        var (sqlServerSchema, errors3) = Apply(BaseSchema(), "alter table products alter column product_id bigint not null");
        Assert.Empty(errors3);
        Assert.True(sqlServerSchema.Tables["products"].Columns["product_id"].IsIdentity);
    }

    [Theory]
    [InlineData("alter table products alter column product_name drop not null", true)]
    [InlineData("alter table products alter column product_name set not null", false)]
    public void AlterColumnNullability_ChangesOnlyNullability_PreservesTypeAndFacets(string sql, bool expectedNullable)
    {
        var (schema, errors) = Apply(BaseSchema(), sql);

        Assert.Empty(errors);
        var col = schema.Tables["products"].Columns["product_name"];
        Assert.Equal(expectedNullable, col.IsNullable);
        // Unlike AlterColumn's wholesale replace, there is no freshly-parsed
        // (and here, misparsed) replacement column to merge from -- the
        // existing column is mutated in place, so type/facets survive.
        Assert.Equal("nvarchar", col.DbType);
        Assert.Equal(40, col.MaxLength);
        Assert.True(col.IsUnicode);
        Assert.False(col.IsPrimaryKey);
    }

    [Theory]
    [InlineData("alter table products alter column product_name drop default")]
    [InlineData("alter table products alter column product_name set default 'n/a'")]
    public void AlterColumnDefault_NoSchemaShapeImpact(string sql)
    {
        var (schema, errors) = Apply(BaseSchema(), sql);

        Assert.Empty(errors);
        var col = schema.Tables["products"].Columns["product_name"];
        Assert.Equal("nvarchar", col.DbType);
        Assert.Equal(40, col.MaxLength);
        Assert.False(col.IsNullable);
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

    // ── Case-insensitivity: SQL Server/MySQL identifiers are effectively
    // case-insensitive under their default collations, so a migration
    // written with different casing than the snapshot's stored casing must
    // still resolve -- not report a false JNT9002 "does not exist".

    [Fact]
    public void AddColumn_TableNameDifferentCase_Resolves()
    {
        var (schema, errors) = Apply(BaseSchema(), "alter table Products add supplier_note nvarchar(50) null");

        Assert.Empty(errors);
        Assert.True(schema.Tables["products"].Columns.ContainsKey("supplier_note"));
    }

    [Fact]
    public void DropColumn_ColumnNameDifferentCase_Resolves_PreservesOtherColumns()
    {
        var (schema, errors) = Apply(BaseSchema(), "alter table products drop column PRODUCT_NAME");

        Assert.Empty(errors);
        var remaining = schema.Tables["products"].Columns.Values.Select(c => c.Name).ToList();
        Assert.Equal(new[] { "product_id", "unit_price" }, remaining);
    }

    [Fact]
    public void AlterColumn_DifferentCase_ReplacesInPlace_NoDuplicateEntry()
    {
        // Indexing by the migration's own casing instead of the resolved
        // key would silently ADD a second, differently-cased column entry
        // (Dictionary key equality is case-sensitive) rather than replacing
        // the existing one.
        var (schema, errors) = Apply(BaseSchema(), "alter table products alter column Product_Name nvarchar(10) not null");

        Assert.Empty(errors);
        var table = schema.Tables["products"];
        Assert.Equal(3, table.Columns.Count); // no phantom duplicate
        Assert.True(table.Columns.ContainsKey("product_name"));
        Assert.False(table.Columns.ContainsKey("Product_Name"));
        Assert.Equal(10, table.Columns["product_name"].MaxLength);
    }

    [Fact]
    public void DropTable_DifferentCase_Resolves()
    {
        var (schema, errors) = Apply(BaseSchema(), "drop table Products");

        Assert.Empty(errors);
        Assert.False(schema.Tables.ContainsKey("products"));
    }

    [Fact]
    public void CreateTable_DifferentCase_DuplicateDetected()
    {
        var (_, errors) = Apply(BaseSchema(),
            "create table PRODUCTS (id int not null primary key)");

        var error = Assert.Single(errors);
        Assert.Equal("JNT9002", error.Code);
    }

    [Fact]
    public void AddPrimaryKey_ColumnNameDifferentCase_Resolves()
    {
        var (schema, errors) = Apply(BaseSchema(),
            "alter table products add constraint pk_x primary key (PRODUCT_NAME)");

        Assert.Empty(errors);
        Assert.True(schema.Tables["products"].Columns["product_name"].IsPrimaryKey);
    }
}
