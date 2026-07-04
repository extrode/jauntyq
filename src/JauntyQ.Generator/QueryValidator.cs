using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;

public static class QueryValidator
{
    public static List<ValidationError> Validate(QueryModel query, DatabaseSchema? schema)
    {
        var errors = new List<ValidationError>();

        // JNT6001: Schema not provided
        if (schema == null)
        {
            errors.Add(new ValidationError(JauntyDiagnostics.JNT6001,
                "Schema snapshot not found. Run 'jaunty schema pull'"));
            return errors;
        }

        // JNT3001: Empty query (SELECT only — CRUD has no columns)
        if (query.StatementType == StatementType.Select && query.Columns.Count == 0)
        {
            errors.Add(new ValidationError(JauntyDiagnostics.JNT3001,
                "SQL file is empty or contains no SELECT columns"));
            return errors;
        }

        // JNT4004: Duplicate parameter names
        var paramNames = new HashSet<string>();
        foreach (var param in query.Parameters)
        {
            if (!paramNames.Add(param.Name))
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT4004,
                    $"Duplicate parameter name '@{param.Name}'"));
            }
        }

        // Build alias-to-table map
        var aliasToTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in query.Tables)
        {
            string key = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
            aliasToTable[key] = table.TableName;
        }

        // JNT2001: Table exists
        foreach (var table in query.Tables)
        {
            if (!schema.Tables.ContainsKey(table.TableName))
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT2001,
                    $"Table '{table.TableName}' does not exist in schema"));
            }
        }

        // JNT2002: Column exists + JNT2003: Ambiguous column
        foreach (var col in query.Columns)
        {
            if (col.ColumnName == "*")
            {
                // JNT3002: SELECT * makes the generated result type depend on
                // live table definition order, and schema drift (a later ADD
                // COLUMN) surfaces at RUNTIME instead of failing this build.
                // The message hands the caller the explicit list to paste.
                errors.Add(new ValidationError(JauntyDiagnostics.JNT3002,
                    BuildSelectStarMessage(query, schema)));
                continue;
            }

            if (!string.IsNullOrEmpty(col.TableAlias))
            {
                // Qualified column — resolve alias to table name
                if (aliasToTable.TryGetValue(col.TableAlias, out string? tableName))
                {
                    if (schema.Tables.TryGetValue(tableName, out var tableSchema))
                    {
                        if (!tableSchema.Columns.ContainsKey(col.ColumnName))
                        {
                            errors.Add(new ValidationError(JauntyDiagnostics.JNT2002,
                                $"Column '{col.ColumnName}' does not exist in table '{tableName}'"));
                        }
                    }
                }
                else
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT2002,
                        $"Unknown table alias '{col.TableAlias}'"));
                }
            }
            else
            {
                // Unqualified column — check ambiguity across all referenced tables
                var matchingTables = new List<string>();
                foreach (var table in query.Tables)
                {
                    if (schema.Tables.TryGetValue(table.TableName, out var tableSchema))
                    {
                        if (tableSchema.Columns.ContainsKey(col.ColumnName))
                        {
                            matchingTables.Add(table.TableName);
                        }
                    }
                }

                if (matchingTables.Count == 0)
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT2002,
                        $"Column '{col.ColumnName}' does not exist in any referenced table"));
                }
                else if (matchingTables.Count > 1)
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT2003,
                        $"Ambiguous column reference '{col.ColumnName}' found in tables: {string.Join(", ", matchingTables)}"));
                }
            }
        }

        // Validate join columns exist
        foreach (var join in query.Joins)
        {
            ValidateJoinSide(join.LeftTable, join.LeftColumn, aliasToTable, schema, errors);
            ValidateJoinSide(join.RightTable, join.RightColumn, aliasToTable, schema, errors);
        }

        // JNT1001: Unsupported SQL constructs
        DetectUnsupportedConstructs(query, errors);

        return errors;
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
