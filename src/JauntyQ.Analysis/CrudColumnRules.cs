using JauntyQ.Schema;

namespace JauntyQ.Analysis;

/// <summary>
/// Single source of truth for which table columns participate in each
/// synthetic CRUD operation (Insert/Update/Delete/Upsert) and which single
/// column is the auto-assigned identity. <see cref="AutoCrud"/> (synthesizes
/// the SQL) and JauntyQ.Generator's CodeEmitter (Part4/6/7/11.cs — emits the
/// matching C# POCO overloads and bulk-insert bodies) must agree on these
/// column sets byte-for-byte: a generated method's C# argument list is
/// positionally forwarded into SQL built from the same column set, so any
/// divergence between two independently-written copies of a filter desyncs
/// a parameter list against its SQL — the kind of bug a compiler cannot
/// catch (both sides still compile; the mismatch only breaks at runtime, or
/// wrongly maps column N's value onto column N's neighbor). Schema-only,
/// Roslyn-free, like <see cref="UpsertKeyResolver"/> — JauntyQ.Generator.csproj
/// compile-includes JauntyQ.Analysis's source directly (see Embedded\
/// JauntyQ.Analysis in the .csproj), so this is available to the generator
/// with zero new project-reference wiring.
/// </summary>
public static class CrudColumnRules
{
    public static List<ColumnSchema> PrimaryKeyColumns(IEnumerable<ColumnSchema> columns)
        => Filter(columns, c => c.IsPrimaryKey);

    public static List<ColumnSchema> RowVersionColumns(IEnumerable<ColumnSchema> columns)
        => Filter(columns, c => c.IsRowVersion);

    /// <summary>
    /// Columns an INSERT may write: identity and rowversion columns are
    /// database-assigned, and computed/generated columns are derived — a
    /// database rejects an INSERT that supplies any of the three.
    /// </summary>
    public static List<ColumnSchema> InsertableColumns(IEnumerable<ColumnSchema> columns)
        => Filter(columns, c => !c.IsIdentity && !c.IsRowVersion && !c.IsComputed);

    /// <summary>
    /// Columns a synthetic UPDATE's SET clause may write: <see
    /// cref="InsertableColumns"/> minus the primary key (PK columns move to
    /// the WHERE clause instead). InsertableColumns' identity exclusion
    /// already covers a non-PK identity column too — every dialect tested
    /// (confirmed live: SQL Server) rejects an UPDATE that targets an
    /// identity column outright, whether or not that column is the key.
    /// </summary>
    public static List<ColumnSchema> UpdatableColumns(IEnumerable<ColumnSchema> columns)
        => Filter(columns, c => !c.IsPrimaryKey && !c.IsIdentity && !c.IsRowVersion && !c.IsComputed);

    /// <summary>
    /// The single identity column on this table, or null when there are
    /// zero or more than one. No dialect supports more than one identity/
    /// auto-increment column per table, so this doubles as the "does this
    /// table have a resolvable identity column at all" check used to decide
    /// whether an Insert can return the database-assigned id.
    /// </summary>
    public static ColumnSchema? SingleIdentityColumn(IEnumerable<ColumnSchema> columns)
    {
        ColumnSchema? identityCol = null;
        foreach (var c in columns)
        {
            if (!c.IsIdentity)
                continue;
            if (identityCol != null)
                return null;
            identityCol = c;
        }
        return identityCol;
    }

    /// <summary>
    /// The full column set a synthetic UPSERT reads/writes (the INSERT
    /// column list, and — for dialects that reference it in a MERGE/ON
    /// CONFLICT src-select or match clause — every key column too):
    /// rowversion and computed/generated columns are excluded outright
    /// (database-assigned; a database rejects an INSERT/UPDATE targeting
    /// either), and an identity column is excluded UNLESS it is itself part
    /// of <paramref name="upsertKey"/> (a composite key with one identity
    /// member, matched on directly and so still needing a bound value) —
    /// every OTHER identity column stays excluded regardless of which key
    /// resolved, since no caller-supplied value can ever reach a column the
    /// database assigns.
    /// </summary>
    public static List<ColumnSchema> UpsertColumns(IEnumerable<ColumnSchema> columns, List<ColumnSchema> upsertKey)
        => Filter(columns, c => !c.IsRowVersion && !c.IsComputed
            && (!c.IsIdentity || upsertKey.Exists(k => string.Equals(k.Name, c.Name, StringComparison.OrdinalIgnoreCase))));

    /// <summary>
    /// The subset of <see cref="UpsertColumns"/> that the UPSERT's UPDATE-
    /// on-conflict clause actually SETs: every key column is excluded (key
    /// columns are matched on, not written to, on the conflict path).
    /// Empty when the table has nothing to update besides its key —
    /// callers use this to decide whether Upsert synthesis is worthwhile at
    /// all, not just how to shape the SQL.
    /// </summary>
    public static List<ColumnSchema> UpsertSetColumns(List<ColumnSchema> upsertColumns, List<ColumnSchema> upsertKey)
        => upsertColumns.FindAll(c => !upsertKey.Exists(k => string.Equals(k.Name, c.Name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// AUD-R37-01: true when <paramref name="upsertKey"/> (UpsertKeyResolver.
    /// Resolve's result — post-AUD-R36-01, this legitimately includes a
    /// Computed or RowVersion primary-key column, matching AutoCrud's own PK
    /// detection for the same table) contains a column <see
    /// cref="UpsertColumns"/> excludes outright with no "unless part of key"
    /// escape hatch. Identity has that escape hatch (a composite key with one
    /// identity member is still a column a caller can bind an explicit value
    /// for, given IDENTITY_INSERT/equivalent) — Computed and RowVersion do
    /// not and cannot: no dialect accepts an explicit INSERT value for either
    /// (the database rejects it outright), so a "src"/conflict-target
    /// reference to one in the generated Upsert SQL would name a column that
    /// was never selected/writable anywhere else in the same statement. Both
    /// AutoCrud.Synthesize's Upsert gate and CodeEmitter.Part6.cs's
    /// EmitUpsert must treat this exactly like "no usable key at all" —
    /// silently skipping Upsert synthesis rather than emitting SQL a live
    /// engine rejects at runtime with "Invalid column name" (confirmed
    /// live against SQL Server 2022 via Testcontainers for the MERGE
    /// src-derived-table shape).
    /// </summary>
    public static bool HasUnbindableUpsertKeyColumn(IEnumerable<ColumnSchema> columns, List<ColumnSchema> upsertKey)
    {
        var upsertCols = UpsertColumns(columns, upsertKey);
        foreach (var k in upsertKey)
        {
            if (!upsertCols.Exists(c => string.Equals(c.Name, k.Name, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private static List<ColumnSchema> Filter(IEnumerable<ColumnSchema> columns, System.Func<ColumnSchema, bool> predicate)
    {
        var result = new List<ColumnSchema>();
        foreach (var c in columns)
        {
            if (predicate(c))
                result.Add(c);
        }
        return result;
    }
}
