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
    /// Manual replacement for Enumerable.Contains(list, value, comparer) --
    /// List&lt;T&gt; has no native overload taking a comparer, and product code
    /// stays System.Linq-free for NativeAOT compatibility.
    /// </summary>
    private static bool Contains(List<string> list, string value, IEqualityComparer<string> comparer)
    {
        foreach (var item in list)
        {
            if (comparer.Equals(item, value))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Analyzes the whole query corpus against the snapshot's FK graph and
    /// returns the JNT8008 diagnostics. <paramref name="corpus"/> entries with
    /// a null query (files that failed validation, @call procs, empty files)
    /// are skipped. At most one diagnostic is emitted per child lookup, and
    /// identical inputs always produce identical output (entries and FK groups
    /// are processed in sorted order).
    /// </summary>
    public static List<Diagnostic> Analyze(
        IReadOnlyList<(string Name, string? Path, QueryModel? Query)> corpus,
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
        foreach (var (name, path, query) in corpus)
        {
            if (query == null || query.StatementType != StatementType.Select || query.Tables.Count == 0)
                continue;
            facts.Add(QueryFacts.Build(name, path, query, schema));
        }
        facts.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        // Name -> facts, so a candidate parent collection can be inspected (e.g.
        // to see whether it already joins the child table).
        var factsByName = new Dictionary<string, QueryFacts>(StringComparer.Ordinal);
        foreach (var fact in facts)
            factsByName[fact.Name] = fact;

        // Parent-collection queries per table (lowercase table -> sorted names).
        var collections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            // A query pinned to a single row by a unique key on ANY table it
            // reads (inner-joined), or projecting only an aggregate, returns at
            // most one row -- it is not a parent collection for any of its
            // tables. Checking every read table (not just each in isolation)
            // catches lookups whose uniqueness lives on a joined table, e.g. a
            // user fetched via a unique email (WHERE e.realm_id = @r AND
            // e.value = @v, unique on (realm_id, value)).
            if (fact.AggregateOnlyProjection || IsSingleRowOverall(fact, schema))
                continue;
            foreach (var tableKey in fact.TablesRead)
            {
                if (FindTableSchema(schema, tableKey) == null)
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

                // A lookup whose FK equality co-occurs with a unique key that
                // includes a column BEYOND the FK returns at most one row via
                // that more selective key: the FK is a scoping predicate (a
                // tenant/realm_id column alongside a unique business key like
                // username), not a per-parent-row N+1 driver. A lookup keyed
                // SOLELY by the FK (unique key == the FK columns) still fires.
                var childSchema = FindTableSchema(schema, group.ChildTableKey);
                if (childSchema != null && IsScopingChildLookup(fact, group, childSchema))
                    continue;

                // A lookup that pins full FKs to two or more DISTINCT parent
                // tables is scoped by that intersection, not iterating one
                // parent's children -- e.g. a user's sessions filtered by
                // (user_id, application_id). Which single parent would you even
                // loop? Not an N+1 driver for any of them.
                if (CountDistinctFilteredParentFks(fact, fkGroups, group.ChildTableKey) >= 2)
                    continue;

                if (!collections.TryGetValue(group.ParentTableKey, out var parents))
                    continue;

                // Pick a representative parent collection, skipping (a) this same
                // query and (b) any collection that already reads the child table
                // -- if the parent query joins the child in, the child rows are
                // already there set-based, so there is no per-row lookup to batch.
                string? parent = null;
                foreach (var candidate in parents)
                {
                    if (string.Equals(candidate, fact.Name, StringComparison.Ordinal))
                        continue;
                    // For a self-FK the parent IS the child table, so "reads the
                    // child" is trivially true and must not disqualify -- only a
                    // cross-table collection that joins the child in is exempt.
                    if (!group.IsSelfReference
                        && factsByName.TryGetValue(candidate, out var candidateFact)
                        && candidateFact.TablesRead.Contains(group.ChildTableKey))
                        continue;
                    parent = candidate;
                    break;
                }
                if (parent == null)
                    continue;

                // Anchor to the child lookup's .sql file so the IDE can navigate.
                diagnostics.Add(Diagnostic.Create(JauntyDiagnostics.JNT8008, JauntyQGenerator.FileLocation(fact.Path),
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
    /// True when the query is constrained to at most one row by a unique key on
    /// ANY table it reads — including a unique key on an inner-joined table (e.g.
    /// a user fetched through a unique email address). Such a query is not a
    /// parent collection to iterate.
    /// </summary>
    private static bool IsSingleRowOverall(QueryFacts fact, DatabaseSchema schema)
    {
        foreach (var tableKey in fact.TablesRead)
        {
            var tableSchema = FindTableSchema(schema, tableKey);
            if (tableSchema != null && IsSingleRowUniqueLookup(fact, tableKey, tableSchema))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the query filters the given table to (at most) one row by an
    /// equality on every column of SOME unique key — its primary key or any
    /// unique index. Such a query returns a single row, so on the parent side it
    /// is not a collection to iterate. Tables with no primary key and no unique
    /// index never qualify. (Filtering by a unique business key such as
    /// (realm_id, username) counts, not just the surrogate primary key.)
    /// </summary>
    private static bool IsSingleRowUniqueLookup(QueryFacts fact, string tableKey, TableSchema tableSchema)
    {
        // Primary key: single-row when every PK column is equality-filtered.
        bool hasPk = false;
        bool pkFullyFiltered = true;
        foreach (var column in tableSchema.Columns.Values)
        {
            if (!column.IsPrimaryKey)
                continue;
            hasPk = true;
            if (!fact.EqualityFilterColumns.Contains(tableKey + "|" + column.Name.ToLowerInvariant()))
            {
                pkFullyFiltered = false;
                break;
            }
        }
        if (hasPk && pkFullyFiltered)
            return true;

        // Any unique index: single-row when every one of its key columns is
        // equality-filtered.
        foreach (var index in tableSchema.Indexes)
        {
            if (!index.IsUnique || index.Columns.Count == 0)
                continue;
            bool allFiltered = true;
            foreach (var column in index.Columns)
            {
                if (!fact.EqualityFilterColumns.Contains(tableKey + "|" + column.ToLowerInvariant()))
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
    /// True when the child lookup is constrained to a single row by a unique key
    /// (primary key or any unique index) whose columns include at least one
    /// column BEYOND the group's foreign-key columns. In that case the FK
    /// equality is a scoping predicate — a tenant/realm discriminator sitting
    /// alongside a more selective unique column that actually picks the row — so
    /// the query is a scoped point read, not an N+1 driver over the parent
    /// collection. A lookup whose unique key is EXACTLY the FK columns (so the FK
    /// alone determines the row) is NOT scoping and still fires.
    /// </summary>
    private static bool IsScopingChildLookup(QueryFacts fact, FkGroup group, TableSchema tableSchema)
    {
        // The primary key as one candidate unique-key column set.
        var pkColumns = new List<string>();
        foreach (var column in tableSchema.Columns.Values)
        {
            if (column.IsPrimaryKey)
                pkColumns.Add(column.Name);
        }
        if (UniqueKeyMakesScoping(fact, group, pkColumns))
            return true;

        foreach (var index in tableSchema.Indexes)
        {
            if (index.IsUnique && UniqueKeyMakesScoping(fact, group, index.Columns))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when every column of <paramref name="keyColumns"/> is equality-filtered
    /// (the query is single-row by this unique key) AND at least one of those
    /// columns is not a foreign-key column of <paramref name="group"/> (so a
    /// non-FK column is what makes it selective). Empty key sets never qualify.
    /// </summary>
    private static bool UniqueKeyMakesScoping(QueryFacts fact, FkGroup group, List<string> keyColumns)
    {
        if (keyColumns.Count == 0)
            return false;

        bool hasColumnBeyondFk = false;
        foreach (var column in keyColumns)
        {
            if (!fact.EqualityFilterColumns.Contains(group.ChildTableKey + "|" + column.ToLowerInvariant()))
                return false; // key not fully constrained: not single-row by it
            if (!Contains(group.ChildColumns, column, StringComparer.OrdinalIgnoreCase))
                hasColumnBeyondFk = true;
        }
        return hasColumnBeyondFk;
    }

    /// <summary>
    /// Counts the distinct parent tables that <paramref name="fact"/> pins with a
    /// full foreign key (every FK column equality-filtered) among the FK groups
    /// whose child is <paramref name="childTableKey"/>. Two or more means the
    /// query is scoped by an intersection of parents, not iterating one parent's
    /// children.
    /// </summary>
    private static int CountDistinctFilteredParentFks(QueryFacts fact, List<FkGroup> fkGroups, string childTableKey)
    {
        var parents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in fkGroups)
        {
            if (group.ChildTableKey != childTableKey)
                continue;
            bool allFiltered = true;
            foreach (var column in group.ChildColumns)
            {
                if (!fact.EqualityFilterColumns.Contains(childTableKey + "|" + column.ToLowerInvariant()))
                {
                    allFiltered = false;
                    break;
                }
            }
            if (allFiltered)
                parents.Add(group.ParentTableKey);
        }
        return parents.Count;
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
            if (!Contains(group.ChildColumns, fk.FromColumn, StringComparer.OrdinalIgnoreCase))
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

        /// <summary>The child lookup's .sql path (JNT8008 diagnostic anchor).</summary>
        public string? Path;

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

        public static QueryFacts Build(string name, string? path, QueryModel query, DatabaseSchema schema)
        {
            var facts = new QueryFacts { Name = name, Path = path };

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
