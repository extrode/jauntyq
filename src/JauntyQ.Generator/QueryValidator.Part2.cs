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

        // JNT8006: an explicit join whose column pair matches no declared
        // foreign key between the two tables. A real FK join is expected; a
        // mismatch is either a wrong-column join (a bug returning wrong/slow
        // results) or an unmodeled relationship. Skipped entirely when the
        // snapshot carries no FK metadata, and per-join when either side does
        // not resolve to a snapshot table (CTE/subquery/view) -- never guessed.
        // Composite FKs are stored as one row per column pair, so pair-by-pair
        // joins each match their own row and do not warn.
        if (schema.ForeignKeys.Count > 0)
        {
            foreach (var join in query.Joins)
            {
                var leftCol = ResolveColumn(query, join.LeftTable, join.LeftColumn, aliasToTable, schema, out string? leftTable);
                var rightCol = ResolveColumn(query, join.RightTable, join.RightColumn, aliasToTable, schema, out string? rightTable);
                if (leftCol == null || rightCol == null || leftTable == null || rightTable == null)
                    continue;

                if (JoinMatchesForeignKey(schema, leftTable, leftCol.Name, rightTable, rightCol.Name))
                    continue;

                string message = $"Join {leftTable}.{leftCol.Name} = {rightTable}.{rightCol.Name} matches no declared foreign key: " +
                                 "verify the join columns (a wrong-column join returns wrong or slow results).";
                if (!errors.Exists(e => e.Code == "JNT8006" && e.Message == message))
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT8006, message));
            }
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

        // The full set of (table instance, column) pairs this query filters/joins
        // on by equality. Keyed by alias when the reference carries one, falling
        // back to the canonical table name only for unaliased references --
        // NOT by canonical table name alone, because a self-join gives two
        // aliases (e.g. p1/p2) that resolve to the SAME table name; keying by
        // table name would let a filter on p1's column wrongly "cover" p2's
        // same-named column, even though p2's own predicates never touched it.
        // Lets a non-leading index column be recognized as seekable when all of
        // its more-leading columns are ALSO filtered in this same query, on the
        // SAME table instance -- a composite index is covered as a whole, not
        // column-by-column, but only for the instance actually filtered.
        var filterColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void CollectFilterColumn(string tableAlias, string columnName)
        {
            var col = ResolveColumn(query, tableAlias, columnName, aliasToTable, schema, out string? tableName);
            if (col != null && tableName != null)
            {
                string instanceKey = !string.IsNullOrEmpty(tableAlias) ? tableAlias : tableName;
                filterColumns.Add(instanceKey + "|" + col.Name);
            }
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

        // JNT8007: an ORDER BY on a column no index can order forces a runtime
        // sort. Only plain base-column items are eligible; ordinals, expressions,
        // and projected aliases were recorded with a kind that skips them here.
        // Reuses the same index-coverage test as JNT8004 (leading column, or a
        // non-leading column whose more-leading index columns are all filtered),
        // and its primary-key short-circuit.
        foreach (var orderBy in query.OrderBy)
        {
            if (orderBy.Kind != OrderByItemKind.PlainColumn)
                continue;

            var column = ResolveColumn(query, orderBy.BoundTableAlias, orderBy.BoundColumnName, aliasToTable, schema, out string? tableName);
            if (column == null || tableName == null)
                continue;
            if (!SchemaLookup.TryGetTable(schema, tableName, out var tableSchema))
                continue;
            string orderByInstanceKey = !string.IsNullOrEmpty(orderBy.BoundTableAlias) ? orderBy.BoundTableAlias : tableName;
            if (IsColumnIndexSupported(column, orderByInstanceKey, tableSchema!, filterColumns))
                continue;

            string message = $"ORDER BY {tableName}.{column.Name} has no supporting index: this query sorts at runtime. " +
                             $"Add an index whose leading column is {column.Name}, or accept the sort.";
            if (!errors.Exists(e => e.Code == "JNT8007" && e.Message == message))
                errors.Add(new ValidationError(JauntyDiagnostics.JNT8007, message));
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
        if (!SchemaLookup.TryGetTable(schema, tableName, out var tableSchema))
            return;
        string instanceKey = !string.IsNullOrEmpty(tableAlias) ? tableAlias : tableName;
        if (IsColumnIndexSupported(column, instanceKey, tableSchema!, filterColumns))
            return;

        string message = $"No index covers {tableName}.{column.Name} used as a filter/join key: " +
                         "this query scans. Add an index or filter on an indexed column.";
        // one warning per column per query
        if (!errors.Exists(e => e.Code == "JNT8004" && e.Message == message))
            errors.Add(new ValidationError(JauntyDiagnostics.JNT8004, message));
    }

    /// <summary>
    /// True when an index can position on <paramref name="column"/>: it is the
    /// table's primary key, or it appears in some index as the leading column, or
    /// as a non-leading column every one of whose more-leading index columns is
    /// also constrained (by an equality filter/join) in this query — so the
    /// composite index can be positioned up to it. Shared by JNT8004 (filter/join
    /// keys) and JNT8007 (ORDER BY columns).
    /// <paramref name="instanceKey"/> identifies the specific table INSTANCE this
    /// column belongs to (the alias, or the table name when unaliased) — never
    /// the bare table name for an aliased reference, so a self-join's two
    /// aliases of the same table are never conflated with each other.
    /// </summary>
    private static bool IsColumnIndexSupported(
        ColumnSchema column, string instanceKey, TableSchema tableSchema, HashSet<string> filterColumns)
    {
        if (column.IsPrimaryKey)
            return true;

        foreach (var index in tableSchema.Indexes)
        {
            int pos = index.Columns.FindIndex(c => string.Equals(c, column.Name, StringComparison.OrdinalIgnoreCase));
            if (pos < 0)
                continue;

            bool coveredUpToHere = true;
            for (int i = 0; i < pos; i++)
            {
                if (!filterColumns.Contains(instanceKey + "|" + index.Columns[i]))
                {
                    coveredUpToHere = false;
                    break;
                }
            }
            if (coveredUpToHere)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the joined column pair (already resolved to canonical table +
    /// column names) matches a declared foreign key in either direction.
    /// Composite FKs are stored one row per column pair, so a multi-column join
    /// matches pair by pair. Comparison is case-insensitive.
    /// </summary>
    private static bool JoinMatchesForeignKey(
        DatabaseSchema schema, string leftTable, string leftColumn, string rightTable, string rightColumn)
    {
        foreach (var fk in schema.ForeignKeys)
        {
            bool forward =
                string.Equals(fk.FromTable, leftTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.FromColumn, leftColumn, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.ToTable, rightTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.ToColumn, rightColumn, StringComparison.OrdinalIgnoreCase);
            bool reverse =
                string.Equals(fk.FromTable, rightTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.FromColumn, rightColumn, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.ToTable, leftTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.ToColumn, leftColumn, StringComparison.OrdinalIgnoreCase);
            if (forward || reverse)
                return true;
        }
        return false;
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
                ValidateNumericLiteral(lit, column, tableName!, schema.Dialect, errors);
            }
        }
    }

    private static void ValidateNumericLiteral(
        LiteralBinding lit, ColumnSchema column, string tableName, string dialect, List<ValidationError> errors)
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

        // integer families: compile-time range check. The base type is the
        // text before any '(n)' facet or modifier word ("tinyint(1) unsigned"
        // -> "tinyint"); MySQL's 'unsigned' modifier shifts the range.
        string fullDbType = column.DbType.ToLowerInvariant();
        bool unsigned = fullDbType.Contains("unsigned");
        string dbType = fullDbType;
        int cut = dbType.IndexOfAny(new[] { '(', ' ' });
        if (cut >= 0)
            dbType = dbType.Substring(0, cut);

        // tinyint is the one integer type whose signedness depends on the
        // engine: SQL Server's is unsigned 0..255, MySQL/MariaDB's is SIGNED
        // -128..127 unless declared 'unsigned'.
        bool mysqlDialect = string.Equals(dialect, "mysql", StringComparison.OrdinalIgnoreCase);

        (decimal Min, decimal Max)? range = dbType switch
        {
            // Postgres SERIAL family: "serial"/"bigserial" already had cases
            // here, but "smallserial" was missing entirely -- a residual gap
            // left by AUD-R19-01 (which fixed only DialectMapper's C#-type
            // mapping, a different switch, not this compile-time range
            // check) -- and "serial2"/"serial4"/"serial8" (Postgres's own
            // documented pure-numeric synonyms for
            // "smallserial"/"serial"/"bigserial") were never recognized
            // anywhere (AUD-R21-01/AUD-R21-02). Before this fix, all four
            // fell to this switch's `_ => null` fallback below, so
            // `range.HasValue` was false and JNT5002 silently never fired
            // for any of them no matter how far out of range the literal.
            "int" or "int4" or "integer" or "serial" or "serial4" =>
                unsigned ? (0m, 4294967295m) : (int.MinValue, int.MaxValue),
            "bigint" or "int8" or "bigserial" or "serial8" =>
                unsigned ? (0m, 18446744073709551615m) : (long.MinValue, long.MaxValue),
            "smallint" or "int2" or "smallserial" or "serial2" =>
                unsigned ? (0m, 65535m) : (short.MinValue, short.MaxValue),
            "tinyint" =>
                mysqlDialect && !unsigned ? (-128m, 127m) : (0m, 255m),
            // MySQL-only types with no case here at all before this fix:
            // DialectMapper.MapDbTypeToCSharp already maps both to
            // System.Int32 (a safe superset for storage), but that C#-side
            // safety doesn't mean the value fits the DATABASE column.
            // mediumint's true range (-8388608..8388607 signed,
            // 0..16777215 unsigned) is narrower than int32, so a literal
            // like 99999999 fit the mapped C# type and silently compiled
            // with no warning, then the database itself rejected it at
            // runtime ("Out of range value"). year's valid literal range is
            // 1901..2155 (MySQL's 4-digit YEAR); anything else round-trips
            // as 0000 at the server.
            "mediumint" =>
                unsigned ? (0m, 16777215m) : (-8388608m, 8388607m),
            "year" => (1901m, 2155m),
            _ => ((decimal, decimal)?)null
        };

        if (range.HasValue && (value < range.Value.Min || value > range.Value.Max))
        {
            errors.Add(new ValidationError(JauntyDiagnostics.JNT5002,
                $"Numeric literal {lit.Value} is out of range for {tableName}.{column.Name} ({column.DbType}). It would overflow at runtime."));
        }
    }

}
