using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class QueryValidator
{
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

        // The full set of (table, column) pairs this query filters/joins on
        // by equality, resolved to their schema-canonical names. Lets a
        // non-leading index column be recognized as seekable when all of its
        // more-leading columns are ALSO filtered in this same query -- a
        // composite index is covered as a whole, not column-by-column.
        var filterColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void CollectFilterColumn(string tableAlias, string columnName)
        {
            var col = ResolveColumn(query, tableAlias, columnName, aliasToTable, schema, out string? tableName);
            if (col != null && tableName != null)
                filterColumns.Add(tableName + "|" + col.Name);
        }

        foreach (var param in query.Parameters)
        {
            if (param.IsWriteTarget || string.IsNullOrEmpty(param.BoundColumnName))
                continue;
            CollectFilterColumn(param.BoundTableAlias, param.BoundColumnName);
        }
        foreach (var hint in query.PerfHints)
        {
            if (hint.Kind == PerfHintKind.ColumnComparedToColumn)
                CollectFilterColumn(hint.BoundTableAlias, hint.BoundColumnName);
        }
        foreach (var join in query.Joins)
        {
            CollectFilterColumn(join.LeftTable, join.LeftColumn);
            CollectFilterColumn(join.RightTable, join.RightColumn);
        }

        foreach (var param in query.Parameters)
        {
            if (param.IsWriteTarget || string.IsNullOrEmpty(param.BoundColumnName))
                continue;
            CheckIndexed(query, param.BoundTableAlias, param.BoundColumnName, aliasToTable, schema, filterColumns, errors);
        }
        foreach (var hint in query.PerfHints)
        {
            if (hint.Kind != PerfHintKind.ColumnComparedToColumn)
                continue;
            CheckIndexed(query, hint.BoundTableAlias, hint.BoundColumnName, aliasToTable, schema, filterColumns, errors);
        }
        foreach (var join in query.Joins)
        {
            CheckIndexed(query, join.LeftTable, join.LeftColumn, aliasToTable, schema, filterColumns, errors);
            CheckIndexed(query, join.RightTable, join.RightColumn, aliasToTable, schema, filterColumns, errors);
        }
    }

    private static void CheckIndexed(
        QueryModel query, string tableAlias, string columnName,
        Dictionary<string, string> aliasToTable, DatabaseSchema schema,
        HashSet<string> filterColumns,
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
            int pos = index.Columns.FindIndex(c => string.Equals(c, column.Name, StringComparison.OrdinalIgnoreCase));
            if (pos < 0)
                continue;

            // The leading column always seeks. A non-leading column seeks
            // too, but only when every column ahead of it in the same index
            // is also being filtered/joined on in this query -- otherwise
            // the composite index can't be positioned past its gap.
            bool coveredUpToHere = true;
            for (int i = 0; i < pos; i++)
            {
                if (!filterColumns.Contains(tableName + "|" + index.Columns[i]))
                {
                    coveredUpToHere = false;
                    break;
                }
            }
            if (coveredUpToHere)
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

}
