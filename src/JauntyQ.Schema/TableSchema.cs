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
}
