using JauntyQ.Schema;

namespace JauntyQ.Analysis;

/// <summary>
/// Resolves the column set an auto-CRUD Upsert matches on before deciding
/// insert-vs-update: the primary key when at least one PK column is not
/// database-assigned, otherwise the first secondary UNIQUE index whose columns
/// are all real, non-identity columns (an identity-only PK has no value to
/// match on before the row exists — e.g. a queue table keyed by an idempotency
/// token instead). Null when neither exists: callers skip Upsert synthesis for
/// that table. Schema-only, Roslyn-free, so both <c>AutoCrud</c> and the Roslyn
/// <c>CodeEmitter</c> share one implementation.
///
/// A key enforced ONLY by a prefix UNIQUE (<see
/// cref="IndexSchema.HasPrefixKeyPart"/> — MySQL <c>UNIQUE (email(5))</c>) is
/// refused outright (decided 2026-07-30): the engine matches rows sharing only
/// the prefix, full-value semantics the emitted method's signature implies but
/// the index does not enforce, so no Upsert form — atomic or two-statement —
/// can honour the contract. The <c>out</c> overload names the refusing index so
/// the generator can report it (JNT2019) instead of a silent skip.
/// </summary>
public static class UpsertKeyResolver
{
    public static List<ColumnSchema>? Resolve(TableSchema tableSchema)
        => Resolve(tableSchema, out _);

    /// <param name="tableSchema">The table to resolve an upsert key for.</param>
    /// <param name="prefixOnlyKey">Non-null exactly when the return value is
    /// null BECAUSE the only constraint that could have served as the key is a
    /// prefix UNIQUE: the index that would have been chosen (fallback path) or
    /// the prefix-flagged restatement of the PK (PK path). Null both when a key
    /// resolves and when there is genuinely no key.</param>
    public static List<ColumnSchema>? Resolve(TableSchema tableSchema, out IndexSchema? prefixOnlyKey)
    {
        prefixOnlyKey = null;
        // AUD-R36-01: the primary key itself must be read straight off
        // tableSchema.Columns -- the same, unfiltered source CrudColumnRules.
        // PrimaryKeyColumns(...) uses for AutoCrud's GetById/Update/Delete
        // WHERE-key resolution against this very same table. A PK column
        // that is ALSO flagged IsComputed (a real SQL Server pattern: a
        // deterministic PERSISTED computed column may be part of a PRIMARY
        // KEY constraint) or IsRowVersion would otherwise be silently
        // dropped from consideration here -- making this resolver disagree
        // with AutoCrud's own already-correct PK detection for the exact
        // same table in the exact same Synthesize pass, and either silently
        // skipping Upsert synthesis entirely or silently falling through to
        // match on the wrong (secondary) unique index instead of the
        // table's real primary key.
        var pkCols = new List<ColumnSchema>();
        foreach (var c in tableSchema.Columns.Values)
        {
            if (c.IsPrimaryKey)
                pkCols.Add(c);
        }
        if (pkCols.Count == 0)
            return null;
        if (!pkCols.TrueForAll(c => c.IsIdentity))
        {
            // MySQL permits prefix key parts in a PRIMARY KEY too
            // (PRIMARY KEY (name(10))). Column-level PK flags carry no prefix
            // information, but the extractor captures the PRIMARY index like
            // any other -- so when that entry is prefix-flagged and no
            // full-column set-equal UNIQUE stands in for it, the PK enforces
            // only prefix uniqueness and the key is refused. A schema with no
            // PRIMARY entry at all (migration simulation) has no evidence
            // either way and resolves as before.
            var pkNames = new List<string>(pkCols.Count);
            foreach (var c in pkCols)
                pkNames.Add(c.Name);
            var prefixOnlyPk = FindPrefixOnlyPkEnforcement(tableSchema, pkNames);
            if (prefixOnlyPk != null)
            {
                prefixOnlyKey = prefixOnlyPk;
                return null;
            }
            return pkCols;
        }

        // The RowVersion/Computed exclusion still applies here, deliberately:
        // a secondary UNIQUE index used as an Upsert fallback match key must
        // consist of real, writable columns (RowVersion/Computed columns are
        // database-assigned/derived, so a caller can never supply a bound
        // value for one), matching UpsertColumns' own identical exclusion.
        var columns = new List<ColumnSchema>(tableSchema.Columns.Count);
        foreach (var c in tableSchema.Columns.Values)
        {
            if (!c.IsRowVersion && !c.IsComputed)
                columns.Add(c);
        }

        IndexSchema? skippedPrefixCandidate = null;
        foreach (var index in tableSchema.Indexes)
        {
            if (!index.IsUnique || index.Columns.Count == 0)
                continue;

            // A flagged index's Columns is only the real-column SUBSET of its
            // key -- UNIQUE (customer_id, lower(email)) arrives as
            // [customer_id], and customer_id alone is not unique. Never a key
            // candidate. Silent, deliberately: pre-2026-07-30 such an index
            // was absent from the snapshot entirely, so no-Upsert-no-
            // diagnostic is the reading these tables already had.
            if (index.HasExpressionKeyPart)
                continue;

            var keyCols = new List<ColumnSchema>();
            bool allResolved = true;
            foreach (var colName in index.Columns)
            {
                var col = columns.Find(c => string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
                if (col == null || col.IsIdentity)
                {
                    allResolved = false;
                    break;
                }
                keyCols.Add(col);
            }
            if (!allResolved)
                continue;

            // A prefix UNIQUE cannot serve as the key it would resolve to --
            // it enforces uniqueness over a truncated prefix, so matching on
            // it delivers prefix semantics under a full-value signature. Skip
            // it and keep scanning: a later full-column UNIQUE still resolves
            // (and the skipped index then correctly counts as competing in
            // HasCompetingUniqueConstraint). Remembered so that when nothing
            // else resolves, the refusal is reportable rather than
            // indistinguishable from "no key at all".
            if (index.HasPrefixKeyPart)
            {
                skippedPrefixCandidate ??= index;
                continue;
            }
            return keyCols;
        }
        prefixOnlyKey = skippedPrefixCandidate;
        return null;
    }

    /// <summary>
    /// The prefix-flagged <c>PRIMARY</c> index entry proving the primary key
    /// itself enforces only prefix uniqueness, or null when the PK is fine.
    /// Null when any full-column unique set-equal to <paramref name="keyNames"/>
    /// exists (the key's columns are enforced in full somewhere, so the flagged
    /// entries are competitors, not the enforcement), when no <c>PRIMARY</c>
    /// entry exists at all (a simulated schema — the PK constraint itself is
    /// the enforcement and column flags carry no prefix information), or when
    /// the flagged set-equal index is a SEPARATE prefix unique beside a
    /// full-column PK (again a competitor: HasCompetingUniqueConstraint sees
    /// it and the two-statement form's full-column WHERE handles it).
    /// <c>PRIMARY</c> is MySQL's fixed name for the PK index, and MySQL's
    /// extractor is the only one that ever sets the flag.
    /// </summary>
    private static IndexSchema? FindPrefixOnlyPkEnforcement(TableSchema tableSchema, List<string> keyNames)
    {
        IndexSchema? flaggedPrimary = null;
        foreach (var index in tableSchema.Indexes)
        {
            if (!index.IsUnique || index.Columns.Count == 0 || !CoversKey(index.Columns, keyNames))
                continue;
            // An expression index's Columns is only the real-column subset of
            // its key: UNIQUE (id, lower(x)) arriving as [id] set-equal to the
            // PK proves nothing about full-column enforcement of the PK.
            if (index.HasExpressionKeyPart)
                continue;
            if (!index.HasPrefixKeyPart)
                return null;
            if (string.Equals(index.Name, "PRIMARY", StringComparison.OrdinalIgnoreCase))
                flaggedPrimary = index;
        }
        return flaggedPrimary;
    }

    /// <summary>
    /// AUD-R4-16: true when the table carries a UNIQUE constraint other than
    /// <paramref name="keyCols"/> itself — a second unique index, or the
    /// primary key when the resolved key is a secondary unique index instead.
    ///
    /// This is the condition under which MySQL's <c>ON DUPLICATE KEY UPDATE</c>
    /// stops agreeing with the key <see cref="Resolve(TableSchema)"/> picked, because it
    /// names no conflict target at all: the engine matches whichever UNIQUE the
    /// insert happens to violate, while postgres/sqlite <c>ON CONFLICT (cols)</c>
    /// and sqlserver <c>MERGE ... ON</c> both name the key explicitly. With one
    /// UNIQUE on the table the two are indistinguishable; with two they are not,
    /// and the divergence is reachable through auto-CRUD alone, with no
    /// hand-written call site (Conduit's <c>users</c>: identity-only PK plus
    /// unique <c>email</c> and unique <c>username</c>).
    ///
    /// Lives beside <see cref="Resolve(TableSchema)"/>, and is Roslyn-free for the same
    /// reason: <c>AutoCrud</c> and the Roslyn <c>CodeEmitter</c> must both reach
    /// one implementation, or they disagree about the same table in the same
    /// pass.
    ///
    /// Partial/filtered unique indexes do not count, and need no special case
    /// here: all three extractors that can produce one already downgrade it to
    /// <c>IsUnique=false</c> at capture (PostgresExtractor.cs:168,
    /// SqliteExtractor.cs:130, SqlServerExtractor.cs:111), precisely so that a
    /// constraint which cannot fire for every row is not treated as one that
    /// can.
    /// </summary>
    public static bool HasCompetingUniqueConstraint(TableSchema tableSchema, List<ColumnSchema> keyCols)
    {
        if (keyCols.Count == 0)
            return false;

        var keyNames = new List<string>(keyCols.Count);
        foreach (var c in keyCols)
            keyNames.Add(c.Name);

        // The primary key is a UNIQUE the engine can match on, whether or not
        // Resolve chose it. When Resolve fell through to a secondary unique
        // index (the identity-only-PK case), the PK is precisely the competing
        // constraint that makes ON DUPLICATE KEY ambiguous -- so it must be
        // considered here, not just tableSchema.Indexes.
        var pkNames = new List<string>();
        foreach (var c in tableSchema.Columns.Values)
        {
            if (c.IsPrimaryKey)
                pkNames.Add(c.Name);
        }
        if (pkNames.Count > 0 && !CoversKey(pkNames, keyNames))
            return true;

        foreach (var index in tableSchema.Indexes)
        {
            if (!index.IsUnique)
                continue;

            // A UNIQUE with an expression key part always competes: it is
            // never the key (Resolve skips it) and it constrains rows through
            // an expression the key's columns say nothing about, so the
            // engine can match a row the key would not. Checked before the
            // empty-Columns guard below -- an ALL-expression unique index
            // (UNIQUE (lower(email))) arrives with no columns at all and
            // must not slip through it.
            if (index.HasExpressionKeyPart)
                return true;

            if (index.Columns.Count == 0)
                continue;

            // The key itself, or a full-column restatement of it in another
            // order: not a competitor. A PREFIX index over the same column set
            // IS one -- UNIQUE (email(5)) fires on rows sharing five characters
            // of a key of (email), rows the full-column constraint would never
            // match. It can also never BE the key: Resolve skips prefix-flagged
            // indexes (and refuses a PK enforced only by one), so an index
            // reaching here flagged means the key is enforced in full elsewhere
            // and this one genuinely competes.
            if (!index.HasPrefixKeyPart && CoversKey(index.Columns, keyNames))
                continue;

            // A full-column UNIQUE over a strict SUPERSET of the key cannot
            // fire independently of it: violating (id, tenant) requires
            // duplicating id, and if id is the unique key the matched row is
            // the key's own match. A prefix superset can -- UNIQUE (id(3),
            // tenant) fires on rows sharing only three characters of id --
            // which is why this relaxation is gated on the extractor-captured
            // flag rather than assumed, and why it briefly shipped ungated and
            // was reverted (see CoversKey's history note).
            if (!index.HasPrefixKeyPart && ContainsAllKeyColumns(index.Columns, keyNames))
                continue;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Set equality, order-insensitive and OrdinalIgnoreCase — matching
    /// <see cref="Resolve(TableSchema)"/>'s own column lookup, since a UNIQUE constraint's
    /// column order is not part of its identity for conflict-matching purposes
    /// (<c>UNIQUE (a, b)</c> and <c>UNIQUE (b, a)</c> reject the same rows).
    /// <para>
    /// <b>History.</b> The caller's superset relaxation briefly shipped
    /// UNGATED as part of this method and was reverted the same day
    /// (2026-07-30): with <c>SUB_PART</c> uncaptured, a prefix UNIQUE —
    /// <c>UNIQUE (id(3), tenant)</c>, which fires on rows sharing only the
    /// first three characters of <c>id</c>, independently of a key of
    /// <c>(id)</c> — arrived here indistinguishable from the full-column form
    /// and was dismissed as redundant, restoring the exact AUD-R4-16 defect.
    /// <c>MySqlExtractor</c> now captures <c>SUB_PART</c> as
    /// <c>IndexSchema.HasPrefixKeyPart</c>, and the relaxation lives in
    /// <see cref="HasCompetingUniqueConstraint"/> gated on that flag. The
    /// caller's primary-key check keeps plain equality: pk names are
    /// column-level flags with no prefix information, and a schema that never
    /// went through the extractor (migration simulation) has no <c>PRIMARY</c>
    /// index entry to carry the flag for it. A prefix-flagged PK is instead
    /// caught in <see cref="Resolve(TableSchema)"/> via
    /// <see cref="FindPrefixOnlyPkEnforcement"/>, when the index entry exists to
    /// prove it.
    /// </para>
    /// </summary>
    private static bool CoversKey(List<string> constraintCols, List<string> keyNames)
    {
        return constraintCols.Count == keyNames.Count
            && ContainsAllKeyColumns(constraintCols, keyNames);
    }

    /// <summary>
    /// True when every key column appears among <paramref name="constraintCols"/>
    /// (OrdinalIgnoreCase) — i.e. the constraint's columns are a (non-strict)
    /// superset of the key's. Column names are unique within an index, so no
    /// multiplicity handling is needed.
    /// </summary>
    private static bool ContainsAllKeyColumns(List<string> constraintCols, List<string> keyNames)
    {
        foreach (var key in keyNames)
        {
            bool found = false;
            foreach (var col in constraintCols)
            {
                if (string.Equals(key, col, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                return false;
        }
        return true;
    }
}
