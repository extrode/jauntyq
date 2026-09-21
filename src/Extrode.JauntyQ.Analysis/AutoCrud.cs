using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser;

namespace Extrode.JauntyQ.Analysis;

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

        /// <summary>
        /// The single column this synthetic filters on when that column is NOT
        /// the primary key — i.e. the foreign-key column of a GetBy&lt;Fk&gt;
        /// loader. Null for every other synthetic.
        ///
        /// Spec 016: this family is the only synthesis with a non-PK WHERE, so
        /// it is the only one that can raise JNT8004, and the acceptance
        /// sidecar keys on exactly this (table, column) pair. Carried as a
        /// field rather than re-derived by parsing Sql back out, because the
        /// value is already in hand at the construction site and a second
        /// derivation is a second thing to keep in step.
        ///
        /// Deliberately NOT set for GetById/Update/Delete: those filter the
        /// full primary key, which short-circuits IsColumnIndexSupported, so
        /// they cannot raise JNT8004 and must not be acceptable.
        /// </summary>
        public string? FilterColumn { get; }

        public SyntheticQuery(string entityName, string methodName, string sql, string tableName, bool isUpsert = false, string? filterColumn = null)
        {
            EntityName = entityName;
            MethodName = methodName;
            Sql = sql;
            TableName = tableName;
            IsUpsert = isUpsert;
            FilterColumn = filterColumn;
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
            // Stryker disable once all : NoCoverage, no caller passes an empty column list; documented equivalent floor
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
            if (DescribeTableName(table.Name, schema.Dialect) != null)
                continue;

            var columns = new List<ColumnSchema>();
            bool allColumnsUsable = true;
            foreach (var col in table.Columns.Values)
            {
                if (!IsBareIdentifier(col.Name, schema.Dialect, SqlIdentifierPosition.Column))
                {
                    allColumnsUsable = false;
                    // Stryker disable once Statement : the loop's accumulated `columns` list is discarded on this failure path regardless of whether the loop breaks early or keeps iterating
                    break;
                }
                columns.Add(col);
            }
            if (!allColumnsUsable || columns.Count == 0)
                continue;

            // AUD-R64-01 (fix 2): two distinct, individually-legal column
            // names (e.g. "order_number"/"OrderNumber") that fold to the
            // same PascalCased property name would make CodeEmitter's shared
            // row POCO for this table (and, for a stored proc, its own
            // Result DTO) declare a duplicate member -- caught and reported
            // as JNT2011 by JauntyQGenerator.Part4.cs's own row-POCO
            // emission loop, and by ResolveCanonicalRowType refusing to
            // route ANY query (AutoCrud-synthesized or hand-written) to a
            // row type it cannot safely emit. Skip synthesizing CRUD for
            // this table entirely here too, at the source: a synthetic
            // Insert/Update/Delete's POCO overload (EmitPocoOverloads) and
            // BulkInsert both unconditionally reference this table's row
            // type by name, so still emitting them here would leave a
            // dangling reference to a type JauntyQGenerator will not emit
            // for this exact reason.
            if (HasColumnNameCollisionAfterPascalCase(columns))
                continue;

            string entityName = DialectMapper.ToPascalCase(table.Name);
            string colList = JoinColumns(columns, ", ", c => c.Name);

            // GetAll — always (works for views and PK-less tables too)
            result.Add(new SyntheticQuery(entityName, "GetAll",
                $"SELECT {colList}\nFROM {table.Name}", table.Name));

            var pkCols = CrudColumnRules.PrimaryKeyColumns(columns);

            // FK loaders: one GetBy<FkColumn> per foreign-key column on this
            // table (composite FKs yield one loader per column). Skipped when
            // the column IS the sole primary key (GetById already covers it).
            var fkColumnsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in schema.ForeignKeys)
            {
                if (!string.Equals(fk.FromTable, table.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsBareIdentifier(fk.FromColumn, schema.Dialect, SqlIdentifierPosition.Column) || !fkColumnsSeen.Add(fk.FromColumn))
                    continue;
                if (pkCols.Count == 1 && string.Equals(pkCols[0].Name, fk.FromColumn, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!table.Columns.ContainsKey(fk.FromColumn))
                    continue;

                result.Add(new SyntheticQuery(entityName, $"GetBy{DialectMapper.ToPascalCase(fk.FromColumn)}",
                    $"SELECT {colList}\nFROM {table.Name}\nWHERE {table.Name}.{fk.FromColumn} = @{fk.FromColumn}", table.Name,
                    filterColumn: fk.FromColumn));
            }

            // Spec 015: a view is read-only whatever its columns look like.
            // The pkCols gate below already stops writes for anything without
            // a primary key, and an extracted view has none -- but that is a
            // consequence, not a guarantee. A hand-authored or partially
            // hand-edited snapshot can mark a view's column isPrimaryKey, and
            // then every write below would be synthesized against a relation
            // the engine refuses. Gate on what the relation IS, not on what its
            // columns happen to say.
            if (table.IsView || pkCols.Count == 0)
                continue; // views / heap tables: read-only beyond GetAll (+ FK loaders)

            string pkWhere = JoinColumns(pkCols, " AND ", c => $"{table.Name}.{c.Name} = @{c.Name}");

            // GetById — single row by primary key
            result.Add(new SyntheticQuery(entityName, "GetById",
                $"-- @first\nSELECT {colList}\nFROM {table.Name}\nWHERE {pkWhere}", table.Name));

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
                    $"{prefix}INSERT INTO {table.Name} ({insertColList})\nVALUES ({insertParams})", table.Name));
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
                string updateWhere = JoinColumns(whereCols, " AND ", c => $"{c.Name} = @{c.Name}");
                result.Add(new SyntheticQuery(entityName, "Update",
                    $"UPDATE {table.Name}\nSET {setList}\nWHERE {updateWhere}", table.Name));
            }

            // Delete — WHERE the full primary key (plus rowversion token)
            string deleteWhere = JoinColumns(whereCols, " AND ", c => $"{c.Name} = @{c.Name}");
            result.Add(new SyntheticQuery(entityName, "Delete",
                $"DELETE FROM {table.Name}\nWHERE {deleteWhere}", table.Name));

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
            // AUD-R37-01: a key resolved post-AUD-R36-01 may legitimately
            // contain a Computed/RowVersion primary-key column (matching
            // AutoCrud's own PK detection for this same table) that
            // CrudColumnRules.UpsertColumns nonetheless excludes outright,
            // with no "unless part of key" escape hatch (unlike Identity) --
            // no dialect accepts an explicit INSERT value for either, so
            // EmitUpsert's SQL Server MERGE branch would reference that
            // column in its "on"/src-derived-table clause without it ever
            // having been selected, and a live engine rejects the query at
            // runtime ("Invalid column name", confirmed via Testcontainers).
            // Skip Upsert synthesis for that table entirely, the same
            // outcome as "no usable key at all".
            bool keyIsBindable = upsertKey == null || !CrudColumnRules.HasUnbindableUpsertKeyColumn(columns, upsertKey);
            if (upsertKey != null && hasNonKeyColumns && keyIsBindable && !string.IsNullOrEmpty(schema.Dialect))
            {
                result.Add(new SyntheticQuery(entityName, "Upsert", "", table.Name, isUpsert: true));
            }
        }

        return result;
    }

    /// <summary>
    /// True when <paramref name="name"/> can be emitted unquoted into synthetic
    /// SQL for <paramref name="dialect"/> and still round-trip correctly.
    /// Beyond the lexical bare-identifier shape, a name that collides with a
    /// SQL reserved word (e.g. a column literally named <c>Group</c> or
    /// <c>Order</c>) also fails this check: unquoted, it tokenizes as a
    /// keyword rather than an identifier, which silently desyncs the
    /// positional column/parameter binding in <c>ParseInsert</c>'s column
    /// list — a wrong-typed-parameter bug, not a clean compile error, for
    /// every column after it. v1 has no quoting support, so such
    /// tables/columns are skipped rather than emitted broken.
    ///
    /// AUD-R64-01: this used to check only <see
    /// cref="SqlTokenizer.IsReservedKeyword"/> — JauntyQ's OWN ~55-word
    /// internal keyword list, which models the subset of SQL this project's
    /// own tokenizer understands, not the TARGET ENGINE's actual reserved-word
    /// grammar. A column named <c>user</c> or <c>key</c> is not a JauntyQ
    /// keyword, so it passed this gate, but PostgreSQL/MySQL/SQL Server all
    /// reserve those words: emitted bare, PostgreSQL silently parses
    /// <c>user</c> as the niladic <c>current_user</c> function (wrong data on
    /// every row, zero diagnostic anywhere — verified live against
    /// postgres:16), MySQL/SQL Server raise a runtime syntax error instead
    /// (verified live against mysql:8.0). Now also checks <see
    /// cref="DialectReservedWords.IsReservedInDialect(string, string?, SqlIdentifierPosition)"/> — a second,
    /// independent gate from the JauntyQ-tokenizer one, since a name must
    /// clear both. Separately, PostgreSQL lower-cases every unquoted
    /// identifier it parses, so a name created quoted with any uppercase
    /// letter can never be safely referenced bare either (<see
    /// cref="DialectReservedWords.RequiresQuotingForCase"/>, PostgreSQL-only —
    /// SQL Server/SQLite's unquoted matching is case-insensitive with no
    /// silent rename, so this does not generalize to other dialects).
    /// </summary>
    private static bool IsBareIdentifier(string name, string? dialect, SqlIdentifierPosition position)
        => DescribeIdentifier(name, dialect, position) == null;

    /// <summary>
    /// Why <paramref name="name"/> cannot be used as a table name here, or null
    /// if it can.
    ///
    /// A table name is not only an object name. GetById and every FK loader
    /// qualify the column in the WHERE clause with it -- <c>WHERE raise.id =
    /// @id</c> -- and that is expression position, where the column-only
    /// reservations bite. Verified live against SQLite 3.45.3: with the table
    /// created as <c>"raise"</c>, <c>SELECT id, nm FROM raise</c> parses, while
    /// <c>SELECT id, nm FROM raise WHERE raise.id = 1</c> is
    /// <c>near ".": syntax error</c>. Same for current_date, current_time and
    /// current_timestamp. The original probe only exercised the FROM position,
    /// so it did not see this; an independent review caught it and the probe was
    /// re-run in the qualified form to confirm.
    ///
    /// So a table name must clear BOTH positions. Only column names can use the
    /// split, which is where it pays for itself: MySQL reserves <c>value</c> as
    /// an object name but not as a column name.
    /// </summary>
    private static string? DescribeTableName(string name, string? dialect)
        => DescribeIdentifier(name, dialect, SqlIdentifierPosition.Object)
        ?? DescribeIdentifier(name, dialect, SqlIdentifierPosition.Column);

    /// <summary>
    /// AUD-R64-01 (T8 residual, 2026-07-29): the reason <paramref name="name"/>
    /// cannot be emitted bare, or null when it can. <see
    /// cref="IsBareIdentifier"/> is defined as "this returned null", so the
    /// gate and the explanation of the gate are one piece of code and cannot
    /// drift — the drift that produced this finding in the first place.
    ///
    /// The returned fragment completes the sentence "...because its column
    /// 'foo' <c>{fragment}</c>." and is user-facing (JNT2015).
    /// </summary>
    private static string? DescribeIdentifier(string name, string? dialect, SqlIdentifierPosition position)
    {
        if (string.IsNullOrEmpty(name))
            return "is empty";
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return "does not begin with a letter or underscore, so it cannot be written unquoted";
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return $"contains '{name[i]}', which cannot appear in an unquoted SQL identifier";
        }
        if (SqlTokenizer.IsReservedKeyword(name))
            return "is a keyword JauntyQ's own SQL parser reserves, so the synthesized statement would not parse";
        if (DialectReservedWords.IsReservedInDialect(name, dialect, position))
        {
            string where = position == SqlIdentifierPosition.Column ? "as a column name" : "as an object name";
            return $"is reserved by {dialect} {where} (verified against a live engine), so the synthesized statement would be rejected";
        }
        if (DialectReservedWords.RequiresQuotingForCase(name, dialect))
            return "contains uppercase letters, and PostgreSQL folds an unquoted reference to lower case, so it would resolve to a different object or none at all";
        return null;
    }

    /// <summary>
    /// AUD-R64-01 (T8 residual, 2026-07-29): why <see cref="Synthesize"/> will
    /// refuse to emit any CRUD for <paramref name="table"/>, or null when it
    /// will not refuse. Exposed so <c>JauntyQGenerator</c> can turn what was a
    /// silent <c>continue</c> — a table simply absent from the generated API,
    /// with nothing anywhere saying why — into JNT2015.
    ///
    /// This does not cover the folded-column-name collision, which has its own
    /// diagnostic (JNT2014) and its own message.
    /// </summary>
    public static string? DescribeUnusableTable(TableSchema table, string? dialect)
    {
        string? why = DescribeTableName(table.Name, dialect);
        if (why != null)
            return $"its name {why}";

        // Synthesize also skips a table with no usable columns at all, and that
        // skip was silent for the same reason the others were. Reachable from a
        // hand-authored or partially-extracted schema snapshot.
        if (table.Columns.Count == 0)
            return "has no columns, so there is nothing to select, insert or update";

        foreach (var col in table.Columns.Values)
        {
            why = DescribeIdentifier(col.Name, dialect, SqlIdentifierPosition.Column);
            if (why != null)
                return $"its column '{col.Name}' {why}";
        }

        return null;
    }

    /// <summary>
    /// True when two columns in <paramref name="columns"/> fold to the same
    /// <see cref="DialectMapper.ToPascalCase"/> result (e.g.
    /// "order_number"/"OrderNumber") -- the same emitted-member-name shape
    /// <c>JauntyQGenerator</c>'s own JNT2011/JNT3009 checks guard for the
    /// canonical row POCO, a query's own projection type, and a stored
    /// proc's Result DTO. Checked here too so a colliding table's ENTIRE
    /// synthetic CRUD surface is skipped at the source, not just its
    /// row-returning queries: a synthetic Insert/Update/Delete's POCO
    /// overload and BulkInsert both unconditionally reference this table's
    /// shared row type by name.
    /// </summary>
    private static bool HasColumnNameCollisionAfterPascalCase(List<ColumnSchema> columns)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var col in columns)
        {
            if (!seen.Add(DialectMapper.ToPascalCase(col.Name)))
                return true;
        }
        return false;
    }
}
