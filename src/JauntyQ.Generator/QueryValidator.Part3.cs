using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class QueryValidator
{
    /// <summary>
    /// Resolves an (alias, column) reference to its schema column and canonical
    /// table. Shared by the per-query analyzers here and the cross-query
    /// NPlusOneAnalyzer (JNT8008): alias-qualified references resolve through
    /// the statement's alias map; unqualified ones resolve only when exactly one
    /// referenced table has the column (ambiguity is never guessed).
    /// </summary>
    internal static ColumnSchema? ResolveColumn(
        QueryModel query, string tableAlias, string columnName,
        Dictionary<string, string> aliasToTable, DatabaseSchema schema, out string? tableName)
    {
        tableName = null;
        if (string.IsNullOrEmpty(columnName))
            return null;

        if (!string.IsNullOrEmpty(tableAlias))
        {
            if (aliasToTable.TryGetValue(tableAlias, out string? resolved) &&
                SchemaLookup.TryGetTable(schema, resolved, out var aliasedTable) &&
                SchemaLookup.TryGetColumn(aliasedTable!, columnName, out var aliasedColumn))
            {
                tableName = resolved;
                return aliasedColumn;
            }
            return null;
        }

        ColumnSchema? match = null;
        foreach (var table in query.Tables)
        {
            if (SchemaLookup.TryGetTable(schema, table.TableName, out var tableSchema) &&
                SchemaLookup.TryGetColumn(tableSchema!, columnName, out var column))
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
        List<ValidationError> errors,
        Dictionary<string, List<string>>? virtualTables = null)
    {
        if (string.IsNullOrEmpty(tableAlias))
            return;

        if (aliasToTable.TryGetValue(tableAlias, out string? tableName))
        {
            // CTE-qualified join sides are opaque — the virtual columns are
            // already scoped for projection resolution; skip existence here.
            if (virtualTables != null && virtualTables.ContainsKey(tableName))
                return;
            if (SchemaLookup.TryGetTable(schema, tableName, out var tableSchema))
            {
                if (!SchemaLookup.ContainsColumn(tableSchema!, columnName))
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT2002,
                        $"Column '{columnName}' does not exist in table '{tableName}'"));
                }
            }
        }
    }

    /// <summary>
    /// JNT7002: verbatim SQL emission has no dialect rewrite, so a RETURNING
    /// clause or a data-modifying CTE fails under dialects that don't support
    /// them as written:
    ///   - sqlserver: no RETURNING/OUTPUT-rewrite and no writable CTEs at all.
    ///   - mysql: no writable CTEs (a CTE body can only be a SELECT), and
    ///     stock MySQL has no RETURNING clause on any DML statement. MariaDB
    ///     (which shares the "mysql" dialect string — see DialectMapper)
    ///     added RETURNING for INSERT/DELETE in 10.5 and UPDATE in 13.0, so
    ///     this intentionally over-flags valid MariaDB RETURNING usage in
    ///     exchange for catching it on stock MySQL, where it always fails.
    /// postgres/sqlite support both constructs and are never gated here.
    /// </summary>
    private static void ValidateDialectConstructs(QueryModel query, DatabaseSchema schema, List<ValidationError> errors)
    {
        bool isSqlServer = string.Equals(schema.Dialect, "sqlserver", StringComparison.OrdinalIgnoreCase);
        bool isMySql = string.Equals(schema.Dialect, "mysql", StringComparison.OrdinalIgnoreCase);
        if (!isSqlServer && !isMySql)
            return;

        if (query.Ctes.Count > 0)
            errors.Add(new ValidationError(JauntyDiagnostics.JNT7002,
                $"Data-modifying CTEs (WITH ... INSERT/UPDATE/DELETE) are not supported under the {schema.Dialect} dialect; the SQL is emitted verbatim with no rewrite."));

        if (query.HasReturning || query.Returning.Count > 0)
        {
            string advice = isSqlServer
                ? "use OUTPUT"
                : "use LAST_INSERT_ID()/a follow-up SELECT, or MariaDB 10.5+/13.0 if RETURNING is actually available on your server";
            errors.Add(new ValidationError(JauntyDiagnostics.JNT7002,
                $"RETURNING is not supported under the {schema.Dialect} dialect ({advice}); the SQL is emitted verbatim so it cannot be rewritten."));
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
            if (!SchemaLookup.TryGetTable(schema, table.TableName, out var tableSchema))
                continue; // JNT2001 already reported
            string prefix = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
            foreach (var col in tableSchema!.Columns.Values)
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
