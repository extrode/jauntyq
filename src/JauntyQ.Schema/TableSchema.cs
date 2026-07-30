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
}
