using System.Collections.Generic;
using System.Linq;
using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Diff;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaSimulatorMySqlEnumTests
{
    private const string StatusEnum = "OrdersStatus";

    private static DatabaseSchema Snapshot()
    {
        var schema = new DatabaseSchema { Dialect = "mysql" };
        var table = new TableSchema { Name = "orders" };
        table.Columns["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true };
        table.Columns["status"] = new ColumnSchema
        {
            Name = "status", DbType = "enum", EnumName = StatusEnum, IsNullable = false, MaxLength = 7, IsUnicode = true
        };
        schema.Tables["orders"] = table;
        var statusEnum = new EnumSchema { Name = StatusEnum };
        statusEnum.Members.Add(new EnumMember { Value = "pending", CSharpName = "Pending" });
        statusEnum.Members.Add(new EnumMember { Value = "shipped", CSharpName = "Shipped" });
        schema.Enums[StatusEnum] = statusEnum;
        return schema;
    }

    private static (DatabaseSchema Effective, SchemaDelta Delta, List<AnalysisDiagnostic> Errors) Apply(DatabaseSchema snapshot, string sql)
    {
        var errors = new List<AnalysisDiagnostic>();
        var effective = SchemaSimulator.Apply(snapshot, new[] { ("V2__m.sql", MigrationParser.Parse(sql)) }, errors);
        return (effective, StructuralSchemaDiff.Compute(snapshot, effective), errors);
    }

    private static string[] Members(DatabaseSchema schema, string enumName) =>
        schema.Enums[enumName].Members.Select(m => m.Value).ToArray();

    [Fact]
    public void Modify_with_the_same_members_keeps_the_enum_and_reports_nothing()
    {
        var (effective, delta, errors) = Apply(Snapshot(), "ALTER TABLE orders MODIFY status ENUM('pending','shipped') NOT NULL;");

        var status = effective.Tables["orders"].Columns["status"];
        Assert.Empty(errors);
        Assert.Equal(StatusEnum, status.EnumName);
        Assert.Equal(new[] { "pending", "shipped" }, Members(effective, StatusEnum));
        Assert.Empty(delta.ModifiedTables);
    }

    [Fact]
    public void Modify_that_removes_a_member_updates_the_enum_and_reports_an_enum_change()
    {
        var (effective, delta, _) = Apply(Snapshot(), "ALTER TABLE orders MODIFY status ENUM('pending') NOT NULL;");

        Assert.Equal(new[] { "pending" }, Members(effective, StatusEnum));
        var change = Assert.Single(Assert.Single(delta.ModifiedTables).ModifiedColumns);
        Assert.Contains(ColumnChangeKind.Enum, change.Kinds);
    }

    [Fact]
    public void Modify_that_adds_a_member_updates_the_enum_and_reports_an_enum_change()
    {
        var (effective, delta, _) = Apply(Snapshot(), "ALTER TABLE orders MODIFY COLUMN status ENUM('pending','shipped','returned') NOT NULL;");

        Assert.Equal(new[] { "pending", "shipped", "returned" }, Members(effective, StatusEnum));
        Assert.Equal(new[] { "pending", "shipped", "returned" }, effective.Enums[StatusEnum].Members.Select(m => m.CSharpName.ToLowerInvariant()).ToArray());
        var change = Assert.Single(Assert.Single(delta.ModifiedTables).ModifiedColumns);
        Assert.Equal(new[] { ColumnChangeKind.MaxLength, ColumnChangeKind.Enum }, change.Kinds);
    }

    [Fact]
    public void Modify_to_a_plain_type_drops_the_enum()
    {
        var (effective, delta, _) = Apply(Snapshot(), "ALTER TABLE orders MODIFY status varchar(20) NOT NULL;");

        Assert.Null(effective.Tables["orders"].Columns["status"].EnumName);
        Assert.False(effective.Enums.ContainsKey(StatusEnum));
        var change = Assert.Single(Assert.Single(delta.ModifiedTables).ModifiedColumns);
        Assert.Contains(ColumnChangeKind.Type, change.Kinds);
        Assert.Contains(ColumnChangeKind.Enum, change.Kinds);
    }

    [Theory]
    [InlineData("ALTER TABLE orders DROP COLUMN status;")]
    [InlineData("DROP TABLE orders;")]
    public void Removing_the_enum_column_removes_its_enum(string sql)
    {
        var (effective, _, errors) = Apply(Snapshot(), sql);

        Assert.Empty(errors);
        Assert.False(effective.Enums.ContainsKey(StatusEnum));
    }

    [Fact]
    public void An_enum_another_column_still_names_is_kept()
    {
        var snapshot = Snapshot();
        snapshot.Tables["orders"].Columns["old_status"] = new ColumnSchema
        {
            Name = "old_status", DbType = "enum", EnumName = StatusEnum, IsNullable = true, MaxLength = 7, IsUnicode = true
        };

        var (effective, _, _) = Apply(snapshot, "ALTER TABLE orders DROP COLUMN status;");

        Assert.True(effective.Enums.ContainsKey(StatusEnum));
    }

    [Fact]
    public void A_postgres_enum_outlives_its_column()
    {
        var snapshot = Snapshot();
        snapshot.Dialect = "postgres";

        var (effective, _, _) = Apply(snapshot, "ALTER TABLE orders DROP COLUMN status;");

        Assert.True(effective.Enums.ContainsKey(StatusEnum));
    }

    [Fact]
    public void Create_table_with_an_inline_enum_captures_it_like_a_live_pull()
    {
        var (effective, _, errors) = Apply(Snapshot(), "CREATE TABLE tickets (id int primary key, mood ENUM('ok','it''s bad','a,b') NULL);");

        var mood = effective.Tables["tickets"].Columns["mood"];
        Assert.Empty(errors);
        Assert.Equal("TicketsMood", mood.EnumName);
        Assert.Equal("enum", mood.DbType);
        Assert.Equal(8, mood.MaxLength);
        Assert.True(mood.IsUnicode);
        Assert.Equal(new[] { "ok", "it's bad", "a,b" }, Members(effective, "TicketsMood"));
    }

    [Theory]
    [InlineData("CREATE TABLE tickets (id int primary key, mood ENUM('ok','bad') CHARACTER SET latin1 NULL);", "tickets", "mood", false)]
    [InlineData("CREATE TABLE tickets (id int primary key, mood ENUM('ok','bad') CHARACTER SET utf8mb4 NULL);", "tickets", "mood", true)]
    [InlineData("ALTER TABLE orders ADD COLUMN mood ENUM('ok','bad') CHARACTER SET latin1 NULL;", "orders", "mood", false)]
    [InlineData("ALTER TABLE orders MODIFY COLUMN status ENUM('ok','bad') CHARACTER SET latin1 NOT NULL;", "orders", "status", false)]
    public void An_inline_enum_with_an_explicit_charset_takes_unicode_from_it(string sql, string table, string column, bool unicode)
    {
        var (effective, _, errors) = Apply(Snapshot(), sql);

        Assert.Empty(errors);
        Assert.NotNull(effective.Tables[table].Columns[column].EnumName);
        Assert.Equal(unicode, effective.Tables[table].Columns[column].IsUnicode);
    }

    [Fact]
    public void Add_column_with_an_inline_enum_captures_it()
    {
        var (effective, _, _) = Apply(Snapshot(), "ALTER TABLE orders ADD priority ENUM('low','high') NOT NULL DEFAULT 'low';");

        var priority = effective.Tables["orders"].Columns["priority"];
        Assert.Equal("OrdersPriority", priority.EnumName);
        Assert.False(priority.IsNullable);
        Assert.Equal(new[] { "low", "high" }, Members(effective, "OrdersPriority"));
    }

    [Fact]
    public void Non_mysql_dialects_capture_no_inline_enum()
    {
        var snapshot = Snapshot();
        snapshot.Dialect = "postgres";
        var (effective, _, _) = Apply(snapshot, "ALTER TABLE orders ADD priority ENUM('low','high');");

        Assert.Null(effective.Tables["orders"].Columns["priority"].EnumName);
        Assert.False(effective.Enums.ContainsKey("OrdersPriority"));
    }

    [Fact]
    public void Parser_reads_inline_enum_members_per_column()
    {
        var stmt = Assert.Single(MigrationParser.Parse("ALTER TABLE orders MODIFY status ENUM('pending','it''s') NOT NULL;"));

        Assert.Equal(new[] { "pending", "it's" }, stmt.EnumMembers["status"]);
        Assert.False(stmt.Columns[0].IsNullable);
    }

    [Fact]
    public void Parser_ignores_a_member_list_that_is_not_all_literals()
    {
        var stmt = Assert.Single(MigrationParser.Parse("ALTER TABLE orders ADD flag ENUM('a', 1) NULL;"));

        Assert.Empty(stmt.EnumMembers);
    }

    [Fact]
    public void Diff_reports_an_enum_change_when_only_the_members_differ()
    {
        var before = Snapshot();
        var after = Snapshot();
        after.Enums[StatusEnum].Members.Reverse();

        var change = Assert.Single(Assert.Single(StructuralSchemaDiff.Compute(before, after).ModifiedTables).ModifiedColumns);
        Assert.Equal(new[] { ColumnChangeKind.Enum }, change.Kinds);
    }

    [Fact]
    public void Diff_reports_an_enum_change_when_the_enum_name_differs()
    {
        var before = Snapshot();
        var after = Snapshot();
        after.Tables["orders"].Columns["status"].EnumName = null;

        var change = Assert.Single(Assert.Single(StructuralSchemaDiff.Compute(before, after).ModifiedTables).ModifiedColumns);
        Assert.Equal(new[] { ColumnChangeKind.Enum }, change.Kinds);
    }

    [Fact]
    public void Diff_reports_an_enum_change_when_the_named_enum_is_gone()
    {
        var before = Snapshot();
        var after = Snapshot();
        after.Enums.Remove(StatusEnum);

        var change = Assert.Single(Assert.Single(StructuralSchemaDiff.Compute(before, after).ModifiedTables).ModifiedColumns);
        Assert.Equal(new[] { ColumnChangeKind.Enum }, change.Kinds);
    }

    [Fact]
    public void Diff_matches_tables_case_insensitively()
    {
        var before = Snapshot();
        var after = Snapshot();
        var orders = after.Tables["orders"];
        after.Tables.Remove("orders");
        orders.Name = "Orders";
        orders.Columns["note"] = new ColumnSchema { Name = "note", DbType = "text" };
        after.Tables["Orders"] = orders;

        var delta = StructuralSchemaDiff.Compute(before, after);

        Assert.Empty(delta.AddedTables);
        Assert.Empty(delta.RemovedTables);
        Assert.Equal(new[] { "note" }, Assert.Single(delta.ModifiedTables).AddedColumns);
    }

    [Fact]
    public void Drop_and_recreate_under_another_casing_is_a_modification()
    {
        var (_, delta, _) = Apply(Snapshot(), "DROP TABLE ORDERS; CREATE TABLE ORDERS (id int primary key, status ENUM('pending','shipped') NOT NULL, note text);");

        Assert.Empty(delta.AddedTables);
        Assert.Empty(delta.RemovedTables);
        Assert.Equal(new[] { "note" }, Assert.Single(delta.ModifiedTables).AddedColumns);
    }

    [Fact]
    public void Diff_ignores_identical_enums()
    {
        Assert.Empty(StructuralSchemaDiff.Compute(Snapshot(), Snapshot()).ModifiedTables);
    }
}
