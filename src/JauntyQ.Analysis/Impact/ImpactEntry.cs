using System.Text.Json.Serialization;

namespace JauntyQ.Analysis.Impact;

/// <summary>One query's classification and the reasons behind it.</summary>
public sealed class ImpactEntry
{
    [JsonPropertyName("queryFile")]
    public string QueryFile { get; set; } = string.Empty;

    [JsonPropertyName("entityMethod")]
    public string EntityMethod { get; set; } = string.Empty;

    [JsonPropertyName("classification")]
    public Classification Classification { get; set; }

    [JsonPropertyName("reasons")]
    public List<ImpactReason> Reasons { get; set; } = new();

    public ImpactEntry() { }

    public ImpactEntry(string queryFile, string entityMethod, Classification classification, List<ImpactReason> reasons)
    {
        QueryFile = queryFile;
        EntityMethod = entityMethod;
        Classification = classification;
        Reasons = reasons;
    }
}
