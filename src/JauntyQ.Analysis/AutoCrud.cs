using JauntyQ.Schema;
using JauntyQ.SqlParser;

namespace JauntyQ.Analysis;

/// <summary>
/// Synthesizes CRUD SQL for every table in the schema snapshot so consumers get
/// GetAll / GetById / Insert / Update / Delete without writing a line of SQL.
/// The synthesized SQL is fed through the exact same pipeline as user .sql files
/// (tokenizer, parser, validator, emitter), so directives-driven behavior
/// (@first on GetById), async twins, the shape guard and DbType binding all
/// apply uniformly. A user .sql file with the same entity + method name always
/// wins over the synthetic one.
/// </summary>
public static class AutoCrud
{
    public sealed class SyntheticQuery
    {
        public string EntityName { get; }
        public string MethodName { get; }
        public string Sql { get; }
        public string TableName { get; }

        /// <summary>
        /// Upsert synthetics bypass the SQL parser (MERGE / ON CONFLICT are
        /// outside the minimal grammar); the generator emits them directly
        /// via CodeEmitter.EmitUpsert. Sql is empty for these.
        /// </summary>
        public bool IsUpsert { get; }

        public SyntheticQuery(string entityName, string methodName, string sql, string tableName, bool isUpsert = false)
        {
            EntityName = entityName;
            MethodName = methodName;
            Sql = sql;
            TableName = tableName;
            IsUpsert = isUpsert;
        }
    }

    /// <summary>
    /// Builds "sep"-joined text from each column via <paramref name="selector"/>
    /// without pulling in System.Linq (product code stays reflection/LINQ-free
    /// for NativeAOT compatibility). string.Join(string, IEnumerable&lt;string&gt;)
    /// is a plain System.String member, not an Enumerable extension method.
    /// </summary>
    private static string JoinColumns(List<ColumnSchema> cols, string separator, System.Func<ColumnSchema, string> selector)
    {
        if (cols.Count == 0)
            return "";
        var parts = new List<string>(cols.Count);
        foreach (var c in cols)
            parts.Add(selector(c));
        return string.Join(separator, parts);
    }

    public static List<SyntheticQuery> Synthesize(DatabaseSchema schema)
    {
        var result = new List<SyntheticQuery>();

        foreach (var table in schema.Tables.Values)
        {
            // v1 synthesizes bare (unquoted) identifiers so the SQL is exactly
            // what a user would write by hand and flows through the minimal
            // parser unchanged. Tables/columns that need quoting are skipped.
            if (!IsBareIdentifier(table.Name))
                continue;

            var columns = new List<ColumnSchema>();
            bool allColumnsUsable = true;
            foreach (var col in table.Columns.Values)
            {
                if (!IsBareIdentifier(col.Name))
                {
                    allColumnsUsable = false;
                    break;
                }
                columns.Add(col);
            }
            if (!allColumnsUsable || columns.Count == 0)
                continue;

            string entityName = DialectMapper.ToPascalCase(table.Name);
            string colList = JoinColumns(columns, ", ", c => c.Name);

            // GetAll — always (works for views and PK-less tables too)
            result.Add(new SyntheticQuery(entityName, "GetAll",
                $"select {colList}\nfrom {table.Name}", table.Name));

            var pkCols = CrudColumnRules.PrimaryKeyColumns(columns);

            // FK loaders: one GetBy<FkColumn> per foreign-key column on this
            // table (composite FKs yield one loader per column). Skipped when
            // the column IS the sole primary key (GetById already covers it).
            var fkColumnsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in schema.ForeignKeys)
            {
                if (!string.Equals(fk.FromTable, table.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsBareIdentifier(fk.FromColumn) || !fkColumnsSeen.Add(fk.FromColumn))
                    continue;
                if (pkCols.Count == 1 && string.Equals(pkCols[0].Name, fk.FromColumn, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!table.Columns.ContainsKey(fk.FromColumn))
                    continue;

                result.Add(new SyntheticQuery(entityName, $"GetBy{DialectMapper.ToPascalCase(fk.FromColumn)}",
                    $"select {colList}\nfrom {table.Name}\nwhere {table.Name}.{fk.FromColumn} = @{fk.FromColumn}", table.Name));
            }

            if (pkCols.Count == 0)
                continue; // views / heap tables: read-only beyond GetAll (+ FK loaders)

            string pkWhere = JoinColumns(pkCols, " and ", c => $"{table.Name}.{c.Name} = @{c.Name}");

            // GetById — single row by primary key
            result.Add(new SyntheticQuery(entityName, "GetById",
                $"-- @first\nselect {colList}\nfrom {table.Name}\nwhere {pkWhere}", table.Name));

            // Optimistic concurrency: rowversion columns are database-assigned
            // tokens — never inserted or updated, but required in the WHERE of
            // Update/Delete so a stale read can't overwrite a newer write
            // (0 rows affected = conflict).
            var versionCols = CrudColumnRules.RowVersionColumns(columns);

            // Insert — identity columns are database-assigned, never bound.
            // When the table has a single identity key and the snapshot knows
            // the dialect, the synthetic Insert returns the new id (-- @identity).
            var insertCols = CrudColumnRules.InsertableColumns(columns);
            if (insertCols.Count > 0)
            {
                string insertColList = JoinColumns(insertCols, ", ", c => c.Name);
                string insertParams = JoinColumns(insertCols, ", ", c => $"@{c.Name}");
                bool returnsIdentity = !string.IsNullOrEmpty(schema.Dialect) && CrudColumnRules.SingleIdentityColumn(columns) != null;
                string prefix = returnsIdentity ? "-- @identity\n" : "";
                result.Add(new SyntheticQuery(entityName, "Insert",
                    $"{prefix}insert into {table.Name} ({insertColList})\nvalues ({insertParams})", table.Name));
            }

            // Update — SET every non-PK column, WHERE the full primary key
            // plus the rowversion token when the table has one. A non-PK
            // identity column (e.g. a separate auto-increment sequence
            // column alongside a natural-key PK) is also excluded: it's
            // still database-assigned even though it isn't the key, and
            // every dialect tested (confirmed live: SQL Server) rejects an
            // UPDATE that targets an identity column outright ("Cannot
            // update identity column '...'"). Must mirror CodeEmitter.
            // Part7.cs's EmitPocoOverloads setCols exactly -- that list
            // supplies the Update(row) POCO overload's forwarded arguments
            // and has to match this SQL's @parameter list one-for-one.
            var setCols = CrudColumnRules.UpdatableColumns(columns);
            var whereCols = new List<ColumnSchema>(pkCols.Count + versionCols.Count);
            whereCols.AddRange(pkCols);
            whereCols.AddRange(versionCols);
            if (setCols.Count > 0)
            {
                string setList = JoinColumns(setCols, ", ", c => $"{c.Name} = @{c.Name}");
                string updateWhere = JoinColumns(whereCols, " and ", c => $"{c.Name} = @{c.Name}");
                result.Add(new SyntheticQuery(entityName, "Update",
                    $"update {table.Name}\nset {setList}\nwhere {updateWhere}", table.Name));
            }

            // Delete — WHERE the full primary key (plus rowversion token)
            string deleteWhere = JoinColumns(whereCols, " and ", c => $"{c.Name} = @{c.Name}");
            result.Add(new SyntheticQuery(entityName, "Delete",
                $"delete from {table.Name}\nwhere {deleteWhere}", table.Name));

            // Upsert — dialect-native, keyed on the PK, or (when the PK is
            // entirely database-assigned) on a secondary UNIQUE index
            // instead (UpsertKeyResolver.Resolve — e.g. an idempotency-key
            // column on an identity-PK queue table). Skipped when there's no
            // usable key at all, or no non-key columns to update. Rowversion
            // columns are excluded inside EmitUpsert; upsert is deliberately
            // last-writer-wins (documented).
            var upsertKey = UpsertKeyResolver.Resolve(table);
            // CrudColumnRules.UpsertColumns/UpsertSetColumns are the same
            // filtering CodeEmitter.Part6.cs's EmitUpsert and Part7.cs's
            // EmitPocoOverloads apply to build the actual MERGE/ON CONFLICT
            // SQL -- reusing them here (rather than a separately-written
            // existence check) is what keeps this gate in sync with what
            // EmitUpsert would actually do. A table whose only non-key
            // column is a lone non-key identity column (e.g. an identity id
            // plus a single idempotency-key unique column, or a natural-PK
            // table with a lone non-PK identity column) has zero SET
            // columns: EmitUpsert would otherwise emit invalid SQL with a
            // dangling "update set" / "do update set" and nothing after it,
            // or SQL that writes to the identity column directly -- the
            // database rejects both at runtime.
            bool hasNonKeyColumns = upsertKey != null
                && CrudColumnRules.UpsertSetColumns(CrudColumnRules.UpsertColumns(columns, upsertKey), upsertKey).Count > 0;
            if (upsertKey != null && hasNonKeyColumns && !string.IsNullOrEmpty(schema.Dialect))
            {
                result.Add(new SyntheticQuery(entityName, "Upsert", "", table.Name, isUpsert: true));
            }
        }

        return result;
    }

    /// <summary>
    /// True when <paramref name="name"/> can be emitted unquoted into synthetic
    /// SQL and still round-trip correctly. Beyond the lexical bare-identifier
    /// shape, a name that collides with a SQL reserved word (e.g. a column
    /// literally named <c>Group</c> or <c>Order</c>) also fails this check:
    /// unquoted, it tokenizes as a keyword rather than an identifier, which
    /// silently desyncs the positional column/parameter binding in
    /// <c>ParseInsert</c>'s column list — a wrong-typed-parameter bug, not a
    /// clean compile error, for every column after it. v1 has no quoting
    /// support, so such tables/columns are skipped rather than emitted broken.
    /// </summary>
    private static bool IsBareIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;
        }
        return !SqlTokenizer.IsReservedKeyword(name);
    }
}
