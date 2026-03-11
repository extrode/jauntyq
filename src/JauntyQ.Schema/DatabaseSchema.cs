using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

public class DatabaseSchema
{
    [JsonPropertyName("tables")]
    public Dictionary<string, TableSchema> Tables { get; set; } = new();

    [JsonPropertyName("foreignKeys")]
    public List<ForeignKeySchema> ForeignKeys { get; set; } = new();
}
