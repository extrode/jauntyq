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
        var columns = new List<ColumnSchema>(tableSchema.Columns.Count);
        foreach (var c in tableSchema.Columns.Values)
        {
            if (!c.IsRowVersion && !c.IsComputed)
                columns.Add(c);
        }
        var pkCols = columns.FindAll(c => c.IsPrimaryKey);
        if (pkCols.Count == 0)
            return null;
        if (!pkCols.TrueForAll(c => c.IsIdentity))
            return pkCols;

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
}
