using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaSimulatorInvalidOperationTests
{
    private const string FileName = "0001_change.sql";

    private static DatabaseSchema Snapshot(string dialect = "postgres")
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        schema.Tables["orders"] = new TableSchema
        {
            Name = "orders",
            Columns =
            {
                ["order_id"] = new ColumnSchema { Name = "order_id", DbType = "int", IsPrimaryKey = true },
                ["total"] = new ColumnSchema { Name = "total", DbType = "decimal", IsNullable = false }
            }
        };
        return schema;
    }

    private static (DatabaseSchema Result, List<AnalysisDiagnostic> Errors) Simulate(
        string sql, string dialect = "postgres")
    {
        var errors = new List<AnalysisDiagnostic>();
        var result = SchemaSimulator.Apply(
            Snapshot(dialect),
            new List<(string, List<MigrationStatement>)> { (FileName, MigrationParser.Parse(sql)) },
            errors);
        return (result, errors);
    }

    private static AnalysisDiagnostic SingleError(List<AnalysisDiagnostic> errors)
    {
        var error = Assert.Single(errors);
        Assert.Equal("JNT9002", error.Code);
        return error;
    }

    [Fact]
    public void CreateTable_DeclaringTheSameColumnTwice_KeepsTheFirstAndReportsTheDuplicate()
    {
        var (result, errors) = Simulate("create table widgets (id int primary key, id varchar(10))");

        var error = SingleError(errors);
        Assert.Contains("duplicate column 'id' in create table 'widgets'", error.Message);
        Assert.Contains(FileName, error.Message);

        var widgets = result.Tables["widgets"];
        Assert.Single(widgets.Columns);
        Assert.Equal("int", widgets.Columns["id"].DbType);
    }

    [Fact]
    public void CreateTable_OfATableThatAlreadyExists_IsRefusedAndLeavesTheSnapshotColumns()
    {
        var (result, errors) = Simulate("create table orders (something_else int)");

        var error = SingleError(errors);
        Assert.Contains("table 'orders' already exists", error.Message);

        Assert.Equal(2, result.Tables["orders"].Columns.Count);
        Assert.False(result.Tables["orders"].Columns.ContainsKey("something_else"));
    }

    [Fact]
    public void AddColumn_ToATableThatDoesNotExist_IsReportedAndCreatesNothing()
    {
        var (result, errors) = Simulate("alter table nosuch add note varchar(20)");

        var error = SingleError(errors);
        Assert.Contains("cannot add column to 'nosuch'", error.Message);
        Assert.Contains("table does not exist", error.Message);
        Assert.False(result.Tables.ContainsKey("nosuch"));
    }

    [Fact]
    public void AddColumn_ThatAlreadyExists_IsRefusedAndKeepsTheExistingType()
    {
        var (result, errors) = Simulate("alter table orders add total varchar(9)");

        var error = SingleError(errors);
        Assert.Contains("column 'orders.total' already exists", error.Message);
        Assert.Equal("decimal", result.Tables["orders"].Columns["total"].DbType);
    }

    [Fact]
    public void AddColumn_ContinuesPastADuplicateToTheNextColumnInTheSameStatement()
    {
        var (result, errors) = Simulate("alter table orders add total varchar(9), note varchar(20)");

        SingleError(errors);
        Assert.True(result.Tables["orders"].Columns.ContainsKey("note"));
    }

    [Fact]
    public void DropColumn_FromATableThatDoesNotExist_IsReported()
    {
        var (_, errors) = Simulate("alter table nosuch drop column note");

        var error = SingleError(errors);
        Assert.Contains("cannot drop column from 'nosuch'", error.Message);
        Assert.Contains("table does not exist", error.Message);
    }

    [Fact]
    public void DropColumn_ThatDoesNotExist_IsReportedAndLeavesTheOtherColumnsIntact()
    {
        var (result, errors) = Simulate("alter table orders drop column nosuch");

        var error = SingleError(errors);
        Assert.Contains("cannot drop column 'orders.nosuch'", error.Message);
        Assert.Equal(2, result.Tables["orders"].Columns.Count);
    }

    [Fact]
    public void AlterColumn_OnATableThatDoesNotExist_IsReported()
    {
        var (_, errors) = Simulate("alter table nosuch alter column note type varchar(20)");

        var error = SingleError(errors);
        Assert.Contains("cannot alter column on 'nosuch'", error.Message);
        Assert.Contains("table does not exist", error.Message);
    }

    [Fact]
    public void AlterColumn_ThatDoesNotExist_IsReportedAndAddsNoPhantomColumn()
    {
        var (result, errors) = Simulate("alter table orders alter column nosuch type varchar(20)");

        var error = SingleError(errors);
        Assert.Contains("cannot alter column 'orders.nosuch'", error.Message);
        Assert.Equal(2, result.Tables["orders"].Columns.Count);
        Assert.False(result.Tables["orders"].Columns.ContainsKey("nosuch"));
    }

    [Fact]
    public void AlterColumnNullability_OnATableThatDoesNotExist_IsReported()
    {
        var (_, errors) = Simulate("alter table nosuch alter column note drop not null");

        var error = SingleError(errors);
        Assert.Contains("cannot alter column on 'nosuch'", error.Message);
        Assert.Contains("table does not exist", error.Message);
    }

    [Fact]
    public void AlterColumnNullability_OfAColumnThatDoesNotExist_IsReported()
    {
        var (result, errors) = Simulate("alter table orders alter column nosuch drop not null");

        var error = SingleError(errors);
        Assert.Contains("cannot alter column 'orders.nosuch'", error.Message);
        Assert.False(result.Tables["orders"].Columns.ContainsKey("nosuch"));
    }

    [Fact]
    public void AlterColumnNullability_OfARealColumn_StillApplies()
    {
        var (result, errors) = Simulate("alter table orders alter column total drop not null");

        Assert.Empty(errors);
        Assert.True(result.Tables["orders"].Columns["total"].IsNullable);
    }

    [Fact]
    public void AddPrimaryKey_OnATableThatDoesNotExist_IsReported()
    {
        var (_, errors) = Simulate("alter table nosuch add primary key (note)");

        var error = SingleError(errors);
        Assert.Contains("cannot add a primary key to 'nosuch'", error.Message);
    }

    [Fact]
    public void AddPrimaryKey_OnAColumnThatDoesNotExist_IsReported()
    {
        var (result, errors) = Simulate("alter table orders add primary key (nosuch)");

        var error = SingleError(errors);
        Assert.Contains("cannot add a primary key on 'orders.nosuch'", error.Message);
        Assert.True(result.Tables["orders"].Columns["order_id"].IsPrimaryKey);
    }

    [Fact]
    public void DropTable_ThatDoesNotExist_IsReportedUnlessIfExistsWasGiven()
    {
        var (_, without) = Simulate("drop table nosuch");
        var (_, with) = Simulate("drop table if exists nosuch");

        var error = SingleError(without);
        Assert.Contains("cannot drop table 'nosuch'", error.Message);
        Assert.Empty(with);
    }

    [Fact]
    public void EveryInvalidOperationIsAnErrorAndSimulationStillReachesLaterStatements()
    {
        var errors = new List<AnalysisDiagnostic>();
        var result = SchemaSimulator.Apply(
            Snapshot(),
            new List<(string, List<MigrationStatement>)>
            {
                (FileName, MigrationParser.Parse(
                    "alter table nosuch add a int; alter table orders add note varchar(20)"))
            },
            errors);

        var error = SingleError(errors);
        Assert.Equal(AnalysisSeverity.Error, error.Severity);
        Assert.True(result.Tables["orders"].Columns.ContainsKey("note"));
    }
}
