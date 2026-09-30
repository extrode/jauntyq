using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

/// <summary>
/// Direct coverage for SchemaSimulator gaps not exercised by
/// SchemaSimulatorInvalidOperationTests/UserTypeTests/ViewTests: the
/// Unsupported/unrecognized-kind diagnostic text, ForeignKeys cascade on
/// DropTable/DropColumn, index survival when a dropped column is only
/// referenced by an all-expression index, RemoveTable's exact-match return
/// path, Finalize's rowversion nullability flip, and Truncate's boundary.
/// </summary>
[Trait("Category", "AuditRegression")]
public class SchemaSimulatorMutationCoverageTests
{
    private const string FileName = "0001_change.sql";

    private static (DatabaseSchema Result, List<AnalysisDiagnostic> Errors) Apply(
        DatabaseSchema snapshot, params MigrationStatement[] statements)
    {
        var errors = new List<AnalysisDiagnostic>();
        var result = SchemaSimulator.Apply(
            snapshot,
            new List<(string, List<MigrationStatement>)> { (FileName, new List<MigrationStatement>(statements)) },
            errors);
        return (result, errors);
    }

    private static (DatabaseSchema Result, List<AnalysisDiagnostic> Errors) Simulate(DatabaseSchema snapshot, string sql)
    {
        var errors = new List<AnalysisDiagnostic>();
        var result = SchemaSimulator.Apply(
            snapshot,
            new List<(string, List<MigrationStatement>)> { (FileName, MigrationParser.Parse(sql)) },
            errors);
        return (result, errors);
    }

    private static DatabaseSchema OrdersSchema(string dialect = "postgres")
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        schema.Tables["orders"] = new TableSchema
        {
            Name = "orders",
            Columns =
            {
                ["order_id"] = new ColumnSchema { Name = "order_id", DbType = "int", IsPrimaryKey = true },
                ["total"] = new ColumnSchema { Name = "total", DbType = "decimal" }
            }
        };
        return schema;
    }

    [Fact]
    public void Unsupported_ProducesExactWarningText()
    {
        var stmt = new MigrationStatement { Kind = MigrationStatementKind.Unsupported, RawText = "rename table orders to legacy_orders" };

        var (_, errors) = Apply(OrdersSchema(), stmt);

        var error = Assert.Single(errors);
        Assert.Equal("JNT9001", error.Code);
        Assert.Equal(AnalysisSeverity.Warning, error.Severity);
        Assert.Equal(
            $"{FileName}: statement not simulated (effective schema may be incomplete): rename table orders to legacy_orders",
            error.Message);
    }

    [Fact]
    public void UnrecognizedKind_FallsToDefaultArm_ProducesSameWarningShapeAsUnsupported()
    {
        var stmt = new MigrationStatement { Kind = (MigrationStatementKind)999, RawText = "create index ix on orders (total)" };

        var (_, errors) = Apply(OrdersSchema(), stmt);

        var error = Assert.Single(errors);
        Assert.Equal("JNT9001", error.Code);
        Assert.Equal(AnalysisSeverity.Warning, error.Severity);
        Assert.Equal(
            $"{FileName}: statement not simulated (effective schema may be incomplete): create index ix on orders (total)",
            error.Message);
    }

    [Fact]
    public void Truncate_TextAtExactly80Chars_IsNotTruncated()
    {
        var text = new string('a', 80);
        var stmt = new MigrationStatement { Kind = MigrationStatementKind.Unsupported, RawText = text };

        var (_, errors) = Apply(OrdersSchema(), stmt);

        Assert.Contains(text, Assert.Single(errors).Message);
    }

    [Fact]
    public void Truncate_TextOver80Chars_TruncatedToFirst77PlusEllipsis()
    {
        var text = new string('a', 100);
        var stmt = new MigrationStatement { Kind = MigrationStatementKind.Unsupported, RawText = text };

        var (_, errors) = Apply(OrdersSchema(), stmt);

        var expected = new string('a', 77) + "...";
        Assert.Contains(expected, Assert.Single(errors).Message);
        Assert.DoesNotContain(text, Assert.Single(errors).Message);
    }

    [Fact]
    public void CreateTable_ResultingTableHasItsNamePropertySet()
    {
        var (result, _) = Simulate(OrdersSchema(), "create table widgets (id int primary key)");

        Assert.Equal("widgets", result.Tables["widgets"].Name);
    }

    [Fact]
    public void DropTable_RemovesForeignKeysWhereDroppedTableIsTheFromTable()
    {
        var schema = OrdersSchema();
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "customer_id", ToTable = "customers", ToColumn = "id" });

        var (result, _) = Simulate(schema, "drop table orders");

        Assert.Empty(result.ForeignKeys);
    }

    [Fact]
    public void DropTable_RemovesForeignKeysWhereDroppedTableIsTheToTable()
    {
        var schema = OrdersSchema();
        schema.Tables["customers"] = new TableSchema { Name = "customers", Columns = { ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true } } };
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "customer_id", ToTable = "customers", ToColumn = "id" });

        var (result, _) = Simulate(schema, "drop table customers");

        Assert.Empty(result.ForeignKeys);
    }

    [Fact]
    public void DropTable_LeavesForeignKeysThatReferenceNeitherSide()
    {
        var schema = OrdersSchema();
        schema.Tables["invoices"] = new TableSchema { Name = "invoices", Columns = { ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true } } };
        schema.Tables["legacy"] = new TableSchema { Name = "legacy", Columns = { ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true } } };
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "invoices", FromColumn = "order_id", ToTable = "orders", ToColumn = "order_id" });

        var (result, _) = Simulate(schema, "drop table legacy");

        Assert.Single(result.ForeignKeys);
    }

    [Fact]
    public void DropColumn_ContinuesPastAMissingNameToTheNextColumnInTheSameStatement()
    {
        var schema = OrdersSchema();

        var (result, errors) = Simulate(schema, "alter table orders drop column nosuch, total");

        Assert.Single(errors);
        Assert.False(result.Tables["orders"].Columns.ContainsKey("total"));
    }

    [Fact]
    public void DropColumn_RemovesForeignKeyWhoseFromColumnWasDropped()
    {
        var schema = OrdersSchema();
        schema.Tables["orders"].Columns["customer_id"] = new ColumnSchema { Name = "customer_id", DbType = "int" };
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "customer_id", ToTable = "customers", ToColumn = "id" });

        var (result, _) = Simulate(schema, "alter table orders drop column customer_id");

        Assert.Empty(result.ForeignKeys);
    }

    [Fact]
    public void DropColumn_LeavesForeignKeyWhenTableMatchesButColumnDoesNot()
    {
        var schema = OrdersSchema();
        schema.Tables["orders"].Columns["customer_id"] = new ColumnSchema { Name = "customer_id", DbType = "int" };
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "customer_id", ToTable = "customers", ToColumn = "id" });

        var (result, _) = Simulate(schema, "alter table orders drop column total");

        Assert.Single(result.ForeignKeys);
    }

    [Fact]
    public void DropColumn_LeavesForeignKeyWhenColumnMatchesButFromTableDoesNot()
    {
        var schema = OrdersSchema();
        schema.Tables["invoices"] = new TableSchema
        {
            Name = "invoices",
            Columns = { ["customer_id"] = new ColumnSchema { Name = "customer_id", DbType = "int" } }
        };
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "invoices", FromColumn = "customer_id", ToTable = "customers", ToColumn = "id" });
        schema.Tables["orders"].Columns["customer_id"] = new ColumnSchema { Name = "customer_id", DbType = "int" };

        var (result, _) = Simulate(schema, "alter table orders drop column customer_id");

        Assert.Single(result.ForeignKeys);
    }

    [Fact]
    public void AlterColumnNullability_WithNoColumnNameGiven_ReportsAgainstAnEmptyName()
    {
        // MigrationParser always adds exactly one ColumnNames entry before
        // producing this Kind, so this defensive fallback is unreachable via
        // the public parser -- direct construction, same technique already
        // used above for the Unsupported/default-arm cases.
        var stmt = new MigrationStatement { Kind = MigrationStatementKind.AlterColumnNullability, TableName = "orders", NullableAfter = true };

        var (_, errors) = Apply(OrdersSchema(), stmt);

        var error = Assert.Single(errors);
        Assert.Equal("JNT9002", error.Code);
        Assert.Equal("0001_change.sql: cannot alter column 'orders.': it does not exist in the effective schema.", error.Message);
    }

    [Fact]
    public void DropColumn_AllExpressionIndex_SurvivesUntouched_EvenThoughItHasZeroKeyColumns()
    {
        var schema = OrdersSchema();
        schema.Tables["orders"].Indexes.Add(new IndexSchema
        {
            Name = "ux_expr",
            Columns = new List<string>(),
            IsUnique = true,
            HasExpressionKeyPart = true
        });

        var (result, _) = Simulate(schema, "alter table orders drop column total");

        var index = Assert.Single(result.Tables["orders"].Indexes);
        Assert.Equal("ux_expr", index.Name);
        Assert.True(index.HasExpressionKeyPart);
    }

    [Fact]
    public void DropTable_ExactCaseMatch_Succeeds_NoError()
    {
        var (result, errors) = Simulate(OrdersSchema(), "drop table orders");

        Assert.Empty(errors);
        Assert.False(result.Tables.ContainsKey("orders"));
    }

    [Fact]
    public void Finalize_RowVersionColumn_IsForcedNonNullable()
    {
        var (result, _) = Simulate(OrdersSchema(), "create table widgets (id int primary key, ver rowversion)");

        var ver = result.Tables["widgets"].Columns["ver"];
        Assert.True(ver.IsRowVersion);
        Assert.False(ver.IsNullable);
    }

    [Fact]
    public void ApplyingNoMigrations_ClonesProcedureFieldsFaithfully()
    {
        var schema = OrdersSchema();
        var proc = new ProcedureSchema { Name = "GetOrder" };
        proc.Params.Add(new ProcedureParam { Name = "@id", DbType = "int", Direction = ProcedureParamDirection.In, IsNullable = false });
        proc.Results.Add(new ColumnSchema { Name = "order_id", DbType = "int" });
        schema.Procedures["GetOrder"] = proc;

        var (result, _) = Apply(schema);

        var cloned = result.Procedures["GetOrder"];
        Assert.Equal("GetOrder", cloned.Name);
        var param = Assert.Single(cloned.Params);
        Assert.Equal("@id", param.Name);
        Assert.Equal("int", param.DbType);
        Assert.Equal(ProcedureParamDirection.In, param.Direction);
        var result0 = Assert.Single(cloned.Results);
        Assert.Equal("order_id", result0.Name);
    }

    [Fact]
    public void ApplyingNoMigrations_ClonesEnumFieldsFaithfully()
    {
        var schema = OrdersSchema();
        var en = new EnumSchema { Name = "order_status" };
        en.Members.Add(new EnumMember { Value = "pending", CSharpName = "Pending" });
        schema.Enums["order_status"] = en;

        var (result, _) = Apply(schema);

        var cloned = result.Enums["order_status"];
        Assert.Equal("order_status", cloned.Name);
        var member = Assert.Single(cloned.Members);
        Assert.Equal("pending", member.Value);
        Assert.Equal("Pending", member.CSharpName);
    }
}
