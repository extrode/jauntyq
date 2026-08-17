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
        // not resolve to a snapshot table (CTE/subquery) -- never guessed.
        //
        // Views are skipped explicitly. Until spec 015 they were excluded by
        // accident: a view was never extracted, so it never resolved and this
        // check fell through the null guard below. Now that a view IS a
        // snapshot relation, it resolves -- and a view cannot declare a
        // foreign key in any of the five dialects, so EVERY join to one would
        // match no FK and warn. The claim "matches no declared foreign key"
        // is unprovable rather than false for a view, and R7 is that an
        // analysis which cannot be proved against a view stays silent.
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

                if (IsViewRelation(schema, leftTable) || IsViewRelation(schema, rightTable))
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

        var equalitySeekColumns = CollectEqualitySeekColumns(query, aliasToTable, schema);

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
            CheckIndexed(query, param.BoundTableAlias, param.BoundColumnName, aliasToTable, schema, filterColumns, equalitySeekColumns, errors);
        }
        foreach (var hint in query.PerfHints)
        {
            if (hint.Kind != PerfHintKind.ColumnComparedToColumn)
                continue;
            CheckIndexed(query, hint.BoundTableAlias, hint.BoundColumnName, aliasToTable, schema, filterColumns, equalitySeekColumns, errors);
        }
        foreach (var join in query.Joins)
        {
            CheckIndexed(query, join.LeftTable, join.LeftColumn, aliasToTable, schema, filterColumns, equalitySeekColumns, errors);
            CheckIndexed(query, join.RightTable, join.RightColumn, aliasToTable, schema, filterColumns, equalitySeekColumns, errors);
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
            // Same reasoning as CheckIndexed's view gate: a view declares no
            // indexes, so "no supporting index" is true of every view column
            // and proves nothing about whether the engine sorts. This block
            // reaches IsColumnIndexSupported directly rather than through
            // CheckIndexed, so it needs its own gate.
            if (tableSchema!.IsView)
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

    /// <summary>
    /// The (table instance, column) pairs this query constrains by STRICT
    /// equality: "=" parameters and ON/USING equijoin columns (a JoinRef is
    /// only ever built from an "=" condition). Range/IN/LIKE parameters are
    /// excluded — "pk &gt; @min" constrains the PK column without constraining
    /// the KEY, so it does not prove a one-row seek. Keyed
    /// <c>instance|column</c>, the format <see cref="FilterSetCoversUniqueKey"/>
    /// consumes; the instance is the alias when the reference carries one, so a
    /// self-join's two aliases are never conflated.
    ///
    /// Lives here rather than inline in <see cref="ValidatePerformance"/>
    /// because <see cref="ValidatePagination"/> needs it BEFORE that method's
    /// index-metadata early return: a DDL-sourced schema carries no index
    /// metadata at all but still carries primary keys, which is exactly what
    /// the pagination checks reason from.
    /// </summary>
    private static HashSet<string> CollectEqualitySeekColumns(
        QueryModel query, Dictionary<string, string> aliasToTable, DatabaseSchema schema)
    {
        var seekColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(string tableAlias, string columnName)
        {
            var col = ResolveColumn(query, tableAlias, columnName, aliasToTable, schema, out string? tableName);
            if (col != null && tableName != null)
            {
                string instanceKey = !string.IsNullOrEmpty(tableAlias) ? tableAlias : tableName;
                seekColumns.Add(instanceKey + "|" + col.Name);
            }
        }

        foreach (var param in query.Parameters)
        {
            if (param.IsWriteTarget || string.IsNullOrEmpty(param.BoundColumnName))
                continue;
            if (param.ComparisonOp == "=")
                Collect(param.BoundTableAlias, param.BoundColumnName);
        }
        foreach (var join in query.Joins)
        {
            Collect(join.LeftTable, join.LeftColumn);
            Collect(join.RightTable, join.RightColumn);
        }

        return seekColumns;
    }

    /// <summary>
    /// True when the named relation resolves to a view in the snapshot. Used
    /// by the perf/consistency analyses to stay silent rather than assert
    /// something a view cannot answer. Case-insensitive via SchemaLookup, for
    /// the same reason every other lookup here is.
    /// </summary>
    private static bool IsViewRelation(DatabaseSchema schema, string tableName) =>
        SchemaLookup.TryGetTable(schema, tableName, out var t) && t!.IsView;

    private static void CheckIndexed(
        QueryModel query, string tableAlias, string columnName,
        Dictionary<string, string> aliasToTable, DatabaseSchema schema,
        HashSet<string> filterColumns,
        HashSet<string> equalitySeekColumns,
        List<ValidationError> errors)
    {
        var column = ResolveColumn(query, tableAlias, columnName, aliasToTable, schema, out string? tableName);
        if (column == null || tableName == null)
            return;
        if (!SchemaLookup.TryGetTable(schema, tableName, out var tableSchema))
            return;

        // A view carries no indexes of its own, so "no index covers this
        // column" is true of every view column and says nothing about whether
        // the query scans: the planner inlines the view's definition and may
        // well seek an index on the base table underneath. Warning here would
        // punish exactly the rewrite the reference page recommends -- a
        // consumer who wraps an unsupported shape in a view would collect one
        // JNT8004 per filter for a query that is not slow. R7: unprovable
        // against a view means silent. JNT8007 (ORDER BY) does NOT come
        // through here -- it calls IsColumnIndexSupported directly -- and
        // carries its own copy of this gate.
        if (tableSchema!.IsView)
            return;

        string instanceKey = !string.IsNullOrEmpty(tableAlias) ? tableAlias : tableName;
        if (IsColumnIndexSupported(column, instanceKey, tableSchema!, filterColumns))
            return;

        // A residual predicate beside a covered unique seek is free: when this
        // query's equality filters on the SAME table instance already cover a
        // complete primary key or unique index, the seek reaches at most one
        // row (per outer row, for a join), so the extra check on this column
        // scans nothing. Auto-CRUD's optimistic-concurrency shape --
        // WHERE pk = @ AND row_version = @ -- reaches this through the
        // rowversion column alone.
        if (FilterSetCoversUniqueKey(instanceKey, tableSchema!, equalitySeekColumns))
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
            // An expression index's Columns is only the real-column subset of
            // its key, positions lost -- (lower(a), b) arrives as [b] at pos 0
            // and would claim seekability falsely. Skipping keeps exactly the
            // behavior these hints had when such indexes were excluded at
            // capture.
            if (index.HasExpressionKeyPart)
                continue;

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
    /// True when the equality-filtered columns of <paramref name="instanceKey"/>
    /// (this query's filter/join set) cover a complete primary key or a complete
    /// non-prefix, non-expression unique index of the table — a seek that reaches
    /// at most one row, making any residual predicate on the same instance free.
    /// Prefix unique indexes are excluded because they enforce uniqueness over
    /// truncated values only; expression indexes because their
    /// <see cref="IndexSchema.Columns"/> is not the full key (both per the
    /// <see cref="IndexSchema"/> flag contracts).
    ///
    /// Three callers, and in every one a "false" only ever means "do not
    /// suppress": JNT8004 passes this query's equality filter set (an uncovered
    /// key errs toward warning); JNT8009/JNT8010 pass the same set to decide a
    /// query is already pinned to one row, and JNT8009 additionally passes its
    /// ORDER BY column set to decide the sort is total. The column set is the
    /// only thing that changes — the question "do these columns cover a unique
    /// key of this table instance" is the same one each time.
    /// </summary>
    private static bool FilterSetCoversUniqueKey(
        string instanceKey, TableSchema tableSchema, HashSet<string> equalitySeekColumns)
    {
        bool hasPkColumn = false, allPkColumnsFiltered = true;
        foreach (var col in tableSchema.Columns.Values)
        {
            if (!col.IsPrimaryKey)
                continue;
            hasPkColumn = true;
            if (!equalitySeekColumns.Contains(instanceKey + "|" + col.Name))
            {
                allPkColumnsFiltered = false;
                break;
            }
        }
        if (hasPkColumn && allPkColumnsFiltered)
            return true;

        foreach (var index in tableSchema.Indexes)
        {
            if (!index.IsUnique || index.HasPrefixKeyPart || index.HasExpressionKeyPart)
                continue;
            if (index.Columns.Count == 0)
                continue;

            bool allFiltered = true;
            foreach (var keyColumn in index.Columns)
            {
                if (!equalitySeekColumns.Contains(instanceKey + "|" + keyColumn))
                {
                    allFiltered = false;
                    break;
                }
            }
            if (allFiltered)
                return true;
        }
        return false;
    }

    /// <summary>
    /// JNT8009/JNT8010: a query that takes a page with LIMIT/OFFSET but cannot
    /// name a deterministic row order. Between rows the ORDER BY ranks equally
    /// the engine may return any order it likes, and it need not pick the same
    /// one twice — so across two page requests a row can arrive on both pages
    /// or on neither. With no ORDER BY at all (JNT8010) the page is simply an
    /// arbitrary subset. Both are silent at runtime and intermittent, which is
    /// why they are worth a build warning.
    ///
    /// Called per statement from ValidateStatement, which already runs once per
    /// CTE body — so a page taken INSIDE a CTE (the idiomatic
    /// paginate-then-aggregate shape) is checked against its own base columns
    /// rather than against the outer statement's unresolvable virtual ones.
    ///
    /// Deliberately narrow. Every gate below is a case where the inference does
    /// not hold or cannot be proven, and the check stays silent rather than
    /// guessing: the value of these codes is entirely in not firing on the
    /// correct paginating queries people already have.
    /// </summary>
    private static void ValidatePagination(
        QueryModel query,
        Dictionary<string, string> aliasToTable,
        DatabaseSchema schema,
        HashSet<string> equalitySeekColumns,
        bool isSubquery,
        List<ValidationError> errors)
    {
        if (query.StatementType != StatementType.Select || !query.HasRowLimit)
            return;

        // A LIMIT inside a predicate subquery -- EXISTS (SELECT 1 ... LIMIT 1)
        // -- is idiomatic and cannot be unstable: the subquery answers a
        // yes/no or membership question, so which row satisfied it never
        // reaches the caller.
        if (isSubquery)
            return;

        // A join fans rows out and GROUP BY collapses them, so a unique key on
        // the one base table proves nothing about the result's row identity.
        // A well-written paginating CTE is single-table by construction, which
        // is why this restriction costs almost nothing in practice.
        if (query.Joins.Count > 0 || query.Tables.Count != 1 || query.HasGroupBy)
            return;

        var table = query.Tables[0];
        if (!SchemaLookup.TryGetTable(schema, table.TableName, out var tableSchema))
            return;
        string instanceKey = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;

        // Already filtered to at most one row: the "page" is that row however
        // it is ordered, so neither code applies. This is the -- @first idiom
        // (WHERE pk = @id LIMIT 1).
        if (FilterSetCoversUniqueKey(instanceKey, tableSchema!, equalitySeekColumns))
            return;

        if (query.OrderBy.Count == 0)
        {
            // JNT8010 needs no uniqueness proof, only the fact that rows are
            // being taken in no stated order -- so unlike JNT8009 it is just as
            // valid for a DDL-sourced table with no captured index metadata.
            string unordered =
                $"{table.TableName} is paginated (LIMIT/OFFSET) with no ORDER BY: the rows in a page " +
                "are an arbitrary subset and can differ between requests. Add an ORDER BY, tiebroken " +
                "on a unique column.";
            if (!errors.Exists(e => e.Code == "JNT8010" && e.Message == unordered))
                errors.Add(new ValidationError(JauntyDiagnostics.JNT8010, unordered));
            return;
        }

        // The plain base-column items, keyed exactly as FilterSetCoversUniqueKey
        // expects. Ordinals, expressions and projected aliases contribute
        // nothing here; they are handled by the bail below.
        var orderByColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedNames = new List<string>();
        bool allItemsArePlainColumns = true;
        foreach (var item in query.OrderBy)
        {
            if (item.Kind != OrderByItemKind.PlainColumn)
            {
                allItemsArePlainColumns = false;
                continue;
            }

            var column = ResolveColumn(query, item.BoundTableAlias, item.BoundColumnName, aliasToTable, schema, out string? tableName);
            if (column == null || tableName == null)
            {
                // An item we cannot resolve might be the unique one.
                allItemsArePlainColumns = false;
                continue;
            }

            orderByColumns.Add(instanceKey + "|" + column.Name);
            orderedNames.Add(column.Name);
        }

        // Total: the sorted-on columns cover a primary key or a complete unique
        // index, so no two rows compare equal and the order is deterministic.
        // Checked BEFORE the non-plain bail, because the common correct shape
        // sorts on a computed rank FIRST and tiebreaks on the key after it --
        // the alias in front does not make the key behind it any less unique.
        if (FilterSetCoversUniqueKey(instanceKey, tableSchema!, orderByColumns))
            return;

        // An expression, ordinal or projected-alias item may itself be unique
        // (ORDER BY lower(email) on a case-insensitively unique email), and
        // nothing in the IR can tell us. Do not guess.
        if (!allItemsArePlainColumns)
            return;

        // No captured indexes means secondary unique indexes are INVISIBLE, not
        // absent: the MigrationParser drops standalone CREATE UNIQUE INDEX and
        // inline UNIQUE constraints (JauntyQGenerator.Part7.cs), so a
        // DDL-sourced table carries its primary key and nothing else. A
        // tiebreak on a genuinely unique non-PK column is then unprovable, and
        // firing would be a false positive on every such project. Stay silent.
        if (tableSchema!.Indexes.Count == 0)
            return;

        string sorted = string.Join(", ", orderedNames);
        string message =
            $"{table.TableName} is paginated (LIMIT/OFFSET) but ORDER BY {sorted} is not unique: " +
            "rows that sort equally have no defined order, so one can appear on two pages or on " +
            "none. Add a tiebreak on a unique column (typically the primary key).";
        if (!errors.Exists(e => e.Code == "JNT8009" && e.Message == message))
            errors.Add(new ValidationError(JauntyDiagnostics.JNT8009, message));
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
                    int length = MeasureLiteralLength(lit.Value.Replace("''", "'"), schema.Dialect);
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

    /// <summary>
    /// The length of a decoded string literal in the unit the target engine
    /// measures <c>MaxLength</c> in.
    ///
    /// AUD-R78-02: SQL Server's <c>nchar</c>/<c>nvarchar(n)</c> is n UTF-16
    /// code units, so a character outside the BMP genuinely costs two and
    /// <see cref="string.Length"/> is the right count. PostgreSQL, MySQL and
    /// SQLite all measure <c>varchar(n)</c> in characters, where that same
    /// character costs one — counting code units there rejected valid SQL
    /// with a false-positive JNT5001 <em>error</em>.
    ///
    /// SQL Server's non-Unicode <c>varchar(n)</c> is n bytes, where a
    /// non-ASCII character may cost more than one, but how many depends on
    /// the column's collation, which the snapshot does not carry. Counting
    /// code units there is the conservative reading and is left unchanged.
    /// </summary>
    private static int MeasureLiteralLength(string decoded, string dialect)
    {
        if (string.Equals(dialect, "sqlserver", StringComparison.OrdinalIgnoreCase))
            return decoded.Length;

        int characters = 0;
        for (int i = 0; i < decoded.Length; i++)
        {
            characters++;
            if (char.IsHighSurrogate(decoded[i]) && i + 1 < decoded.Length && char.IsLowSurrogate(decoded[i + 1]))
                i++;
        }
        return characters;
    }

    private static void ValidateNumericLiteral(
        LiteralBinding lit, ColumnSchema column, string tableName, string dialect, List<ValidationError> errors)
    {
        if (!decimal.TryParse(lit.Value,
                System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out decimal value))
            return;

        // The integer families are settled FIRST, because Precision is not
        // exclusively a decimal facet: AUD-R20-01 folds MySQL's display-width-1
        // boolean signal into it (the channel bit(n)'s length already rides on)
        // while leaving DbType a bare "tinyint". The precision arm below is
        // ungated by type family, so running it first read that sentinel as
        // "at most one digit before the decimal point" and reported a JNT5002
        // Error for `level = 12` on a TINYINT(1) -- a value MySQL stores
        // happily, since the column's real range is -128..127. The range switch
        // is the single authority on which types are integer-shaped, so
        // deferring to it cannot drift the way a second hand-kept list would.
        (decimal Min, decimal Max)? integerRange = ResolveIntegerRange(column.DbType, dialect, column.Precision);
        if (integerRange.HasValue)
        {
            if (value < integerRange.Value.Min || value > integerRange.Value.Max)
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT5002,
                    $"Numeric literal {lit.Value} is out of range for {tableName}.{column.Name} ({column.DbType}). It would overflow at runtime."));
            }
            return;
        }

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
                return;
            }

            // AUD-R78-01: the fractional half of the same promise. This
            // method's own summary says literals are proven "against decimal
            // precision/scale", but only the integer part was ever checked --
            // a literal with more fractional digits than the column's scale
            // is rounded on write by every engine, silently, with no runtime
            // error to notice either. That is exactly the loss JNT5001
            // reports for an over-long string literal one arm above.
            //
            // Compared by VALUE, never by counting digits: 1.500 is written
            // with three fractional digits yet is exactly representable at
            // scale 2, and counting would make it a false positive. Scale
            // above 28 is skipped because System.Decimal cannot represent it
            // -- such a literal was already reshaped by decimal.TryParse
            // above, so the comparison would report the parser's rounding
            // rather than the column's.
            if (scale >= 0 && scale <= 28 && Math.Round(value, scale) != value)
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT5002,
                    $"Numeric literal {lit.Value} does not fit {tableName}.{column.Name} ({column.DbType}, precision {precision}, scale {scale}): " +
                    $"at most {scale} digit(s) after the decimal point. It would be rounded on write and can never match on read."));
            }
            return;
        }
    }

    /// <summary>
    /// The integer families' compile-time range, or null when the column is
    /// not integer-shaped. The base type is the text before any '(n)' facet or
    /// modifier word ("tinyint(1) unsigned" -> "tinyint"); MySQL's 'unsigned'
    /// modifier shifts the range.
    ///
    /// <para>
    /// <paramref name="columnPrecision"/> is consulted for exactly one type.
    /// MySQL's <c>bit(n)</c> holds an n-bit UNSIGNED integer, 0..2^n-1, but
    /// <c>NormalizeDbType</c> strips the <c>(n)</c> off the dbType string, so
    /// the width survives only in <c>Precision</c> — the same channel the
    /// <c>tinyint(1)</c> boolean sentinel rides (AUD-R20-01). Without an arm
    /// here a <c>bit(8)</c> fell through to the precision arm and was checked
    /// as "at most 8 digits", so it accepted 300 (overflows 0..255) and would
    /// have rejected nothing it should.
    /// </para>
    /// <para>
    /// Scoped to the mysql dialect by decision (2026-08-03), because
    /// <c>bit</c> is not one type across engines: SQL Server's is 0..1, and
    /// Postgres's <c>bit(n)</c> is a BIT STRING rather than a number at all.
    /// Those keep their existing behaviour and stay recorded in the todo list;
    /// widening this arm would commit JauntyQ to a per-dialect reading of
    /// <c>bit</c> that <c>DialectMapper</c> does not yet make anywhere.
    /// </para>
    /// </summary>
    private static (decimal Min, decimal Max)? ResolveIntegerRange(string columnDbType, string dialect, int? columnPrecision)
    {
        string fullDbType = columnDbType.ToLowerInvariant();
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
            // MySQL bit(n): unsigned, 0..2^n-1. Width comes from Precision
            // (see this method's summary). A null/out-of-band width yields no
            // arm at all rather than a guessed range -- an unprovable bound
            // must not become a JNT5002 Error. 64 is MySQL's own maximum.
            "bit" when mysqlDialect && columnPrecision is int bits && bits >= 1 && bits <= 64 =>
                (0m, TwoToThePowerMinusOne(bits)),
            _ => ((decimal, decimal)?)null
        };

        return range;
    }

    /// <summary>
    /// 2^<paramref name="bits"/> - 1 as an exact decimal, for bit widths up to
    /// 64. Doubled in decimal rather than via Math.Pow, whose double result
    /// stops being exact above 2^53 — a bit(64) bound computed that way would
    /// be wrong in the last digits and could accept a literal the column
    /// cannot hold.
    /// </summary>
    private static decimal TwoToThePowerMinusOne(int bits)
    {
        decimal max = 1m;
        for (int i = 0; i < bits; i++)
            max *= 2m;
        return max - 1m;
    }
}
