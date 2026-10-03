using System.Collections.Generic;
using System.Linq;
using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaSimulatorPostgresTableNameTests
{
    private static DatabaseSchema Snapshot(string dialect, string tableName)
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        var table = new TableSchema { Name = tableName };
        table.Columns["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true };
        schema.Tables[tableName] = table;
        return schema;
    }

    private static (DatabaseSchema Effective, List<AnalysisDiagnostic> Errors) Apply(DatabaseSchema snapshot, string sql)
    {
        var errors = new List<AnalysisDiagnostic>();
        var effective = SchemaSimulator.Apply(snapshot, new[] { ("V2__m.sql", MigrationParser.Parse(sql)) }, errors);
        return (effective, errors);
    }

    [Theory]
    [InlineData("Customers", "CREATE TABLE customers (id int PRIMARY KEY);", "customers")]
    [InlineData("Customers", "CREATE TABLE CUSTOMERS (id int PRIMARY KEY);", "customers")]
    [InlineData("customers", "CREATE TABLE \"Customers\" (id int PRIMARY KEY);", "Customers")]
    public void A_name_postgres_keeps_apart_is_a_new_table(string existing, string sql, string created)
    {
        var (effective, errors) = Apply(Snapshot("postgres", existing), sql);

        Assert.Empty(errors);
        Assert.Equal(new[] { existing, created }.OrderBy(n => n, System.StringComparer.Ordinal),
            effective.Tables.Keys.OrderBy(n => n, System.StringComparer.Ordinal));
        Assert.Equal(created, effective.Tables[created].Name);
    }

    [Theory]
    [InlineData("customers", "CREATE TABLE Customers (id int PRIMARY KEY);")]
    [InlineData("Customers", "CREATE TABLE \"Customers\" (id int PRIMARY KEY);")]
    public void A_name_postgres_resolves_to_the_existing_table_already_exists(string existing, string sql)
    {
        var (_, errors) = Apply(Snapshot("postgres", existing), sql);

        Assert.Contains(errors, e => e.Code == "JNT9002" && e.Message.Contains("already exists"));
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void Other_dialects_still_match_names_case_insensitively(string dialect)
    {
        var (_, errors) = Apply(Snapshot(dialect, "Customers"), "CREATE TABLE customers (id int PRIMARY KEY);");

        Assert.Contains(errors, e => e.Code == "JNT9002" && e.Message.Contains("already exists"));
    }

    [Theory]
    [InlineData("\"Customers\"", true)]
    [InlineData("`Customers`", true)]
    [InlineData("Customers", false)]
    [InlineData("[Customers]", false)]
    public void The_tokenizer_marks_quoted_identifiers(string identifier, bool quoted)
    {
        Assert.Equal(quoted, SqlTokenizer.Tokenize("select 1 from " + identifier)[3].IsQuoted);
    }
}
