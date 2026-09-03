using Extrode.JauntyQ.Schema;

namespace Extrode.JauntyQ.Schema.Extraction;

/// <summary>
/// Shared accumulator for index extraction: rows arrive one key column at a
/// time (ordered by key ordinal) and are folded into IndexSchema entries.
/// </summary>
internal static class IndexCapture
{
    public static void AddIndexColumn(DatabaseSchema schema, string tableName, string indexName, bool isUnique, string columnName, bool hasPrefixKeyPart = false, bool hasExpressionKeyPart = false)
    {
        if (!schema.Tables.TryGetValue(tableName, out var table))
            return;
        var index = EnsureIndex(schema, tableName, indexName, isUnique, hasExpressionKeyPart);
        if (index == null)
            return;
        index.Columns.Add(columnName);
        // OR, not assignment: one prefixed key part marks the whole index, and a
        // later full-column part of the same index must not clear it.
        if (hasPrefixKeyPart)
            index.HasPrefixKeyPart = true;
    }

    /// <summary>
    /// Finds or creates the index entry without adding a column — the entry
    /// point for an ALL-expression index (e.g. <c>UNIQUE (lower(email))</c>),
    /// which produces no real-column rows at all but must still exist in the
    /// snapshot so a unique one is visible as a competing constraint. The
    /// expression flag ORs like the prefix flag: one expression key part marks
    /// the whole index.
    /// </summary>
    public static IndexSchema? EnsureIndex(DatabaseSchema schema, string tableName, string indexName, bool isUnique, bool hasExpressionKeyPart = false)
    {
        if (!schema.Tables.TryGetValue(tableName, out var table))
            return null;
        var index = table.Indexes.Find(i => i.Name == indexName);
        if (index == null)
        {
            index = new IndexSchema { Name = indexName, IsUnique = isUnique };
            table.Indexes.Add(index);
        }
        if (hasExpressionKeyPart)
            index.HasExpressionKeyPart = true;
        return index;
    }
}
