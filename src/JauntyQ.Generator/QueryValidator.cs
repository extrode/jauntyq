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

        // JNT5001/JNT5002: literals must fit their target columns
        ValidateLiterals(query, aliasToTable, schema, errors);

        // JNT8xxx: performance analysis (warnings only, never block)
        ValidatePerformance(query, aliasToTable, schema, errors);

        // JNT1001: Unsupported SQL constructs
        DetectUnsupportedConstructs(query, errors);

        return errors;
    }

    /// <summary>
    /// Static performance analysis from the query shape, parser-captured
    /// WHERE-clause patterns, and snapshot index metadata. All warnings:
    /// the query is correct, it just will not be fast.
    /// </summary>
    private static void ValidatePerformance(
        QueryModel query,
        Dictionary<string, string> aliasToTable,
        DatabaseSchema schema,
        List<ValidationError> errors)
    {
        // JNT8001: more tables than join conditions linking them
        if (query.StatementType == StatementType.Select &&
            query.Tables.Count > 1 &&
            query.Joins.Count < query.Tables.Count - 1)
        {
            errors.Add(new ValidationError(JauntyDiagnostics.JNT8001,
                $"{query.Tables.Count} tables but only {query.Joins.Count} join condition(s) found: " +
                "every unlinked table multiplies the result set (cartesian product)."));
        }

        // JNT8002 / JNT8003 from parser-captured WHERE patterns
        foreach (var hint in query.PerfHints)
        {
            var column = ResolveColumn(query, hint.BoundTableAlias, hint.BoundColumnName, aliasToTable, schema, out string? hintTable);
            if (column == null)
                continue; // unresolvable: do not guess

            if (hint.Kind == PerfHintKind.FunctionOnColumn)
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT8002,
                    $"{hint.FunctionName}({hintTable}.{column.Name}) in WHERE prevents index use: " +
                    "the function runs on every row. Compute on the parameter side instead."));
            }
            else if (hint.Kind == PerfHintKind.LeadingWildcardLike)
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT8003,
                    $"LIKE '{hint.Detail}' on {hintTable}.{column.Name} cannot seek an index " +
                    "(leading wildcard forces a scan)."));
            }
        }

        // JNT8004: filter/join columns no index covers. Only when the
        // snapshot carries index metadata at all (older snapshots do not).
        bool hasIndexMetadata = false;
        foreach (var table in schema.Tables.Values)
        {
            if (table.Indexes.Count > 0)
            {
                hasIndexMetadata = true;
                break;
            }
        }
        if (!hasIndexMetadata)
            return;

        foreach (var param in query.Parameters)
        {
            if (param.IsWriteTarget || string.IsNullOrEmpty(param.BoundColumnName))
                continue;
            CheckIndexed(query, param.BoundTableAlias, param.BoundColumnName, aliasToTable, schema, errors);
        }
        foreach (var join in query.Joins)
        {
            CheckIndexed(query, join.LeftTable, join.LeftColumn, aliasToTable, schema, errors);
            CheckIndexed(query, join.RightTable, join.RightColumn, aliasToTable, schema, errors);
        }
    }

    private static void CheckIndexed(
        QueryModel query, string tableAlias, string columnName,
        Dictionary<string, string> aliasToTable, DatabaseSchema schema,
        List<ValidationError> errors)
    {
        var column = ResolveColumn(query, tableAlias, columnName, aliasToTable, schema, out string? tableName);
        if (column == null || tableName == null)
            return;
        if (column.IsPrimaryKey)
            return;
        if (!schema.Tables.TryGetValue(tableName, out var tableSchema))
            return;

        foreach (var index in tableSchema.Indexes)
        {
            // only the leading key column makes a filter seekable
            if (index.Columns.Count > 0 &&
                string.Equals(index.Columns[0], column.Name, StringComparison.OrdinalIgnoreCase))
                return;
        }

        string message = $"No index covers {tableName}.{column.Name} used as a filter/join key: " +
                         "this query scans. Add an index or filter on an indexed column.";
        // one warning per column per query
        if (!errors.Exists(e => e.Code == "JNT8004" && e.Message == message))
            errors.Add(new ValidationError(JauntyDiagnostics.JNT8004, message));
    }

    /// <summary>
    /// Value safety: every SQL literal the parser bound to a column is
    /// proven to fit that column at compile time — string length against
    /// maxLength, numeric literals against decimal precision/scale and
    /// integer ranges. Unresolvable bindings are skipped (never guessed).
    /// </summary>
    private static void ValidateLiterals(
        QueryModel query,
        Dictionary<string, string> aliasToTable,
        DatabaseSchema schema,
        List<ValidationError> errors)
    {
        foreach (var lit in query.Literals)
        {
            var column = ResolveColumn(query, lit.BoundTableAlias, lit.BoundColumnName, aliasToTable, schema, out string? tableName);
            if (column == null)
                continue; // unresolvable, or already reported as JNT2002

            if (lit.Kind == LiteralKind.String)
            {
                if (column.MaxLength is int max && max > 0)
                {
                    // '' in the raw token is one escaped quote character
                    int length = lit.Value.Replace("''", "'").Length;
                    if (length > max)
                    {
                        errors.Add(new ValidationError(JauntyDiagnostics.JNT5001,
                            $"String literal ({length} chars) exceeds {tableName}.{column.Name} max length of {max}. " +
                            "It would truncate on write and can never match on read."));
                    }
                }
            }
            else
            {
                ValidateNumericLiteral(lit, column, tableName!, errors);
            }
        }
    }

    private static void ValidateNumericLiteral(
        LiteralBinding lit, ColumnSchema column, string tableName, List<ValidationError> errors)
    {
        if (!decimal.TryParse(lit.Value,
                System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out decimal value))
            return;

        // decimal/numeric/money: integer digits must fit precision - scale
        if (column.Precision is int precision && precision > 0)
        {
            int scale = column.Scale ?? 0;
            decimal integerPart = decimal.Truncate(Math.Abs(value));
            int intDigits = integerPart == 0
                ? 0
                : integerPart.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
            if (intDigits > precision - scale)
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT5002,
                    $"Numeric literal {lit.Value} does not fit {tableName}.{column.Name} ({column.DbType}, precision {precision}, scale {scale}): " +
                    $"at most {precision - scale} digit(s) before the decimal point. It would overflow at runtime."));
            }
            return;
        }

        // integer families: compile-time range check
        string dbType = column.DbType.ToLowerInvariant();
        int paren = dbType.IndexOf('(');
        if (paren >= 0)
            dbType = dbType.Substring(0, paren);

        (decimal Min, decimal Max)? range = dbType switch
        {
            "int" or "int4" or "integer" or "serial" => (int.MinValue, int.MaxValue),
            "bigint" or "int8" or "bigserial" => (long.MinValue, long.MaxValue),
            "smallint" or "int2" => (short.MinValue, short.MaxValue),
            "tinyint" => (0m, 255m),
            _ => ((decimal, decimal)?)null
        };

        if (range.HasValue && (value < range.Value.Min || value > range.Value.Max))
        {
            errors.Add(new ValidationError(JauntyDiagnostics.JNT5002,
                $"Numeric literal {lit.Value} is out of range for {tableName}.{column.Name} ({column.DbType}). It would overflow at runtime."));
        }
    }

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
