using JauntyQ.Schema;

namespace JauntyQ.Analysis.Diff;

/// <summary>
/// Computes a name-based <see cref="SchemaDelta"/> between the pre-migration
/// baseline and the post-migration effective schema (the output of
/// <c>SchemaSimulator.Apply</c>). Composes the existing schema model — it is a
/// field-by-field structural comparison, not a new diffing engine. Tables and
/// columns are matched by name (case-insensitive); ordinal/position changes are
/// intentionally ignored here because a query references columns by name, not by
/// position (positional drift is the separate concern of <c>schema verify</c>).
/// </summary>
public static class StructuralSchemaDiff
{
    public static SchemaDelta Compute(DatabaseSchema baseline, DatabaseSchema effective)
    {
        var addedTables = new List<string>();
        var removedTables = new List<string>();
        var modifiedTables = new List<TableDelta>();

        foreach (var table in baseline.Tables.Values)
        {
            if (!effective.Tables.ContainsKey(table.Name))
            {
                removedTables.Add(table.Name);
                continue;
            }

            var delta = CompareTable(table, effective.Tables[table.Name]);
            if (delta != null)
                modifiedTables.Add(delta);
        }

        foreach (var table in effective.Tables.Values)
        {
            if (!baseline.Tables.ContainsKey(table.Name))
                addedTables.Add(table.Name);
        }

        return new SchemaDelta(addedTables, removedTables, modifiedTables);
    }

    private static TableDelta? CompareTable(TableSchema baseline, TableSchema effective)
    {
        var added = new List<string>();
        var removed = new List<string>();
        var modified = new List<ColumnChange>();

        var effectiveByName = ByName(effective.Columns.Values);
        var baselineByName = ByName(baseline.Columns.Values);

        foreach (var col in baseline.Columns.Values)
        {
            if (!effectiveByName.TryGetValue(col.Name, out var after))
            {
                removed.Add(col.Name);
                continue;
            }

            var kinds = DiffColumn(col, after);
            if (kinds.Count > 0)
                modified.Add(new ColumnChange(col.Name, col, after, kinds));
        }

        foreach (var col in effective.Columns.Values)
        {
            if (!baselineByName.ContainsKey(col.Name))
                added.Add(col.Name);
        }

        if (added.Count == 0 && removed.Count == 0 && modified.Count == 0)
            return null;

        return new TableDelta(effective.Name, added, removed, modified);
    }

    private static List<ColumnChangeKind> DiffColumn(ColumnSchema before, ColumnSchema after)
    {
        var kinds = new List<ColumnChangeKind>();
        if (!string.Equals(before.DbType, after.DbType, StringComparison.OrdinalIgnoreCase))
            kinds.Add(ColumnChangeKind.Type);
        if (before.IsNullable != after.IsNullable)
            kinds.Add(ColumnChangeKind.Nullability);
        if (before.MaxLength != after.MaxLength)
            kinds.Add(ColumnChangeKind.MaxLength);
        if (before.Precision != after.Precision || before.Scale != after.Scale)
            kinds.Add(ColumnChangeKind.PrecisionScale);
        if (before.IsUnicode != after.IsUnicode)
            kinds.Add(ColumnChangeKind.Unicode);
        if (before.IsPrimaryKey != after.IsPrimaryKey)
            kinds.Add(ColumnChangeKind.PrimaryKey);
        if (before.IsIdentity != after.IsIdentity)
            kinds.Add(ColumnChangeKind.Identity);
        if (before.IsRowVersion != after.IsRowVersion)
            kinds.Add(ColumnChangeKind.RowVersion);
        // Previously undiffed: a migration that turns a plain column
        // computed/generated (or vice versa) produced zero delta here, so a
        // query writing to that column via INSERT/UPDATE was classified Safe
        // by ImpactClassifier instead of Risky, even though the write will
        // be rejected by the database once the column is generated.
        if (before.IsComputed != after.IsComputed)
            kinds.Add(ColumnChangeKind.Computed);
        return kinds;
    }

    private static Dictionary<string, ColumnSchema> ByName(IEnumerable<ColumnSchema> columns)
    {
        var map = new Dictionary<string, ColumnSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in columns)
            map[c.Name] = c;
        return map;
    }
}
