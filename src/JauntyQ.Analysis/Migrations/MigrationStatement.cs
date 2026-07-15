using JauntyQ.Schema;

namespace JauntyQ.Analysis.Migrations;

public enum MigrationStatementKind
{
    CreateTable,
    DropTable,
    AddColumn,
    DropColumn,
    AlterColumn,

    /// <summary>ALTER TABLE t ADD [CONSTRAINT name] PRIMARY KEY (a, b, ...):
    /// marks existing columns primary-key/non-nullable without redefining
    /// them. See <see cref="MigrationStatement.ColumnNames"/>.</summary>
    AddPrimaryKey,

    /// <summary>Statement that cannot change the table/column model (indexes,
    /// transactions, SET options): skipped without a diagnostic.</summary>
    Ignored,

    /// <summary>Statement the simulator cannot model (renames, arbitrary DDL):
    /// reported as JNT9001 so the developer knows the effective schema may
    /// be incomplete.</summary>
    Unsupported
}

/// <summary>
/// One parsed migration statement. Column definitions reuse ColumnSchema so
/// the simulator can splice them straight into the effective schema.
/// </summary>
public class MigrationStatement
{
    public MigrationStatementKind Kind { get; set; }
    public string TableName { get; set; } = string.Empty;

    /// <summary>CreateTable: full column list. AddColumn/AlterColumn: the affected column(s).</summary>
    public List<ColumnSchema> Columns { get; } = new();

    /// <summary>DropColumn: the column names to remove. AddPrimaryKey: the
    /// existing column(s) to mark primary-key/non-nullable.</summary>
    public List<string> ColumnNames { get; } = new();

    /// <summary>DROP TABLE IF EXISTS: a missing table is not an error.</summary>
    public bool IfExists { get; set; }

    /// <summary>Original SQL text, for diagnostics.</summary>
    public string RawText { get; set; } = string.Empty;
}
