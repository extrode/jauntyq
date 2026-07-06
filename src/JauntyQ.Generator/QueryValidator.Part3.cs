using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class QueryValidator
{
    private static ColumnSchema? ResolveColumn(
        QueryModel query, string tableAlias, string columnName,
        Dictionary<string, string> aliasToTable, DatabaseSchema schema, out string? tableName)
    {
        tableName = null;
        if (string.IsNullOrEmpty(columnName))
            return null;

        if (!string.IsNullOrEmpty(tableAlias))
        {
            if (aliasToTable.TryGetValue(tableAlias, out string? resolved) &&
                schema.Tables.TryGetValue(resolved, out var aliasedTable) &&
                aliasedTable.Columns.TryGetValue(columnName, out var aliasedColumn))
            {
                tableName = resolved;
                return aliasedColumn;
            }
            return null;
        }

        ColumnSchema? match = null;
        foreach (var table in query.Tables)
        {
            if (schema.Tables.TryGetValue(table.TableName, out var tableSchema) &&
                tableSchema.Columns.TryGetValue(columnName, out var column))
            {
                if (match != null)
                    return null; // ambiguous — JNT2003 territory, skip the value check
                match = column;
                tableName = table.TableName;
            }
        }
        return match;
    }

    private static void DetectUnsupportedConstructs(QueryModel query, List<ValidationError> errors)
    {
        foreach (var construct in query.UnsupportedConstructs)
        {
            errors.Add(new ValidationError(JauntyDiagnostics.JNT1001,
                $"Unsupported SQL construct: {construct}"));
        }
    }

    private static void ValidateJoinSide(
        string tableAlias, string columnName,
        Dictionary<string, string> aliasToTable,
        DatabaseSchema schema,
        List<ValidationError> errors)
    {
        if (string.IsNullOrEmpty(tableAlias))
            return;

        if (aliasToTable.TryGetValue(tableAlias, out string? tableName))
        {
            if (schema.Tables.TryGetValue(tableName, out var tableSchema))
            {
                if (!tableSchema.Columns.ContainsKey(columnName))
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT2002,
                        $"Column '{columnName}' does not exist in table '{tableName}'"));
                }
            }
        }
    }

    /// <summary>
    /// Builds the JNT3002 message with the paste-ready explicit column list
    /// (alias-qualified when the query joins multiple tables).
    /// </summary>
    private static string BuildSelectStarMessage(QueryModel query, DatabaseSchema schema)
    {
        var cols = new List<string>();
        bool qualify = query.Tables.Count > 1;
        foreach (var table in query.Tables)
        {
            if (!schema.Tables.TryGetValue(table.TableName, out var tableSchema))
                continue; // JNT2001 already reported
            string prefix = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
            foreach (var col in tableSchema.Columns.Values)
            {
                cols.Add(qualify ? $"{prefix}.{col.Name}" : col.Name);
            }
        }

        string list = cols.Count > 0 ? string.Join(", ", cols) : "<no columns resolved>";
        return "SELECT * is not allowed: the projection must be explicit so the generated " +
               "result type is deterministic and schema drift fails this build instead of " +
               $"failing at runtime. Replace * with: {list}";
    }
}
