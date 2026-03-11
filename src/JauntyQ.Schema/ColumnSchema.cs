using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

public class ColumnSchema
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("dbType")]
    public string DbType { get; set; } = string.Empty;

    [JsonPropertyName("isNullable")]
    public bool IsNullable { get; set; }
}
