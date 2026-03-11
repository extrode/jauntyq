using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;

public static class QueryValidator
{
    public static List<ValidationError> Validate(QueryModel query, DatabaseSchema? schema)
    {
        var errors = new List<ValidationError>();

        // JAUNTY006: Schema not provided
        if (schema == null)
        {
            errors.Add(new ValidationError("JAUNTY006",
                "Schema snapshot not found. Run 'jaunty schema pull'"));
            return errors;
        }

        // JAUNTY004: Empty query
        if (query.Columns.Count == 0)
        {
            errors.Add(new ValidationError("JAUNTY004",
                "SQL file is empty or contains no SELECT columns",
                ValidationSeverity.Warning));
            return errors;
        }

        // JAUNTY005: Duplicate parameter names
        var paramNames = new HashSet<string>();
        foreach (var param in query.Parameters)
        {
            if (!paramNames.Add(param.Name))
            {
                errors.Add(new ValidationError("JAUNTY005",
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

        // JAUNTY002: Table exists
        foreach (var table in query.Tables)
        {
            if (!schema.Tables.ContainsKey(table.TableName))
            {
                errors.Add(new ValidationError("JAUNTY002",
                    $"Table '{table.TableName}' does not exist in schema"));
            }
        }

        // JAUNTY001: Column exists + JAUNTY003: Ambiguous column
        foreach (var col in query.Columns)
        {
            if (col.ColumnName == "*")
                continue; // star select is valid if tables exist

            if (!string.IsNullOrEmpty(col.TableAlias))
            {
                // Qualified column — resolve alias to table name
                if (aliasToTable.TryGetValue(col.TableAlias, out string? tableName))
                {
                    if (schema.Tables.TryGetValue(tableName, out var tableSchema))
                    {
                        if (!tableSchema.Columns.ContainsKey(col.ColumnName))
                        {
                            errors.Add(new ValidationError("JAUNTY001",
                                $"Column '{col.ColumnName}' does not exist in table '{tableName}'"));
                        }
                    }
                }
                else
                {
                    errors.Add(new ValidationError("JAUNTY001",
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
                    errors.Add(new ValidationError("JAUNTY001",
                        $"Column '{col.ColumnName}' does not exist in any referenced table"));
                }
                else if (matchingTables.Count > 1)
                {
                    errors.Add(new ValidationError("JAUNTY003",
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

        // JAUNTY007: Unsupported SQL constructs
        DetectUnsupportedConstructs(query, errors);

        return errors;
    }

    private static void DetectUnsupportedConstructs(QueryModel query, List<ValidationError> errors)
    {
        foreach (var construct in query.UnsupportedConstructs)
        {
            errors.Add(new ValidationError("JAUNTY007",
                $"Unsupported SQL construct: {construct}",
                ValidationSeverity.Warning));
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
                    errors.Add(new ValidationError("JAUNTY001",
                        $"Column '{columnName}' does not exist in table '{tableName}'"));
                }
            }
        }
    }
}
