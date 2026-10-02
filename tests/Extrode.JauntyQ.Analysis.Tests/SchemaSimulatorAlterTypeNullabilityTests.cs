using System.Collections.Generic;
using System.Linq;
using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Diff;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaSimulatorAlterTypeNullabilityTests
{
    private static DatabaseSchema Snapshot(string dialect, bool qtyNullable)
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        var table = new TableSchema { Name = "items" };
        table.Columns["id"] = new ColumnSchema { Name = "id", DbType = "integer", IsPrimaryKey = true };
        table.Columns["qty"] = new ColumnSchema { Name = "qty", DbType = "integer", IsNullable = qtyNullable };
        schema.Tables["items"] = table;
        return schema;
    }

    private static (DatabaseSchema Effective, List<AnalysisDiagnostic> Errors) Apply(DatabaseSchema snapshot, string sql)
    {
        var errors = new List<AnalysisDiagnostic>();
        var effective = SchemaSimulator.Apply(snapshot, new[] { ("V2__m.sql", MigrationParser.Parse(sql)) }, errors);
        return (effective, errors);
    }

    [Fact]
    public void Postgres_alter_type_keeps_not_null()
    {
        var snapshot = Snapshot("postgres", qtyNullable: false);
        var (effective, errors) = Apply(snapshot, "ALTER TABLE items ALTER COLUMN qty TYPE bigint;");

        var qty = effective.Tables["items"].Columns["qty"];
        Assert.Empty(errors);
        Assert.Equal("bigint", qty.DbType);
        Assert.False(qty.IsNullable);
        var change = Assert.Single(StructuralSchemaDiff.Compute(snapshot, effective).ModifiedTables.Single().ModifiedColumns);
        Assert.Equal(new[] { ColumnChangeKind.Type }, change.Kinds);
    }

    [Fact]
    public void Postgres_set_data_type_keeps_not_null()
    {
        var (effective, errors) = Apply(Snapshot("postgres", qtyNullable: false), "ALTER TABLE items ALTER COLUMN qty SET DATA TYPE bigint;");

        var qty = effective.Tables["items"].Columns["qty"];
        Assert.Empty(errors);
        Assert.Equal("bigint", qty.DbType);
        Assert.False(qty.IsNullable);
    }

    [Fact]
    public void Postgres_alter_type_keeps_nullable()
    {
        var (effective, _) = Apply(Snapshot("postgres", qtyNullable: true), "ALTER TABLE items ALTER COLUMN qty TYPE bigint;");

        Assert.True(effective.Tables["items"].Columns["qty"].IsNullable);
    }

    [Fact]
    public void Postgres_alter_type_with_using_keeps_not_null()
    {
        var (effective, errors) = Apply(Snapshot("postgres", qtyNullable: false), "ALTER TABLE items ALTER COLUMN qty TYPE bigint USING qty::bigint;");

        var qty = effective.Tables["items"].Columns["qty"];
        Assert.Empty(errors);
        Assert.Equal("bigint", qty.DbType);
        Assert.False(qty.IsNullable);
    }

    [Fact]
    public void Parser_marks_only_the_type_form_as_type_only()
    {
        var pg = Assert.Single(MigrationParser.Parse("ALTER TABLE items ALTER COLUMN qty TYPE bigint;"));
        var ss = Assert.Single(MigrationParser.Parse("ALTER TABLE items ALTER COLUMN qty bigint;"));
        var my = Assert.Single(MigrationParser.Parse("ALTER TABLE items MODIFY qty bigint;"));

        Assert.True(pg.TypeOnly);
        Assert.False(ss.TypeOnly);
        Assert.False(my.TypeOnly);
    }

    [Fact]
    public void Sql_server_alter_column_without_null_clause_still_redefines_nullability()
    {
        var (effective, _) = Apply(Snapshot("sqlserver", qtyNullable: false), "ALTER TABLE items ALTER COLUMN qty bigint;");

        Assert.True(effective.Tables["items"].Columns["qty"].IsNullable);
    }

    [Fact]
    public void Mysql_modify_without_null_clause_still_redefines_nullability()
    {
        var (effective, _) = Apply(Snapshot("mysql", qtyNullable: false), "ALTER TABLE items MODIFY qty bigint;");

        Assert.True(effective.Tables["items"].Columns["qty"].IsNullable);
    }
}
