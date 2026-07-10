using Microsoft.CodeAnalysis;
using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;

/// <summary>
/// Cross-query N+1 heuristic (JNT8008). Offline and deterministic: pairs a
/// child point-lookup query — a SELECT filtered by an equality on every
/// column of a foreign key — with a parent-collection query over the FK's
/// referenced table. When both exist in the corpus, calling the child lookup
/// once per parent row is the classic 1 + N round-trip pattern, so the child
/// lookup gets a Warning suggesting the set-based fix (a join, or a batched
/// IN). Unlike the per-query JNT8001–8007 analyzers this needs the whole
/// query corpus plus the FK graph, so it runs at the aggregate stage in
/// <see cref="JauntyQGenerator"/> (beside the JNT8005 duplicate-query pass).
///
/// False-positive guards: a child that is already set-based (IN on the FK
/// column, or joins/reads the parent table) never fires; a parent query
/// narrowed to a single row by its primary key is not a collection; an
/// aggregate-only parent projection returns one row, not a collection; and
/// with no foreign keys in the snapshot the pass is inert.
/// </summary>
internal static class NPlusOneAnalyzer
{
    /// <summary>
    /// Analyzes the whole query corpus against the snapshot's FK graph and
    /// returns the JNT8008 diagnostics. <paramref name="corpus"/> entries with
    /// a null query (files that failed validation, @call procs, empty files)
    /// are skipped. At most one diagnostic is emitted per child lookup, and
    /// identical inputs always produce identical output (entries and FK groups
    /// are processed in sorted order).
    /// </summary>
    public static List<Diagnostic> Analyze(
        IReadOnlyList<(string Name, QueryModel? Query)> corpus,
        DatabaseSchema schema)
    {
        var diagnostics = new List<Diagnostic>();

        // Inert without FK metadata (FR-006), like JNT8006.
        if (schema.ForeignKeys.Count == 0)
            return diagnostics;

        var fkGroups = BuildFkGroups(schema);
        if (fkGroups.Count == 0)
            return diagnostics;

        // Classify each parsed SELECT once.
        var facts = new List<QueryFacts>();
        foreach (var (name, query) in corpus)
        {
            if (query == null || query.StatementType != StatementType.Select || query.Tables.Count == 0)
                continue;
            facts.Add(QueryFacts.Build(name, query, schema));
        }
        facts.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        // Parent-collection queries per table (lowercase table -> sorted names).
        var collections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            foreach (var tableKey in fact.TablesRead)
            {
                var tableSchema = FindTableSchema(schema, tableKey);
                if (tableSchema == null)
                    continue;
                if (IsSingleRowPkLookup(fact, tableKey, tableSchema) || fact.AggregateOnlyProjection)
                    continue;
                if (!collections.TryGetValue(tableKey, out var names))
                {
                    names = new List<string>();
                    collections[tableKey] = names;
                }
                names.Add(fact.Name); // facts are pre-sorted, so each list is too
            }
        }

        // Pair child point-lookups with parent collections via the FK graph.
        foreach (var fact in facts)
        {
            foreach (var group in fkGroups)
            {
                if (!IsChildPointLookup(fact, group))
                    continue;

                if (!collections.TryGetValue(group.ParentTableKey, out var parents))
                    continue;

                // A self-FK child lookup is itself a multi-row SELECT over the
                // parent table; it cannot be its own parent collection.
                string? parent = null;
                foreach (var candidate in parents)
                {
                    if (!string.Equals(candidate, fact.Name, StringComparison.Ordinal))
                    {
                        parent = candidate;
                        break;
                    }
                }
                if (parent == null)
                    continue;

                diagnostics.Add(Diagnostic.Create(JauntyDiagnostics.JNT8008, Location.None,
                    BuildMessage(fact, group, parent)));
                break; // at most once per child lookup (FR-007)
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// True when the query is a point lookup on the group's child table by the
    /// FULL foreign key (an equality parameter on every FK column; a partial
    /// composite match never counts), and is not already set-based on it: no
    /// IN-list on an FK column, no read of the parent table (a join to the
    /// parent always puts it in Tables), and for a self-FK no self-join.
    /// </summary>
    private static bool IsChildPointLookup(QueryFacts fact, FkGroup group)
    {
        if (!fact.TablesRead.Contains(group.ChildTableKey))
            return false;

        foreach (var column in group.ChildColumns)
        {
            string key = group.ChildTableKey + "|" + column.ToLowerInvariant();
            if (!fact.EqualityFilterColumns.Contains(key))
                return false; // composite FK requires ALL columns (FR-006)
            if (fact.InFilterColumns.Contains(key))
                return false; // already batched with IN (FR-004)
        }

        if (group.IsSelfReference)
        {
            // Reading the table twice (self-join) is already set-based.
            if (fact.TableRefCounts.TryGetValue(group.ChildTableKey, out int refs) && refs > 1)
                return false;
        }
        else if (fact.TablesRead.Contains(group.ParentTableKey))
        {
            return false; // joins (or otherwise reads) the parent: set-based (FR-004)
        }

        return true;
    }

    private static string BuildMessage(QueryFacts fact, FkGroup group, string parentQuery)
    {
        string columns = string.Join(", ", group.ChildColumns);

        if (fact.AggregateOnlyProjection)
        {
            return $"{fact.Name} computes a per-row aggregate on {group.ChildTable} filtered by its " +
                   $"foreign key to {group.ParentTable} ({columns}), and {parentQuery} returns a " +
                   $"{group.ParentTable} collection: calling it once per row is an N+1 query pattern. " +
                   $"Consider a grouped aggregate join (JOIN {group.ChildTable} ... GROUP BY {columns}) " +
                   $"or a batched WHERE {columns} IN (...).";
        }

        return $"{fact.Name} is a point lookup on {group.ChildTable} by its foreign key to " +
               $"{group.ParentTable} ({columns}), and {parentQuery} returns a {group.ParentTable} " +
               $"collection: calling it once per row is an N+1 query pattern. Fetch the rows " +
               $"set-based instead: join {group.ChildTable} into the parent query, or batch the " +
               $"lookups with WHERE {columns} IN (...).";
    }

    /// <summary>
    /// True when the query filters the given table to (at most) one row by an
    /// equality on every primary-key column — a single-row lookup, so there is
    /// no collection to iterate. Tables without a primary key never qualify.
    /// </summary>
    private static bool IsSingleRowPkLookup(QueryFacts fact, string tableKey, TableSchema tableSchema)
    {
        bool hasPk = false;
        foreach (var column in tableSchema.Columns.Values)
        {
            if (!column.IsPrimaryKey)
                continue;
            hasPk = true;
            if (!fact.EqualityFilterColumns.Contains(tableKey + "|" + column.Name.ToLowerInvariant()))
                return false;
        }
        return hasPk;
    }

    /// <summary>
    /// Groups the snapshot's per-column-pair FK rows into child→parent
    /// relationships keyed by (child table, parent table). A composite FK's
    /// rows share the pair and merge into one group requiring every column.
    /// Two genuinely distinct FKs between the same pair of tables also merge —
    /// a deliberate conservative choice: the merged group demands equality on
    /// the union of columns, so it can only under-fire, never over-fire.
    /// Groups and their columns are sorted for deterministic output.
    /// </summary>
    private static List<FkGroup> BuildFkGroups(DatabaseSchema schema)
    {
        var byPair = new Dictionary<string, FkGroup>(StringComparer.Ordinal);
        foreach (var fk in schema.ForeignKeys)
        {
            if (string.IsNullOrEmpty(fk.FromTable) || string.IsNullOrEmpty(fk.FromColumn) ||
                string.IsNullOrEmpty(fk.ToTable))
                continue;

            string childKey = fk.FromTable.ToLowerInvariant();
            string parentKey = fk.ToTable.ToLowerInvariant();
            string pairKey = childKey + "|" + parentKey;

            if (!byPair.TryGetValue(pairKey, out var group))
            {
                group = new FkGroup(fk.FromTable, childKey, fk.ToTable, parentKey);
                byPair[pairKey] = group;
            }
            if (!group.ChildColumns.Contains(fk.FromColumn, StringComparer.OrdinalIgnoreCase))
                group.ChildColumns.Add(fk.FromColumn);
        }

        var groups = new List<FkGroup>(byPair.Values);
        foreach (var group in groups)
            group.ChildColumns.Sort(StringComparer.OrdinalIgnoreCase);
        groups.Sort(static (a, b) =>
        {
            int byChild = string.CompareOrdinal(a.ChildTableKey, b.ChildTableKey);
            return byChild != 0 ? byChild : string.CompareOrdinal(a.ParentTableKey, b.ParentTableKey);
        });
        return groups;
    }

    private static TableSchema? FindTableSchema(DatabaseSchema schema, string tableName)
    {
        if (schema.Tables.TryGetValue(tableName, out var direct))
            return direct;
        foreach (var entry in schema.Tables)
        {
            if (string.Equals(entry.Key, tableName, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }
        return null;
    }

    /// <summary>One FK relationship child→parent (composite = multiple columns).</summary>
    private sealed class FkGroup
    {
        public string ChildTable { get; }
        public string ChildTableKey { get; }   // lowercase
        public string ParentTable { get; }
        public string ParentTableKey { get; }  // lowercase
        public List<string> ChildColumns { get; } = new List<string>();
        public bool IsSelfReference => ChildTableKey == ParentTableKey;

        public FkGroup(string childTable, string childTableKey, string parentTable, string parentTableKey)
        {
            ChildTable = childTable;
            ChildTableKey = childTableKey;
            ParentTable = parentTable;
            ParentTableKey = parentTableKey;
        }
    }

    /// <summary>
    /// Everything the pairing pass needs to know about one SELECT, computed in
    /// a single linear scan of its model (no per-pair re-parse).
    /// </summary>
    private sealed class QueryFacts
    {
        public string Name = string.Empty;

        /// <summary>Lowercased snapshot tables this statement reads in FROM/JOIN.</summary>
        public readonly HashSet<string> TablesRead = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>How many FROM/JOIN references each table has (self-join detection).</summary>
        public readonly Dictionary<string, int> TableRefCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>"table|column" (lowercase) pairs constrained by an equality parameter.</summary>
        public readonly HashSet<string> EqualityFilterColumns = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>"table|column" (lowercase) pairs constrained by an IN-list parameter.</summary>
        public readonly HashSet<string> InFilterColumns = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// True when every projection item is an expression and at least one is
        /// an aggregate (count/sum/avg/min/max): the scalar-per-parent N+1
        /// variant on the child side, and a single-row (non-collection) shape
        /// on the parent side.
        /// </summary>
        public bool AggregateOnlyProjection;

        public static QueryFacts Build(string name, QueryModel query, DatabaseSchema schema)
        {
            var facts = new QueryFacts { Name = name };

            // Same alias map the validator builds (later duplicates win).
            var aliasToTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in query.Tables)
            {
                string key = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
                aliasToTable[key] = table.TableName;

                if (FindTableSchema(schema, table.TableName) == null)
                    continue; // CTE/unknown: never judged
                string tableKey = table.TableName.ToLowerInvariant();
                facts.TablesRead.Add(tableKey);
                facts.TableRefCounts.TryGetValue(tableKey, out int count);
                facts.TableRefCounts[tableKey] = count + 1;
            }

            // Filter columns from parameter bindings, keyed by the operator the
            // parser captured. Unresolvable bindings (subquery-local aliases,
            // ambiguous unqualified names) are skipped — never guessed.
            foreach (var param in query.Parameters)
            {
                if (param.IsWriteTarget || string.IsNullOrEmpty(param.BoundColumnName))
                    continue;
                bool isEquality = param.ComparisonOp == "=";
                bool isInList = string.Equals(param.ComparisonOp, "IN", StringComparison.OrdinalIgnoreCase);
                if (!isEquality && !isInList)
                    continue;

                var column = QueryValidator.ResolveColumn(query, param.BoundTableAlias,
                    param.BoundColumnName, aliasToTable, schema, out string? tableName);
                if (column == null || tableName == null)
                    continue;

                string key = tableName.ToLowerInvariant() + "|" + column.Name.ToLowerInvariant();
                if (isEquality)
                    facts.EqualityFilterColumns.Add(key);
                else
                    facts.InFilterColumns.Add(key);
            }

            bool allExpressions = query.Columns.Count > 0;
            bool anyAggregate = false;
            foreach (var col in query.Columns)
            {
                if (!col.IsExpression)
                {
                    allExpressions = false;
                    break;
                }
                if (IsAggregateExpression(col))
                    anyAggregate = true;
            }
            facts.AggregateOnlyProjection = allExpressions && anyAggregate;

            return facts;
        }

        private static bool IsAggregateExpression(ColumnRef col)
        {
            if (!string.IsNullOrEmpty(col.AggregateFunction))
                return true; // SUM/AVG over a bare column, captured by the parser

            string sql = col.ExpressionSql.TrimStart();
            int end = 0;
            while (end < sql.Length && char.IsLetter(sql[end]))
                end++;
            string head = sql.Substring(0, end);
            return head.Equals("count", StringComparison.OrdinalIgnoreCase)
                || head.Equals("sum", StringComparison.OrdinalIgnoreCase)
                || head.Equals("avg", StringComparison.OrdinalIgnoreCase)
                || head.Equals("min", StringComparison.OrdinalIgnoreCase)
                || head.Equals("max", StringComparison.OrdinalIgnoreCase);
        }
    }
}
