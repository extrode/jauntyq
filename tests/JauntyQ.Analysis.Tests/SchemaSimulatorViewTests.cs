using JauntyQ.Analysis;
using JauntyQ.Analysis.Migrations;
using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Analysis.Tests;

/// <summary>
/// Spec 015 followup. <see cref="SchemaSimulator.Apply"/> rebuilds every
/// TableSchema field by field — it is the only place in the codebase that
/// does — and 015 widened all four extractors without touching it. The clone
/// therefore dropped IsView, so every view came out of a migration simulation
/// as an ordinary base table: IsInsertable (derived as !IsView) flipped back
/// to true, and the JNT2021 write refusal 015 added would not fire against a
/// simulated schema.
///
/// The failure mode is the quiet one. Migration impact analysis would report
/// a write to a view as a perfectly ordinary write, so the check that exists
/// precisely to catch it never runs on the schema the consumer is asking
/// about.
/// </summary>
public class SchemaSimulatorViewTests
{
    private static DatabaseSchema BuildSnapshot()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };

        schema.Tables["inbound_messages"] = new TableSchema
        {
            Name = "inbound_messages",
            Columns =
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true },
                ["subject"] = new ColumnSchema { Name = "subject", DbType = "varchar" }
            }
        };

        schema.Tables["v_message_outcomes"] = new TableSchema
        {
            Name = "v_message_outcomes",
            IsView = true,
            Columns =
            {
                ["message_id"] = new ColumnSchema { Name = "message_id", DbType = "int" },
                ["outcome"] = new ColumnSchema { Name = "outcome", DbType = "varchar" }
            }
        };

        return schema;
    }

    /// <summary>
    /// No migrations at all: the identity case isolates the clone from every
    /// statement handler, so a failure here is unambiguously the copy.
    /// </summary>
    [Fact]
    public void ApplyingNoMigrations_PreservesIsView()
    {
        var errors = new List<AnalysisDiagnostic>();

        var result = SchemaSimulator.Apply(
            BuildSnapshot(),
            new List<(string, List<MigrationStatement>)>(),
            errors);

        Assert.True(result.Tables["v_message_outcomes"].IsView);
        Assert.False(result.Tables["v_message_outcomes"].IsInsertable);
    }

    [Fact]
    public void ApplyingNoMigrations_LeavesABaseTableWritable()
    {
        var errors = new List<AnalysisDiagnostic>();

        var result = SchemaSimulator.Apply(
            BuildSnapshot(),
            new List<(string, List<MigrationStatement>)>(),
            errors);

        Assert.False(result.Tables["inbound_messages"].IsView);
        Assert.True(result.Tables["inbound_messages"].IsInsertable);
    }
}
