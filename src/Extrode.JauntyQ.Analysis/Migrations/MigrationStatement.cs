using Extrode.JauntyQ.Schema;

namespace Extrode.JauntyQ.Analysis.Migrations;

public enum MigrationStatementKind
{
    CreateTable,
    DropTable,
    AddColumn,
    DropColumn,
    AlterColumn,

    /// <summary>PostgreSQL ALTER TABLE t ALTER COLUMN c SET/DROP NOT NULL:
    /// a nullability-only change, with no type token at all -- distinct from
    /// <see cref="AlterColumn"/>, which redefines the column's full
    /// type/facets. See <see cref="MigrationStatement.NullableAfter"/>.</summary>
    AlterColumnNullability,

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

    /// <summary>AlterColumnNullability: the column's nullability after the
    /// statement (true for DROP NOT NULL, false for SET NOT NULL). The
    /// target column name is ColumnNames[0].</summary>
    public bool NullableAfter { get; set; }

    /// <summary>AlterColumn from PostgreSQL's "ALTER COLUMN c [SET DATA] TYPE t":
    /// only the type and its facets change, so the simulator keeps the
    /// column's existing nullability instead of reading the missing NULL/NOT
    /// NULL clause as nullable. False for SQL Server ALTER COLUMN and MySQL
    /// MODIFY, which redefine the whole column.</summary>
    public bool TypeOnly { get; set; }

    /// <summary>MySQL inline <c>ENUM('a','b')</c> member lists, keyed by
    /// column name, for CreateTable/AddColumn/AlterColumn. Values are
    /// verbatim (a doubled '' is unescaped). The simulator turns each into
    /// the {Table}{Column} <see cref="EnumSchema"/> a live pull captures.</summary>
    public Dictionary<string, List<string>> EnumMembers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>MySQL columns declared with an explicit <c>CHARACTER SET x</c>
    /// or <c>CHARSET x</c>, keyed by column name. The value is whether x is a
    /// Unicode charset (utf8*, utf16*, utf32, ucs2), which is what a live pull
    /// reports as the column's Unicode flag.</summary>
    public Dictionary<string, bool> CharsetIsUnicode { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Inline column-level <c>REFERENCES t [(c)]</c> targets for
    /// CreateTable/AddColumn. FromTable is left empty (the statement's table);
    /// ToColumn is empty when the clause names no column, meaning the
    /// target's primary key. The simulator adds them to the effective schema
    /// for every dialect but MySQL, which parses and ignores the inline form.</summary>
    public List<ForeignKeySchema> ForeignKeys { get; } = new();

    /// <summary>Original SQL text, for diagnostics.</summary>
    public string RawText { get; set; } = string.Empty;
}
