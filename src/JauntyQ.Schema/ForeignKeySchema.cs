using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

public class ForeignKeySchema
{
    [JsonPropertyName("fromTable")]
    public string FromTable { get; set; } = string.Empty;

    [JsonPropertyName("fromColumn")]
    public string FromColumn { get; set; } = string.Empty;

    [JsonPropertyName("toTable")]
    public string ToTable { get; set; } = string.Empty;

    [JsonPropertyName("toColumn")]
    public string ToColumn { get; set; } = string.Empty;
}
