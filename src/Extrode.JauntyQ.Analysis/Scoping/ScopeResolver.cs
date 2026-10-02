using Extrode.JauntyQ.Schema;

namespace Extrode.JauntyQ.Analysis.Scoping;

/// <summary>
/// Turns a loaded <c>jaunty.scope.json</c> into the scopes that are enforced,
/// checked against the effective schema (snapshot or DDL, after pending
/// migrations). Spec 021, R1.
///
/// Every entry that cannot be enforced is dropped and described in
/// <c>problems</c>; each description ends by saying the table is unscoped,
/// because a dropped entry read as "enforced" is the failure this feature
/// exists to prevent.
/// </summary>
public static class ScopeResolver
{
    public static List<ScopeColumn> Resolve(ScopeFile file, DatabaseSchema schema, List<string> problems)
    {
        var result = new List<ScopeColumn>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < file.Scopes.Count; i++)
        {
            var entry = file.Scopes[i];
            string where = $"Scope entry {i + 1}";
            if (string.IsNullOrWhiteSpace(entry.Table) || string.IsNullOrWhiteSpace(entry.Column))
            {
                string table = string.IsNullOrWhiteSpace(entry.Table) ? "(no table)" : entry.Table!;
                problems.Add($"{where} needs both \"table\" and \"column\". It was dropped, so table '{table}' is unscoped by it.");
                continue;
            }

            var tableSchema = FindTable(schema, entry.Table!);
            if (tableSchema == null)
            {
                problems.Add($"{where} names table '{entry.Table}', which is not in the schema. It was dropped, so table '{entry.Table}' is unscoped.");
                continue;
            }

            var column = FindColumn(tableSchema, entry.Column!);
            if (column == null)
            {
                problems.Add($"{where} names column '{entry.Column}', which table '{tableSchema.Name}' does not have. It was dropped, so table '{tableSchema.Name}' is unscoped by it.");
                continue;
            }

            string? unbindable = column.IsPrimaryKey ? "is part of the primary key"
                : column.IsIdentity ? "is an identity column"
                : column.IsComputed ? "is computed"
                : column.IsRowVersion ? "is a rowversion column"
                : null;
            if (unbindable != null)
            {
                problems.Add($"{where}: column '{column.Name}' of table '{tableSchema.Name}' {unbindable}, so it cannot be bound from a scope parameter. It was dropped, so table '{tableSchema.Name}' is unscoped by it.");
                continue;
            }

            if (!seen.Add(tableSchema.Name + "\u0000" + column.Name))
            {
                problems.Add($"{where} repeats table '{tableSchema.Name}', column '{column.Name}'. The repeat was dropped; the first entry still scopes the table.");
                continue;
            }

            result.Add(new ScopeColumn(tableSchema.Name, column.Name));
        }
        return result;
    }

    private static TableSchema? FindTable(DatabaseSchema schema, string name)
    {
        foreach (var table in schema.Tables.Values)
            if (string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase))
                return table;
        return null;
    }

    private static ColumnSchema? FindColumn(TableSchema table, string name)
    {
        foreach (var column in table.Columns.Values)
            if (string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase))
                return column;
        return null;
    }
}
