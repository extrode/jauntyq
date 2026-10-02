using System.Collections.Generic;
using System.Linq;
using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaSimulatorInlineReferencesTests
{
    private static DatabaseSchema Snapshot(string dialect)
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        var customers = new TableSchema { Name = "customers" };
        customers.Columns["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true };
        schema.Tables["customers"] = customers;
        return schema;
    }

    private static (DatabaseSchema Effective, List<AnalysisDiagnostic> Errors) Apply(DatabaseSchema snapshot, string sql)
    {
        var errors = new List<AnalysisDiagnostic>();
        var effective = SchemaSimulator.Apply(snapshot, new[] { ("V2__m.sql", MigrationParser.Parse(sql)) }, errors);
        return (effective, errors);
    }

    private static string Show(ForeignKeySchema fk) => $"{fk.FromTable}.{fk.FromColumn}->{fk.ToTable}.{fk.ToColumn}";

    [Theory]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    [InlineData("sqlite")]
    public void Create_table_captures_an_inline_reference(string dialect)
    {
        var (effective, errors) = Apply(Snapshot(dialect), "CREATE TABLE orders (id int primary key, customer_id int NOT NULL REFERENCES customers(id));");

        Assert.Empty(errors);
        Assert.Equal("orders.customer_id->customers.id", Show(Assert.Single(effective.ForeignKeys)));
        Assert.False(effective.Tables["orders"].Columns["customer_id"].IsNullable);
    }

    [Fact]
    public void Add_column_captures_an_inline_reference()
    {
        var (effective, errors) = Apply(Snapshot("postgres"), "ALTER TABLE customers ADD COLUMN referrer_id int REFERENCES customers (id);");

        Assert.Empty(errors);
        Assert.Equal("customers.referrer_id->customers.id", Show(Assert.Single(effective.ForeignKeys)));
    }

    [Fact]
    public void A_reference_without_a_column_targets_the_primary_key()
    {
        var (effective, errors) = Apply(Snapshot("postgres"), "CREATE TABLE orders (id int primary key, customer_id int REFERENCES public.customers);");

        Assert.Empty(errors);
        Assert.Equal("orders.customer_id->customers.id", Show(Assert.Single(effective.ForeignKeys)));
    }

    [Fact]
    public void A_self_reference_without_a_column_targets_the_new_tables_key()
    {
        var (effective, errors) = Apply(Snapshot("postgres"), "CREATE TABLE nodes (id int primary key, parent_id int REFERENCES nodes);");

        Assert.Empty(errors);
        Assert.Equal("nodes.parent_id->nodes.id", Show(Assert.Single(effective.ForeignKeys)));
    }

    [Fact]
    public void An_unresolvable_target_key_is_reported_not_dropped()
    {
        var (effective, errors) = Apply(Snapshot("postgres"), "CREATE TABLE orders (id int primary key, warehouse_id int REFERENCES warehouses);");

        Assert.Empty(effective.ForeignKeys);
        var error = Assert.Single(errors);
        Assert.Equal("JNT9001", error.Code);
        Assert.Contains("warehouses", error.Message);
    }

    [Theory]
    [InlineData("ON DELETE SET NULL")]
    [InlineData("ON DELETE SET DEFAULT ON UPDATE CASCADE")]
    [InlineData("ON UPDATE NO ACTION ON DELETE RESTRICT")]
    [InlineData("ON UPDATE SET NULL")]
    public void Referential_actions_do_not_change_nullability(string actions)
    {
        var (effective, _) = Apply(Snapshot("postgres"), $"CREATE TABLE orders (id int primary key, customer_id int NOT NULL REFERENCES customers(id) {actions});");

        Assert.False(effective.Tables["orders"].Columns["customer_id"].IsNullable);
        Assert.Single(effective.ForeignKeys);
    }

    [Fact]
    public void Flags_after_the_reference_still_apply()
    {
        var (effective, _) = Apply(Snapshot("postgres"), "CREATE TABLE orders (id int primary key, customer_id int REFERENCES customers(id) ON DELETE CASCADE NOT NULL);");

        Assert.False(effective.Tables["orders"].Columns["customer_id"].IsNullable);
    }

    [Fact]
    public void Mysql_ignores_inline_references_like_the_server_does()
    {
        var (effective, errors) = Apply(Snapshot("mysql"), "CREATE TABLE orders (id int primary key, customer_id int REFERENCES customers(id));");

        Assert.Empty(errors);
        Assert.Empty(effective.ForeignKeys);
        Assert.True(effective.Tables.ContainsKey("orders"));
    }

    [Fact]
    public void Parser_records_the_reference_on_the_statement()
    {
        var stmt = Assert.Single(MigrationParser.Parse("CREATE TABLE orders (id int primary key, customer_id int CONSTRAINT fk_c REFERENCES [dbo].[customers] ([id]));"));

        var fk = Assert.Single(stmt.ForeignKeys);
        Assert.Equal("customer_id", fk.FromColumn);
        Assert.Equal("customers", fk.ToTable);
        Assert.Equal("id", fk.ToColumn);
    }
}
