using Extrode.JauntyQ.Analysis.Migrations;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class MigrationParserParenlessComputedTests
{
    [Theory]
    [InlineData("total as qty * price", true)]
    [InlineData("total as qty * price persisted", true)]
    [InlineData("total as qty * price persisted not null", false)]
    [InlineData("total as qty * price persisted null", true)]
    [InlineData("total as isnull(qty, 0) * price persisted not null", false)]
    [InlineData("total as case when qty is not null then qty * price end", true)]
    [InlineData("total as case when qty is not null then qty * price end persisted", true)]
    [InlineData("total as (qty) * price persisted not null", false)]
    [InlineData("total as (qty) + case when note is not null then 1 else 0 end", true)]
    [InlineData("total as (qty) + case when note is not null then 1 else 0 end persisted", true)]
    [InlineData("total as (qty) + (case when note is not null then 1 end) persisted not null", false)]
    [InlineData("total as (persisted + 1) persisted not null", false)]
    public void CreateTable_ParenlessComputedColumn_EntersTheSchema(string column, bool nullable)
    {
        var statements = MigrationParser.Parse(
            "create table orders (id int primary key, qty int, price decimal(10,2), " + column + ", note int)");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.CreateTable, stmt.Kind);
        Assert.Equal(new[] { "id", "qty", "price", "total", "note" }, stmt.Columns.ConvertAll(c => c.Name));
        var total = stmt.Columns[3];
        Assert.True(total.IsComputed);
        Assert.Equal(string.Empty, total.DbType);
        Assert.Equal(nullable, total.IsNullable);
    }

    [Fact]
    public void AlterTableAdd_ParenlessComputedColumn_IsAnAddColumn()
    {
        var statements = MigrationParser.Parse("alter table orders add total as qty * price");

        var stmt = Assert.Single(statements);
        Assert.Equal(MigrationStatementKind.AddColumn, stmt.Kind);
        var total = Assert.Single(stmt.Columns);
        Assert.Equal("total", total.Name);
        Assert.True(total.IsComputed);
    }

    [Fact]
    public void AsWithNothingAfterIt_IsStillUnrecognized()
    {
        var statements = MigrationParser.Parse("create table orders (id int primary key, total as)");

        var stmt = Assert.Single(statements);
        Assert.DoesNotContain(stmt.Columns, c => c.Name == "total");
    }
}
