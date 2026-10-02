using System.Collections.Generic;
using System.Linq;
using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Diff;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaSimulatorTypeResolutionTests
{
    private static DatabaseSchema Snapshot(string dialect)
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        var table = new TableSchema { Name = "people" };
        table.Columns["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true };
        schema.Tables["people"] = table;
        return schema;
    }

    private static DatabaseSchema Apply(DatabaseSchema snapshot, string sql)
    {
        var errors = new List<AnalysisDiagnostic>();
        var effective = SchemaSimulator.Apply(snapshot, new[] { ("V2__m.sql", MigrationParser.Parse(sql)) }, errors);
        Assert.Empty(errors);
        return effective;
    }

    private static DatabaseSchema SqlServerWithSsn()
    {
        var schema = Snapshot("sqlserver");
        schema.UserTypes["ssn"] = new UserTypeSchema { Name = "ssn", Kind = UserTypeKind.Alias, UnderlyingDbType = "varchar", MaxLength = 11 };
        return schema;
    }

    [Fact]
    public void Sql_server_alias_column_resolves_to_the_underlying_type()
    {
        var effective = Apply(SqlServerWithSsn(), "ALTER TABLE people ADD tax_id ssn NOT NULL;");

        var taxId = effective.Tables["people"].Columns["tax_id"];
        Assert.Equal("varchar", taxId.DbType);
        Assert.Equal("ssn", taxId.ResolvedFromUserType);
        Assert.Equal(11, taxId.MaxLength);
        Assert.False(taxId.IsUnicode);
        Assert.False(taxId.IsNullable);
        Assert.Equal("string", DialectMapper.MapDbTypeToCSharp(taxId.DbType, taxId.IsNullable, taxId.MaxLength, "sqlserver"));
    }

    [Fact]
    public void Altering_a_resolved_alias_column_to_the_same_alias_reports_only_nullability()
    {
        var snapshot = SqlServerWithSsn();
        snapshot.Tables["people"].Columns["tax_id"] = new ColumnSchema
        {
            Name = "tax_id", DbType = "varchar", MaxLength = 11, IsUnicode = false, IsNullable = false, ResolvedFromUserType = "ssn"
        };

        var effective = Apply(snapshot, "ALTER TABLE people ALTER COLUMN tax_id ssn NULL;");

        var change = Assert.Single(Assert.Single(StructuralSchemaDiff.Compute(snapshot, effective).ModifiedTables).ModifiedColumns);
        Assert.Equal(new[] { ColumnChangeKind.Nullability }, change.Kinds);
    }

    [Fact]
    public void Postgres_domain_column_resolves_and_keeps_its_own_facets()
    {
        var snapshot = Snapshot("postgres");
        snapshot.UserTypes["price"] = new UserTypeSchema { Name = "price", Kind = UserTypeKind.Domain, UnderlyingDbType = "numeric", Precision = 10, Scale = 2 };

        var amount = Apply(snapshot, "ALTER TABLE people ADD COLUMN amount price;").Tables["people"].Columns["amount"];

        Assert.Equal("numeric", amount.DbType);
        Assert.Equal("price", amount.ResolvedFromUserType);
        Assert.Equal(10, amount.Precision);
        Assert.Equal(2, amount.Scale);
    }

    [Fact]
    public void Postgres_enum_column_is_tagged_with_its_enum()
    {
        var snapshot = Snapshot("postgres");
        snapshot.Enums["mood"] = new EnumSchema { Name = "mood", Members = { new EnumMember { Value = "happy", CSharpName = "Happy" } } };

        var mood = Apply(snapshot, "ALTER TABLE people ADD COLUMN mood mood NOT NULL;").Tables["people"].Columns["mood"];

        Assert.Equal("mood", mood.DbType);
        Assert.Equal("mood", mood.EnumName);
        Assert.Null(mood.ResolvedFromUserType);
    }

    [Fact]
    public void Postgres_enum_column_in_a_new_table_is_tagged()
    {
        var snapshot = Snapshot("postgres");
        snapshot.Enums["mood"] = new EnumSchema { Name = "mood", Members = { new EnumMember { Value = "happy", CSharpName = "Happy" } } };

        var effective = Apply(snapshot, "CREATE TABLE moods (id int primary key, current mood);");

        Assert.Equal("mood", effective.Tables["moods"].Columns["current"].EnumName);
    }

    [Fact]
    public void Composite_and_table_types_are_not_resolved()
    {
        var snapshot = Snapshot("postgres");
        snapshot.UserTypes["address"] = new UserTypeSchema { Name = "address", Kind = UserTypeKind.Composite };

        var home = Apply(snapshot, "ALTER TABLE people ADD COLUMN home address;").Tables["people"].Columns["home"];

        Assert.Equal("address", home.DbType);
        Assert.Null(home.ResolvedFromUserType);
    }

    [Fact]
    public void Mysql_columns_are_never_tagged_from_the_enums_dictionary()
    {
        var snapshot = Snapshot("mysql");
        snapshot.Enums["mood"] = new EnumSchema { Name = "mood" };

        var mood = Apply(snapshot, "ALTER TABLE people ADD mood mood NULL;").Tables["people"].Columns["mood"];

        Assert.Null(mood.EnumName);
    }

    [Fact]
    public void Plain_columns_are_untouched()
    {
        var snapshot = SqlServerWithSsn();

        var name = Apply(snapshot, "ALTER TABLE people ADD name nvarchar(40) NULL;").Tables["people"].Columns["name"];

        Assert.Equal("nvarchar", name.DbType);
        Assert.Null(name.ResolvedFromUserType);
        Assert.Null(name.EnumName);
        Assert.Equal(40, name.MaxLength);
    }
}
