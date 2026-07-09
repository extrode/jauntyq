using JauntyQ.Schema;

namespace JauntyQ.Analysis.Diff;

/// <summary>
/// The kind of change a migration made to a column that still exists in the
/// effective schema. Drives the RISKY reason text and, for the generator, the
/// JNT9004 message.
/// </summary>
public enum ColumnChangeKind
{
    Type,
    Nullability,
    MaxLength,
    PrecisionScale,
    Unicode,
    PrimaryKey,
    Identity,
    RowVersion
}

/// <summary>
/// One column that exists in both the baseline and the effective schema but
/// changed. <see cref="Baseline"/>/<see cref="Effective"/> carry the before/after
/// facets so callers can render precise reason text ("varchar(100) → varchar(50)").
/// </summary>
public sealed class ColumnChange
{
    public string Column { get; }
    public IReadOnlyList<ColumnChangeKind> Kinds { get; }
    public ColumnSchema Baseline { get; }
    public ColumnSchema Effective { get; }

    public ColumnChange(string column, ColumnSchema baseline, ColumnSchema effective, IReadOnlyList<ColumnChangeKind> kinds)
    {
        Column = column;
        Baseline = baseline;
        Effective = effective;
        Kinds = kinds;
    }
}

/// <summary>One table's added / removed / modified columns (compared by name).</summary>
public sealed class TableDelta
{
    public string Table { get; }
    public IReadOnlyList<string> AddedColumns { get; }
    public IReadOnlyList<string> RemovedColumns { get; }
    public IReadOnlyList<ColumnChange> ModifiedColumns { get; }

    public TableDelta(string table, IReadOnlyList<string> added, IReadOnlyList<string> removed, IReadOnlyList<ColumnChange> modified)
    {
        Table = table;
        AddedColumns = added;
        RemovedColumns = removed;
        ModifiedColumns = modified;
    }
}

/// <summary>
/// A name-based baseline→effective schema difference produced by
/// <see cref="StructuralSchemaDiff"/>. Impact classification is fundamentally a
/// query's referenced objects intersected with this delta, so it exposes O(1)
/// lookups (<see cref="IsTableRemoved"/>, <see cref="IsColumnRemoved"/>,
/// <see cref="FindColumnChange"/>) in addition to the enumerable collections.
/// All name matching is case-insensitive, consistent with the rest of the
/// schema tooling.
/// </summary>
public sealed class SchemaDelta
{
    public IReadOnlyList<string> AddedTables { get; }
    public IReadOnlyList<string> RemovedTables { get; }
    public IReadOnlyList<TableDelta> ModifiedTables { get; }

    private readonly HashSet<string> _removedTables;
    private readonly Dictionary<string, TableDelta> _modifiedByTable;

    public SchemaDelta(
        IReadOnlyList<string> addedTables,
        IReadOnlyList<string> removedTables,
        IReadOnlyList<TableDelta> modifiedTables)
    {
        AddedTables = addedTables;
        RemovedTables = removedTables;
        ModifiedTables = modifiedTables;

        _removedTables = new HashSet<string>(removedTables, StringComparer.OrdinalIgnoreCase);
        _modifiedByTable = new Dictionary<string, TableDelta>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in modifiedTables)
            _modifiedByTable[t.Table] = t;
    }

    /// <summary>True when the migration set changed nothing.</summary>
    public bool IsEmpty =>
        AddedTables.Count == 0 && RemovedTables.Count == 0 && ModifiedTables.Count == 0;

    /// <summary>True when the table was dropped by the migration set.</summary>
    public bool IsTableRemoved(string table) => _removedTables.Contains(table);

    /// <summary>True when the column was dropped (its table survives but the column doesn't).</summary>
    public bool IsColumnRemoved(string table, string column)
    {
        if (!_modifiedByTable.TryGetValue(table, out var t))
            return false;
        foreach (var c in t.RemovedColumns)
            if (string.Equals(c, column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>The change for a still-present modified column, or null if it was not modified.</summary>
    public ColumnChange? FindColumnChange(string table, string column)
    {
        if (!_modifiedByTable.TryGetValue(table, out var t))
            return null;
        foreach (var c in t.ModifiedColumns)
            if (string.Equals(c.Column, column, StringComparison.OrdinalIgnoreCase))
                return c;
        return null;
    }
}
