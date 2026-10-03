using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaSimulatorFunctionCloneTests
{
    private static DatabaseSchema BuildSnapshot()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Tables["accounts"] = new TableSchema
        {
            Name = "accounts",
            Columns = { ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true } }
        };
        schema.Functions["calc(int)"] = new FunctionSchema
        {
            Name = "calc",
            Schema = "billing",
            Params =
            {
                new FunctionParam
                {
                    Name = "p_amount", DbType = "numeric", IsNullable = true,
                    MaxLength = 7, Precision = 12, Scale = 2, ResolvedFromUserType = "money_amount"
                }
            },
            Return = new FunctionReturn
            {
                DbType = "varchar", IsNullable = false,
                MaxLength = 40, Precision = 5, Scale = 1, ResolvedFromUserType = "label"
            }
        };
        schema.Functions["calc(text)"] = new FunctionSchema
        {
            Name = "calc",
            Params = { new FunctionParam { Name = "p_text", DbType = "text" } }
        };
        schema.UserTypes["money_amount"] = new UserTypeSchema
        {
            Name = "money_amount",
            Schema = "billing",
            Kind = UserTypeKind.Domain,
            UnderlyingDbType = "numeric",
            IsNullable = false,
            MaxLength = 9,
            Precision = 12,
            Scale = 2
        };
        schema.UserTypes["line_items"] = new UserTypeSchema
        {
            Name = "line_items",
            Kind = UserTypeKind.TableType,
            Members = { new ColumnSchema { Name = "sku", DbType = "nvarchar", MaxLength = 20, IsNullable = false } }
        };
        return schema;
    }

    private static DatabaseSchema Simulate(DatabaseSchema snapshot)
    {
        var migration = MigrationParser.Parse("ALTER TABLE accounts ADD note varchar(10) NULL;");
        var errors = new List<AnalysisDiagnostic>();
        var result = SchemaSimulator.Apply(snapshot, new List<(string, List<MigrationStatement>)> { ("001_note.sql", migration) }, errors);
        Assert.Empty(errors);
        Assert.True(result.Tables["accounts"].Columns.ContainsKey("note"));
        return result;
    }

    [Fact]
    public void PendingMigration_KeepsEveryFunctionUnderItsOverloadKey()
    {
        var result = Simulate(BuildSnapshot());

        Assert.Equal(new[] { "calc(int)", "calc(text)" }, result.Functions.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("p_text", Assert.Single(result.Functions["calc(text)"].Params).Name);
    }

    [Fact]
    public void PendingMigration_KeepsEveryFunctionField()
    {
        var snapshot = BuildSnapshot();
        var fn = Simulate(snapshot).Functions["calc(int)"];

        Assert.NotSame(snapshot.Functions["calc(int)"], fn);
        Assert.Equal("calc", fn.Name);
        Assert.Equal("billing", fn.Schema);

        var p = Assert.Single(fn.Params);
        Assert.NotSame(snapshot.Functions["calc(int)"].Params[0], p);
        Assert.Equal(("p_amount", "numeric", true), (p.Name, p.DbType, p.IsNullable));
        Assert.Equal((7, 12, 2), (p.MaxLength, p.Precision, p.Scale));
        Assert.Equal("money_amount", p.ResolvedFromUserType);

        Assert.NotSame(snapshot.Functions["calc(int)"].Return, fn.Return);
        Assert.Equal(("varchar", false), (fn.Return.DbType, fn.Return.IsNullable));
        Assert.Equal((40, 5, 1), (fn.Return.MaxLength, fn.Return.Precision, fn.Return.Scale));
        Assert.Equal("label", fn.Return.ResolvedFromUserType);
    }

    [Fact]
    public void PendingMigration_KeepsEveryUserTypeField()
    {
        var snapshot = BuildSnapshot();
        var result = Simulate(snapshot);

        Assert.Equal(new[] { "line_items", "money_amount" }, result.UserTypes.Keys.OrderBy(k => k, StringComparer.Ordinal));

        var domain = result.UserTypes["money_amount"];
        Assert.NotSame(snapshot.UserTypes["money_amount"], domain);
        Assert.Equal(("money_amount", "billing", UserTypeKind.Domain), (domain.Name, domain.Schema, domain.Kind));
        Assert.Equal(("numeric", false), (domain.UnderlyingDbType, domain.IsNullable));
        Assert.Equal((9, 12, 2), (domain.MaxLength, domain.Precision, domain.Scale));

        var member = Assert.Single(result.UserTypes["line_items"].Members);
        Assert.NotSame(snapshot.UserTypes["line_items"].Members[0], member);
        Assert.Equal(("sku", "nvarchar", 20, false), (member.Name, member.DbType, member.MaxLength, member.IsNullable));
        Assert.Equal(UserTypeKind.TableType, result.UserTypes["line_items"].Kind);
    }
}
