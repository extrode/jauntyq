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
/// </summary>
public static class UpsertKeyResolver
{
    public static List<ColumnSchema>? Resolve(TableSchema tableSchema)
    {
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
            return pkCols;

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

        foreach (var index in tableSchema.Indexes)
        {
            if (!index.IsUnique || index.Columns.Count == 0)
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
            if (allResolved)
                return keyCols;
        }
        return null;
    }

    /// <summary>
    /// AUD-R4-16: true when the table carries a UNIQUE constraint other than
    /// <paramref name="keyCols"/> itself — a second unique index, or the
    /// primary key when the resolved key is a secondary unique index instead.
    ///
    /// This is the condition under which MySQL's <c>ON DUPLICATE KEY UPDATE</c>
    /// stops agreeing with the key <see cref="Resolve"/> picked, because it
    /// names no conflict target at all: the engine matches whichever UNIQUE the
    /// insert happens to violate, while postgres/sqlite <c>ON CONFLICT (cols)</c>
    /// and sqlserver <c>MERGE ... ON</c> both name the key explicitly. With one
    /// UNIQUE on the table the two are indistinguishable; with two they are not,
    /// and the divergence is reachable through auto-CRUD alone, with no
    /// hand-written call site (Conduit's <c>users</c>: identity-only PK plus
    /// unique <c>email</c> and unique <c>username</c>).
    ///
    /// Lives beside <see cref="Resolve"/>, and is Roslyn-free for the same
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
        if (pkNames.Count > 0 && !SameColumnSet(pkNames, keyNames))
            return true;

        foreach (var index in tableSchema.Indexes)
        {
            if (!index.IsUnique || index.Columns.Count == 0)
                continue;
            if (!SameColumnSet(index.Columns, keyNames))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Set equality, order-insensitive and OrdinalIgnoreCase — matching
    /// <see cref="Resolve"/>'s own column lookup, since a UNIQUE constraint's
    /// column order is not part of its identity for conflict-matching purposes
    /// (<c>UNIQUE (a, b)</c> and <c>UNIQUE (b, a)</c> reject the same rows).
    /// </summary>
    private static bool SameColumnSet(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
            return false;
        foreach (var name in left)
        {
            bool found = false;
            foreach (var other in right)
            {
                if (string.Equals(name, other, StringComparison.OrdinalIgnoreCase))
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
