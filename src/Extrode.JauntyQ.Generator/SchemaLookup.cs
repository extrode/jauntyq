using Extrode.JauntyQ.Schema;

namespace Extrode.JauntyQ.Generator;

/// <summary>
/// Case-insensitive lookup of tables/columns in a schema snapshot. Table and
/// alias names as written in a .sql file (and alias resolution elsewhere in
/// this pipeline, e.g. CodeEmitter's ResolveAlias) are matched case-insensitively,
/// but <see cref="DatabaseSchema.Tables"/> and <see cref="TableSchema.Columns"/>
/// are plain (ordinal, case-sensitive) dictionaries keyed by whatever casing the
/// snapshot was pulled with -- so a direct TryGetValue silently misses whenever
/// the .sql file's casing differs from the live database's. Falls back to a
/// linear case-insensitive scan only when the fast-path exact lookup misses,
/// mirroring the same pattern already used for stored-procedure resolution
/// (see JauntyQGenerator.Part3.TryResolveProcedure).
/// </summary>
internal static class SchemaLookup
{
    public static bool TryGetTable(DatabaseSchema schema, string name, out TableSchema? table)
    {
        if (schema.Tables.TryGetValue(name, out table))
            return true;
        foreach (var kvp in schema.Tables)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                table = kvp.Value;
                return true;
            }
        }
        table = null;
        return false;
    }

    public static bool ContainsTable(DatabaseSchema schema, string name) =>
        TryGetTable(schema, name, out _);

    public static bool TryGetColumn(TableSchema table, string name, out ColumnSchema? column)
    {
        if (table.Columns.TryGetValue(name, out column))
            return true;
        foreach (var kvp in table.Columns)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                column = kvp.Value;
                return true;
            }
        }
        column = null;
        return false;
    }

    public static bool ContainsColumn(TableSchema table, string name) =>
        TryGetColumn(table, name, out _);
}
