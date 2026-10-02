using System.Collections.Generic;
using System.Linq;
using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Diff;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaSimulatorUnicodeParityTests
{
    private static DatabaseSchema Snapshot(string dialect, string nameType, bool? nameUnicode)
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        var table = new TableSchema { Name = "users" };
        table.Columns["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true };
        table.Columns["name"] = new ColumnSchema { Name = "name", DbType = nameType, MaxLength = 100, IsNullable = true, IsUnicode = nameUnicode };
        schema.Tables["users"] = table;
        return schema;
    }

    private static DatabaseSchema Apply(DatabaseSchema snapshot, string sql)
    {
        var errors = new List<AnalysisDiagnostic>();
        var effective = SchemaSimulator.Apply(snapshot, new[] { ("V2__m.sql", MigrationParser.Parse(sql)) }, errors);
        Assert.Empty(errors);
        return effective;
    }

    [Theory]
    [InlineData("postgres", "ALTER TABLE users ALTER COLUMN name TYPE varchar(200);")]
    [InlineData("mysql", "ALTER TABLE users MODIFY name varchar(200) NULL;")]
    public void Widening_a_text_column_reports_only_the_length(string dialect, string sql)
    {
        var snapshot = Snapshot(dialect, "varchar", nameUnicode: true);
        var effective = Apply(snapshot, sql);

        Assert.True(effective.Tables["users"].Columns["name"].IsUnicode);
        var change = Assert.Single(Assert.Single(StructuralSchemaDiff.Compute(snapshot, effective).ModifiedTables).ModifiedColumns);
        Assert.Equal(new[] { ColumnChangeKind.MaxLength }, change.Kinds);
    }

    [Theory]
    [InlineData("postgres", "text")]
    [InlineData("postgres", "citext")]
    [InlineData("postgres", "char(3)")]
    [InlineData("mysql", "varchar(10)")]
    [InlineData("mysql", "longtext")]
    [InlineData("sqlite", "text")]
    [InlineData("sqlite", "clob")]
    public void Text_columns_added_outside_sql_server_are_unicode(string dialect, string type)
    {
        var effective = Apply(Snapshot(dialect, "varchar", true), $"ALTER TABLE users ADD note {type} NULL;");

        Assert.True(effective.Tables["users"].Columns["note"].IsUnicode);
    }

    [Theory]
    [InlineData("varchar(10)", false)]
    [InlineData("char(10)", false)]
    [InlineData("nvarchar(10)", true)]
    [InlineData("nchar(10)", true)]
    public void Sql_server_unicode_follows_the_n_prefix(string type, bool expected)
    {
        var effective = Apply(Snapshot("sqlserver", "nvarchar", true), $"ALTER TABLE users ADD note {type} NULL;");

        Assert.Equal(expected, effective.Tables["users"].Columns["note"].IsUnicode);
    }

    [Theory]
    [InlineData("postgres", "integer")]
    [InlineData("mysql", "varbinary(16)")]
    [InlineData("sqlite", "blob")]
    public void Non_text_columns_keep_unicode_unset(string dialect, string type)
    {
        var effective = Apply(Snapshot(dialect, "varchar", true), $"ALTER TABLE users ADD note {type} NULL;");

        Assert.Null(effective.Tables["users"].Columns["note"].IsUnicode);
    }
}
