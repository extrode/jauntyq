using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

public class TableSchema
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("columns")]
    public Dictionary<string, ColumnSchema> Columns { get; set; } = new();

    /// <summary>
    /// Indexes on the table (key columns in key order). Used by the JNT8xxx
    /// performance analyzer; empty for snapshots that predate index capture.
    /// </summary>
    [JsonPropertyName("indexes")]
    public List<IndexSchema> Indexes { get; set; } = new();

    /// <summary>
    /// True when this relation is a database view rather than a base table.
    /// Materialized views set it too: spec 015 puts both on identical terms,
    /// and a second flag distinguishing them would have no reader.
    ///
    /// Views are read-only. Insertability is <see cref="IsInsertable"/>, which
    /// is DERIVED from this flag rather than stored: a stored "isInsertable"
    /// would deserialize to false on every snapshot written before views
    /// existed, silently making every table in them non-insertable. One field,
    /// absent-means-base-table, is the only shape that survives an old snapshot.
    /// </summary>
    [JsonPropertyName("isView")]
    public bool IsView { get; set; }

    /// <summary>
    /// False for a view, true for a base table. Not serialized — see the note
    /// on <see cref="IsView"/> for why this must not become a stored field.
    /// </summary>
    [JsonIgnore]
    public bool IsInsertable => !IsView;
}

public class IndexSchema
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Key columns in key order; only the leading column makes a filter seekable.</summary>
    [JsonPropertyName("columns")]
    public List<string> Columns { get; set; } = new();

    [JsonPropertyName("isUnique")]
    public bool IsUnique { get; set; }

    /// <summary>
    /// True when any key part is a MySQL/MariaDB column-prefix — <c>UNIQUE
    /// (id(3), tenant)</c> — captured from <c>INFORMATION_SCHEMA.STATISTICS.SUB_PART</c>.
    /// Such an index enforces uniqueness over the truncated prefix, not the full
    /// column values, so it can fire against rows whose full <see cref="Columns"/>
    /// differ: it is stricter than the full-column index of the same columns and
    /// must not be treated as equivalent to (or redundant against) one.
    /// <see cref="Columns"/> still lists the full column names. False for every
    /// other dialect (no prefix key parts exist) and for snapshots that predate
    /// this capture — the conservative default, matching how those snapshots were
    /// already being read.
    /// </summary>
    [JsonPropertyName("hasPrefixKeyPart")]
    public bool HasPrefixKeyPart { get; set; }

    /// <summary>
    /// True when any key part is an expression rather than a plain column —
    /// MySQL <c>(lower(email))</c>, Postgres/SQLite <c>lower(email)</c>. The
    /// model cannot carry the expression itself, so for a flagged index
    /// <see cref="Columns"/> holds only the REAL-COLUMN SUBSET of the key, in
    /// relative order, possibly empty for an all-expression index. That list
    /// must never be read as the full key: no seekability, coverage or
    /// uniqueness conclusion may be drawn from it (<c>UNIQUE (customer_id,
    /// lower(email))</c> does not make <c>customer_id</c> unique). What a
    /// flagged UNIQUE index does prove is that a competing unique constraint
    /// exists — the reason such indexes are represented at all rather than
    /// excluded as they were before 2026-07-30. False for snapshots that
    /// predate the capture, which simply omitted these indexes — the same
    /// reading they already got.
    /// </summary>
    [JsonPropertyName("hasExpressionKeyPart")]
    public bool HasExpressionKeyPart { get; set; }
}
