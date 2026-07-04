using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

public class DatabaseSchema
{
    /// <summary>
    /// Source dialect the snapshot was pulled from ("sqlserver", "postgres",
    /// "mysql", "sqlite"). Drives dialect-specific SQL synthesis (auto-CRUD).
    /// </summary>
    [JsonPropertyName("dialect")]
    public string Dialect { get; set; } = string.Empty;

    [JsonPropertyName("tables")]
    public Dictionary<string, TableSchema> Tables { get; set; } = new();

    [JsonPropertyName("foreignKeys")]
    public List<ForeignKeySchema> ForeignKeys { get; set; } = new();
}
