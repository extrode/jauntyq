using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

/// <summary>
/// AUD-R1-03. <see cref="SchemaSimulator.Apply"/> clones every ColumnSchema
/// field by field, the same shape of bug <see cref="SchemaSimulatorViewTests"/>
/// covers for IsView: ResolvedFromUserType was added to ColumnSchema after
/// the clone was last written and never propagated into it, so a migration
/// simulation silently lost a column's DOMAIN/alias provenance.
/// </summary>
public class SchemaSimulatorUserTypeTests
{
    private static DatabaseSchema BuildSnapshot()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };

        schema.Tables["accounts"] = new TableSchema
        {
            Name = "accounts",
            Columns =
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true },
                ["balance"] = new ColumnSchema
                {
                    Name = "balance",
                    DbType = "numeric",
                    Precision = 12,
                    Scale = 2,
                    ResolvedFromUserType = "money_amount"
                }
            }
        };

        return schema;
    }

    [Fact]
    public void ApplyingNoMigrations_PreservesResolvedFromUserType()
    {
        var errors = new List<AnalysisDiagnostic>();

        var result = SchemaSimulator.Apply(
            BuildSnapshot(),
            new List<(string, List<MigrationStatement>)>(),
            errors);

        Assert.Equal("money_amount", result.Tables["accounts"].Columns["balance"].ResolvedFromUserType);
    }

    [Fact]
    public void ApplyingNoMigrations_LeavesOrdinaryColumnWithoutUserType()
    {
        var errors = new List<AnalysisDiagnostic>();

        var result = SchemaSimulator.Apply(
            BuildSnapshot(),
            new List<(string, List<MigrationStatement>)>(),
            errors);

        Assert.Null(result.Tables["accounts"].Columns["id"].ResolvedFromUserType);
    }
}
