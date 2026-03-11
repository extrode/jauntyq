using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

public class TableSchema
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("columns")]
    public Dictionary<string, ColumnSchema> Columns { get; set; } = new();
}
