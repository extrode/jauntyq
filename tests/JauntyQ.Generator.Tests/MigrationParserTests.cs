using System;
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
    public void CreateTable_TableLevelUniqueForeignCheck_EachSurfacesAsSiblingUnsupported()
    {
        // AUD-R4-18: table-level UNIQUE/FOREIGN KEY/CHECK constraints used to
        // be silently dropped with no diagnostic at all, unlike the identical
        // constructs added later via ALTER TABLE ADD CONSTRAINT (correctly
        // Unsupported/JNT9001). Each must now surface as its own sibling
        // Unsupported statement alongside the CreateTable statement, so the
        // effective schema's incompleteness isn't silent.
        var statements = MigrationParser.Parse(@"
create table products (
    product_id int not null primary key,
    category_id int not null,
    product_name varchar(40) not null,
    unit_price decimal(10,2) not null,
    constraint uq_products_name unique (product_name),
    constraint fk_products_category foreign key (category_id) references categories (category_id),
    constraint chk_products_price check (unit_price > 0)
)");

        Assert.Equal(4, statements.Count);

        var createStmt = statements[0];
        Assert.Equal(MigrationStatementKind.CreateTable, createStmt.Kind);
        Assert.Equal("products", createStmt.TableName);
        Assert.Equal(4, createStmt.Columns.Count);

        for (int i = 1; i < 4; i++)
            Assert.Equal(MigrationStatementKind.Unsupported, statements[i].Kind);

        Assert.Contains("unique", statements[1].RawText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("foreign", statements[2].RawText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("check", statements[3].RawText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("primary key clustered (order_id, item_no)")]
    [InlineData("primary key nonclustered (order_id, item_no)")]
    [InlineData("constraint pk_order_items primary key clustered (order_id, item_no)")]
    [InlineData("constraint pk_order_items primary key nonclustered (order_id, item_no)")]
    public void CreateTable_TableLevelPrimaryKeyClustered_MarksColumns(string pkClause)
    {
        // AUD-R16-01: SQL Server's idiomatic table-level primary-key form --
        // CONSTRAINT name PRIMARY KEY CLUSTERED (cols), the literal text SSMS
        // itself generates for every scripted table -- was silently dropped.
        // ReadParenNameList requires "(" immediately after "KEY"; CLUSTERED/
        // NONCLUSTERED sitting in between made it return an empty list, and
        // unlike the sibling UNIQUE/FOREIGN/CHECK branch (AUD-R4-18), this
        // branch had no fallback: it just `continue`d with no Unsupported
        // sibling and no diagnostic at all. No column ever got IsPrimaryKey,
        // so a table keyed only this way was silently treated as keyless by
        // downstream consumers (e.g. UpsertKeyResolver) with zero signal to
        // the developer.
        var statements = MigrationParser.Parse($@"
create table order_items (
    order_id int not null,
    item_no int not null,
    qty int not null,
    {pkClause}
)");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.CreateTable, stmt.Kind);
        Assert.True(stmt.Columns.Single(c => c.Name == "order_id").IsPrimaryKey);
        Assert.True(stmt.Columns.Single(c => c.Name == "item_no").IsPrimaryKey);
        Assert.False(stmt.Columns.Single(c => c.Name == "qty").IsPrimaryKey);
    }

    [Fact]
    public void CreateTable_TableLevelPrimaryKeyMalformed_SurfacesAsSiblingUnsupported()
    {
        // AUD-R16-01 general fallback: if a table-level PRIMARY KEY clause
        // still can't be parsed into a column list after the CLUSTERED/
        // NONCLUSTERED fix above (e.g. some other unanticipated form), it
        // must surface as an Unsupported sibling -- matching UNIQUE/FOREIGN/
        // CHECK's existing behavior -- instead of silently vanishing with no
        // diagnostic, which was the root cause of this finding.
        var statements = MigrationParser.Parse(@"
create table order_items (
    order_id int not null,
    item_no int not null,
    constraint pk_order_items primary key nocheck
)");

        Assert.Equal(2, statements.Count);
        Assert.Equal(MigrationStatementKind.CreateTable, statements[0].Kind);
        Assert.False(statements[0].Columns.Single(c => c.Name == "order_id").IsPrimaryKey);
        Assert.Equal(MigrationStatementKind.Unsupported, statements[1].Kind);
        Assert.Contains("primary", statements[1].RawText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("gadget_id int not null primary key clustered identity(1,1)")]
    [InlineData("gadget_id int not null primary key nonclustered identity(1,1)")]
    public void CreateTable_ColumnLevelPrimaryKeyClustered_AlreadyHandled(string columnDef)
    {
        // AUD-R16-01 sibling-sweep (§4.4): the column-level "col ... PRIMARY
        // KEY [CLUSTERED|NONCLUSTERED]" flag in ParseColumnDef's flag-scanning
        // loop -- a different code path from the table-level constraint
        // clause fixed above -- doesn't require a paren immediately after
        // KEY, so a trailing CLUSTERED/NONCLUSTERED token was already falling
        // through harmlessly to the generic "unknown flag, skip one token"
        // branch with IsPrimaryKey already correctly set. Recorded as a
        // confirmed-PASS regression guard, not part of the fix.
        var statements = MigrationParser.Parse($"create table gadgets ({columnDef}, name varchar(40) not null)");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.CreateTable, stmt.Kind);
        var id = stmt.Columns.Single(c => c.Name == "gadget_id");
        Assert.True(id.IsPrimaryKey);
        Assert.True(id.IsIdentity);
    }

    [Theory]
    [InlineData("alter table products add constraint pk_products primary key clustered (product_id)")]
    [InlineData("alter table products add constraint pk_products primary key nonclustered (product_id)")]
    [InlineData("alter table products add primary key clustered (product_id)")]
    public void AddConstraintPrimaryKeyClustered_ParsesAsAddPrimaryKey(string sql)
    {
        // AUD-R16-01 sibling: the same CLUSTERED/NONCLUSTERED gap in
        // ReadParenNameList affected ALTER TABLE ADD CONSTRAINT ... PRIMARY
        // KEY CLUSTERED too, though that path already fell back to
        // Unsupported/JNT9001 (S1, not silent) rather than vanishing
        // entirely. Fixed to correctly recognize it as AddPrimaryKey instead
        // of merely diagnosing its absence.
        var statements = MigrationParser.Parse(sql);

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.AddPrimaryKey, stmt.Kind);
        Assert.Equal("products", stmt.TableName);
        Assert.Equal(new[] { "product_id" }, stmt.ColumnNames);
    }

    [Fact]
    public void CreateTable_NoTableLevelConstraints_StillSingleStatement()
    {
        // Guards against a regression where every CreateTable now
        // unconditionally carries trailing Unsupported siblings.
        var statements = MigrationParser.Parse(@"
create table widgets (
    widget_id int not null primary key,
    name varchar(40) not null
)");

        Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.CreateTable, statements[0].Kind);
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

    [Theory]
    [InlineData("alter table products alter column category_id drop identity")]
    [InlineData("alter table products alter column category_id drop identity if exists")]
    [InlineData("alter table products alter column category_id add generated always as identity")]
    [InlineData("alter table products alter column category_id add generated by default as identity")]
    [InlineData("alter table products alter column category_id set generated always")]
    public void AlterColumnIdentityToggle_Postgres_Unsupported_NotMisparsed(string sql)
    {
        // None of these forms carry a type token, so falling through to
        // ParseColumnDef's "name type ..." shape used to misparse the
        // literal ADD/DROP/SET keyword itself as the column's new DbType
        // (e.g. "drop identity" -> DbType "drop", corrupting the real type
        // and every facet) and, for "DROP IDENTITY" specifically, land the
        // literal IDENTITY keyword on ParseColumnDef's IDENTITY-flag check
        // and set IsIdentity=true -- backwards from what DROP means.
        // ApplyAlterColumn can't represent an identity toggle via AlterColumn
        // for Postgres regardless (identity stays pinned to the pre-migration
        // value there), so this must surface as Unsupported/JNT9001 instead
        // of silently corrupting the column.
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

    /// <summary>
    /// AUD-R11 (§2.4-Ignored-statement-list-scope-check): locks in the exact
    /// membership of MigrationParser's top-level "cannot affect the
    /// table/column model" bucket (the block right after ALTER TABLE in
    /// ParseStatement) -- CREATE INDEX/UNIQUE INDEX, DROP INDEX, SET, USE,
    /// BEGIN/COMMIT/ROLLBACK, GRANT/REVOKE/DENY. Each is provably incapable
    /// of changing a column/table's shape (index DDL, session-scoped
    /// settings, transaction control, permissions), so classifying them
    /// Ignored rather than the loud Unsupported/JNT9001 fallback is correct
    /// -- but nothing previously asserted this whole set in one place, so a
    /// future edit that widened one of these conditions (e.g. broadening
    /// the CREATE/UNIQUE check to also swallow CREATE VIEW or CREATE
    /// SEQUENCE, which DO need modeling -- see JauntyQGenerator.Part7.cs's
    /// own disclosed-limitation comment about indexes specifically) could
    /// silently start dropping something real with no test catching it.
    /// Also confirms constructs that are NOT in this list (CREATE SEQUENCE/
    /// PROCEDURE/VIEW/TRIGGER, entirely unhandled by MigrationParser) fall
    /// through to the loud Unsupported/JNT9001 path instead of being
    /// silently swallowed -- the safe direction if this list ever needs to
    /// grow.
    /// </summary>
    [Theory]
    [InlineData("create index ix_products_name on products (product_name)")]
    [InlineData("create unique index ix_products_sku on products (sku)")]
    [InlineData("drop index ix_products_name")]
    [InlineData("drop index ix_products_name on products")]
    [InlineData("set foreign_key_checks = 0")]
    [InlineData("use jauntyqdb")]
    [InlineData("begin transaction")]
    [InlineData("begin")]
    [InlineData("commit")]
    [InlineData("commit transaction")]
    [InlineData("rollback")]
    [InlineData("grant select on products to app_user")]
    [InlineData("revoke select on products from app_user")]
    [InlineData("deny select on products to app_user")]
    public void TopLevelIgnoredStatementList_ExactMembership(string sql)
    {
        var statements = MigrationParser.Parse(sql);

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.Ignored, stmt.Kind);
    }

    [Theory]
    [InlineData("create sequence products_seq start with 1 increment by 1")]
    [InlineData("create procedure get_products as select * from products")]
    [InlineData("create view active_products as select * from products where active = 1")]
    [InlineData("create trigger trg_products_audit on products after insert as select 1")]
    public void ConstructsOutsideIgnoredList_FallThroughToLoudUnsupported_NotSilentlySwallowed(string sql)
    {
        var statements = MigrationParser.Parse(sql);

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.Unsupported, stmt.Kind);
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
    // Precision facet between the type name and WITH/WITHOUT TIME ZONE
    // ("TIME(6) WITH TIME ZONE" is the actual SQL grammar -- the facet comes
    // before the timezone clause, not after it). The WITH/WITHOUT check used
    // to run before the facet-paren was consumed, so it only ever matched
    // the no-facet form; a faceted column like this one left DbType bare
    // ("time"/"timestamp"), and for "time" specifically that's not just a
    // lost string -- DialectMapper maps bare "time" to TimeSpan but "time
    // with time zone" to DateTimeOffset, so the generated property silently
    // disagreed with a live pull of the same column.
    [InlineData("start_time time(6) with time zone not null", "time with time zone")]
    [InlineData("start_time time(6) without time zone not null", "time without time zone")]
    [InlineData("created_at timestamp(3) with time zone not null", "timestamp with time zone")]
    [InlineData("created_at timestamp(3) without time zone not null", "timestamp without time zone")]
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

    [Fact]
    public void SqlServerComputedColumn_AsExprShorthand_EntersSchemaAsComputed()
    {
        // "Total AS (Qty*Price)" has no declared type -- "AS" tokenizes as a
        // Keyword, not an Identifier, so before this fix ParseColumnDef's
        // "def[1] must be an Identifier" guard rejected it and returned
        // null, and ParseCreateTable silently dropped the column from the
        // effective schema with no diagnostic. A later query selecting Total
        // would then falsely fail JNT2002 "column does not exist" even
        // though the column is real and live.
        var statements = MigrationParser.Parse(
            "create table orders (id int primary key, qty int, price decimal(10,2), total as (qty*price) persisted not null)");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.CreateTable, stmt.Kind);
        Assert.Equal(4, stmt.Columns.Count);

        var total = stmt.Columns[3];
        Assert.Equal("total", total.Name);
        Assert.True(total.IsComputed);
        Assert.False(total.IsNullable);
    }

    [Fact]
    public void SqlServerComputedColumn_WithoutPersisted_DefaultsNullable()
    {
        var statements = MigrationParser.Parse(
            "create table orders (id int primary key, qty int, price decimal(10,2), total as (qty*price))");

        var stmt = Assert.Single(statements);
        var total = stmt.Columns[3];
        Assert.True(total.IsComputed);
        Assert.True(total.IsNullable);
    }

    [Theory]
    [InlineData("alter table orders add column total numeric generated always as (qty*price) stored", "stored")]
    [InlineData("alter table orders add column total numeric generated always as (qty*price) virtual", "virtual")]
    public void GeneratedColumn_PostgresMySqlStyle_MarkedComputed_NotOrdinaryWritableColumn(string sql, string mode)
    {
        // Before this fix, GENERATED/ALWAYS/AS/the parenthesized expression/
        // STORED|VIRTUAL were each eaten one-at-a-time by the flags loop's
        // generic unknown-flag skip, and IsComputed was never set. The
        // simulated effective schema then reported "total" as an ordinary
        // writable column, so CrudColumnRules.InsertableColumns/
        // UpdatableColumns included it in a generated INSERT/UPDATE --
        // compiles clean, fails at runtime ("cannot insert into generated
        // column").
        var statements = MigrationParser.Parse(sql);

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.AddColumn, stmt.Kind);
        var col = Assert.Single(stmt.Columns);
        Assert.Equal("total", col.Name);
        Assert.True(col.IsComputed, $"expected IsComputed=true for GENERATED ... {mode}");
    }

    [Fact]
    public void GeneratedAsIdentity_Postgres_StillRecognizedAsIdentity_NotComputed()
    {
        // Guards against the new GENERATED handling accidentally swallowing
        // the pre-existing "GENERATED ALWAYS AS IDENTITY" column-level
        // identity form (distinct from the ALTER COLUMN identity-TOGGLE
        // form already covered elsewhere) -- AS IDENTITY must set
        // IsIdentity, not IsComputed.
        var statements = MigrationParser.Parse(
            "create table orders (id int generated always as identity primary key, name text)");

        var stmt = Assert.Single(statements);
        var id = stmt.Columns[0];
        Assert.True(id.IsIdentity);
        Assert.False(id.IsComputed);
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
    public void Real_NormalizedToDouble_OnlyOnMySql()
    {
        // MySQL parses REAL as a pure synonym for DOUBLE (unless the rare,
        // non-default REAL_AS_FLOAT sql_mode is active) -- a live re-pull of
        // a migration-declared "REAL" column reports DbType "double", not
        // "real". Left unnormalized, DialectMapper would map it to C# float
        // (4-byte) instead of double (8-byte), silently losing precision.
        // Postgres/SQL Server's REAL genuinely is a 4-byte float and must
        // stay untouched.
        var (mysqlSchema, errors) = Apply(new DatabaseSchema { Dialect = "mysql" },
            "create table sensors (id int not null primary key, reading real not null)");
        Assert.Empty(errors);
        Assert.Equal("double", mysqlSchema.Tables["sensors"].Columns["reading"].DbType);

        var (pgSchema, _) = Apply(new DatabaseSchema { Dialect = "postgres" },
            "create table sensors (id int not null primary key, reading real not null)");
        Assert.Equal("real", pgSchema.Tables["sensors"].Columns["reading"].DbType);
    }

    [Fact]
    public void MySqlSerial_NormalizedToBigintUnsigned_OnlyOnMySql()
    {
        // AUD-R18: MySQL's SERIAL column type is desugared by the server
        // itself to "BIGINT UNSIGNED NOT NULL AUTO_INCREMENT UNIQUE" -- a
        // live re-pull of a migration-declared "id SERIAL" column reports
        // DbType "bigint unsigned" (MySqlExtractor's COLUMN_TYPE LIKE
        // '%unsigned%' check), never the literal "serial". Left
        // unnormalized, DialectMapper.MapDbTypeToCSharp's unsigned-widening
        // branch (keyed on the DbType string literally containing
        // "unsigned") never triggers, so the column maps to plain C# int
        // instead of the ulong a live pull produces -- silently both too
        // narrow (4 vs. 8 bytes) and wrong-signed. Postgres's SERIAL must
        // stay unnormalized ("serial" is already a synonym arm in
        // DialectMapper matching what Postgres's own information_schema
        // reports there).
        var (mysqlSchema, errors) = Apply(new DatabaseSchema { Dialect = "mysql" },
            "create table widgets (id serial primary key, name varchar(40) not null)");
        Assert.Empty(errors);
        var mysqlId = mysqlSchema.Tables["widgets"].Columns["id"];
        Assert.Equal("bigint unsigned", mysqlId.DbType);
        Assert.True(mysqlId.IsIdentity);
        Assert.False(mysqlId.IsNullable);
        Assert.Equal("ulong", DialectMapper.MapColumnToCSharp(mysqlId, "mysql"));

        var (pgSchema2, _) = Apply(new DatabaseSchema { Dialect = "postgres" },
            "create table widgets (id serial primary key, name varchar(40) not null)");
        var pgId = pgSchema2.Tables["widgets"].Columns["id"];
        Assert.Equal("serial", pgId.DbType);
        Assert.Equal("int", DialectMapper.MapColumnToCSharp(pgId, "postgres"));
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
    public void CreateTable_Sqlite_BareIntegerPrimaryKey_IsIdentity()
    {
        // SQLite aliases a single-column PRIMARY KEY declared with the exact
        // type "INTEGER" (no facet, no other keywords) to the table's rowid,
        // which auto-assigns 1, 2, 3, ... on insert -- confirmed live via
        // SqliteExtractor_IntegerPrimaryKey_IsIdentity (JauntyQ.Tests/
        // ExtractorTests.cs), which reports IsIdentity=true for this exact
        // shape after a real sqlite3 CREATE TABLE. The simulated
        // (pre-live-DB) effective schema must agree, or a project with a
        // pending "create table widgets (id INTEGER primary key, name text)"
        // migration sees IsIdentity=false right up until the migration is
        // actually applied and re-pulled, silently disagreeing with itself.
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        var (effective, errors) = Apply(schema,
            "create table widgets (id INTEGER primary key, name text)");

        Assert.Empty(errors);
        Assert.True(effective.Tables["widgets"].Columns["id"].IsIdentity);
    }

    [Theory]
    [InlineData("INT")]
    [InlineData("BIGINT")]
    public void CreateTable_Sqlite_NonLiteralIntegerPrimaryKey_IsNotIdentity(string declaredType)
    {
        // Only the literal declared type "INTEGER" aliases to the rowid --
        // "INT"/"BIGINT"/etc. share INTEGER storage affinity but do NOT get
        // auto-assigned values (confirmed live: SqliteExtractor_
        // NonLiteralIntegerPrimaryKey_IsNotIdentity). The simulator must not
        // over-eagerly mark every integer-typed PK as identity.
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        var (effective, _) = Apply(schema,
            $"create table widgets (id {declaredType} primary key, name text)");

        Assert.False(effective.Tables["widgets"].Columns["id"].IsIdentity);
    }

    [Fact]
    public void CreateTable_Sqlite_CompositeIntegerPrimaryKey_IsNotIdentity()
    {
        // SQLite only aliases a SINGLE-column INTEGER PRIMARY KEY to the
        // rowid; a composite primary key never gets rowid aliasing even if
        // one of its columns is declared INTEGER (confirmed live via
        // SqliteExtractor's own pkCols.Count == 1 guard).
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        var (effective, _) = Apply(schema,
            "create table order_items (order_id INTEGER, line_no INTEGER, qty INTEGER, primary key (order_id, line_no))");

        Assert.False(effective.Tables["order_items"].Columns["order_id"].IsIdentity);
        Assert.False(effective.Tables["order_items"].Columns["line_no"].IsIdentity);
    }

    [Fact]
    public void CreateTable_NonSqlite_BareIntegerPrimaryKey_IsNotIdentity()
    {
        // The rowid-aliasing rule is SQLite-specific; an "INTEGER" column
        // name has no special meaning as a bare type keyword on the other
        // three dialects (Postgres/MySQL/SQL Server don't even ship an
        // "INTEGER" alias behaving this way), so this must stay dialect-gated.
        var schema = new DatabaseSchema { Dialect = "postgres" };
        var (effective, _) = Apply(schema,
            "create table widgets (id INTEGER primary key, name text)");

        Assert.False(effective.Tables["widgets"].Columns["id"].IsIdentity);
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
    public void CreateTable_OtherConstraint_JNT9001Warning_NotSilentlyDropped_AndTableStillCreated()
    {
        // AUD-R4-18 end-to-end: a table-level constraint dropped during
        // CREATE TABLE parsing must both (a) still create the table with its
        // columns, and (b) surface JNT9001 for the unmodeled constraint --
        // not lose the diagnostic just because it rode along with a
        // CreateTable statement instead of a standalone ALTER TABLE.
        var (schema, errors) = Apply(BaseSchema(), @"
create table widgets (
    widget_id int not null primary key,
    name varchar(40) not null,
    constraint uq_widgets_name unique (name)
)");

        Assert.True(schema.Tables.ContainsKey("widgets"));
        Assert.Equal(2, schema.Tables["widgets"].Columns.Count);

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

    // ---- AUD-R6: SchemaSimulator.Clone() field parity, standing/mandatory
    // check per the audit criteria §2.3. Exercises every field of all 8
    // hand-cloned schema types (ColumnSchema's 11 facets, TableSchema,
    // DatabaseSchema, IndexSchema, ForeignKeySchema, ProcedureSchema,
    // ProcedureParam, SequenceSchema) through Clone() by applying a migration
    // that touches an unrelated table -- the exact shape that has silently
    // dropped whole sections (AUD-R1-02) and single fields (AUD-R4-12) before.
    // A diff against Clone()'s source was also performed by hand this round;
    // this test is the executable version of that diff so a future field
    // added to any of the 8 types and forgotten in Clone() fails a real test,
    // not just a "stay vigilant" comment.

    private static DatabaseSchema FullFacetSchema()
    {
        var schema = new DatabaseSchema { Dialect = "sqlserver" };
        schema.Tables["widgets"] = new TableSchema
        {
            Name = "widgets",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["widget_id"] = new()
                {
                    Name = "widget_id", DbType = "int", IsNullable = false, IsPrimaryKey = true,
                    IsIdentity = true, MaxLength = null, Precision = null, Scale = null,
                    IsUnicode = null, IsRowVersion = false, IsComputed = false
                },
                ["label"] = new()
                {
                    Name = "label", DbType = "nvarchar", IsNullable = true, IsPrimaryKey = false,
                    IsIdentity = false, MaxLength = 77, Precision = null, Scale = null,
                    IsUnicode = true, IsRowVersion = false, IsComputed = false
                },
                ["amount"] = new()
                {
                    Name = "amount", DbType = "decimal", IsNullable = true, IsPrimaryKey = false,
                    IsIdentity = false, MaxLength = null, Precision = 12, Scale = 4,
                    IsUnicode = null, IsRowVersion = false, IsComputed = false
                },
                ["search_label"] = new()
                {
                    Name = "search_label", DbType = "nvarchar", IsNullable = true,
                    IsComputed = true
                },
                ["stamp"] = new()
                {
                    Name = "stamp", DbType = "rowversion", IsNullable = false, IsRowVersion = true
                }
            },
            Indexes = new List<IndexSchema>
            {
                new() { Name = "ix_widgets_label", Columns = new List<string> { "label" }, IsUnique = true }
            }
        };
        schema.ForeignKeys.Add(new ForeignKeySchema
        {
            FromTable = "widgets", FromColumn = "widget_id", ToTable = "orders", ToColumn = "widget_id"
        });
        schema.Procedures["get_widget"] = new ProcedureSchema
        {
            Name = "get_widget",
            Params = new List<ProcedureParam>
            {
                new()
                {
                    Name = "out_total", DbType = "decimal", Direction = ProcedureParamDirection.Out,
                    IsNullable = true, MaxLength = null, Precision = 19, Scale = 6
                }
            },
            Results = new List<ColumnSchema>
            {
                new() { Name = "widget_id", DbType = "int" }
            }
        };
        schema.Sequences["widget_seq"] = new SequenceSchema
        {
            Name = "widget_seq", StartValue = 100, Increment = 5, MinValue = 1, MaxValue = 999999, CurrentValue = 235
        };
        return schema;
    }

    [Fact]
    public void PendingMigration_ClonePreservesEveryFieldOfEveryHandClonedSchemaType()
    {
        var (schema, errors) = Apply(FullFacetSchema(),
            "create table gadgets (gadget_id int not null primary key)");

        Assert.Empty(errors);

        var widgets = schema.Tables["widgets"];

        var widgetId = widgets.Columns["widget_id"];
        Assert.True(widgetId.IsPrimaryKey);
        Assert.True(widgetId.IsIdentity);
        Assert.False(widgetId.IsNullable);

        var label = widgets.Columns["label"];
        Assert.Equal(77, label.MaxLength);
        Assert.True(label.IsUnicode);
        Assert.True(label.IsNullable);

        var amount = widgets.Columns["amount"];
        Assert.Equal(12, amount.Precision);
        Assert.Equal(4, amount.Scale);

        Assert.True(widgets.Columns["search_label"].IsComputed);
        Assert.True(widgets.Columns["stamp"].IsRowVersion);

        var ix = Assert.Single(widgets.Indexes);
        Assert.Equal("ix_widgets_label", ix.Name);
        Assert.True(ix.IsUnique);
        Assert.Equal(new[] { "label" }, ix.Columns);

        var fk = Assert.Single(schema.ForeignKeys);
        Assert.Equal("widgets", fk.FromTable);
        Assert.Equal("widget_id", fk.FromColumn);
        Assert.Equal("orders", fk.ToTable);
        Assert.Equal("widget_id", fk.ToColumn);

        var proc = schema.Procedures["get_widget"];
        var param = Assert.Single(proc.Params);
        Assert.Equal(ProcedureParamDirection.Out, param.Direction);
        Assert.Equal(19, param.Precision);
        Assert.Equal(6, param.Scale);
        var result = Assert.Single(proc.Results);
        Assert.Equal("int", result.DbType);

        var seq = schema.Sequences["widget_seq"];
        Assert.Equal(100, seq.StartValue);
        Assert.Equal(5, seq.Increment);
        Assert.Equal(1, seq.MinValue);
        Assert.Equal(999999, seq.MaxValue);
        Assert.Equal(235, seq.CurrentValue);
    }
}
