using System.Text.Json.Serialization;

namespace JauntyQ.Analysis.Impact;

/// <summary>Why a query is RISKY or BREAKING: the schema object, the kind of
/// change, and the human-readable effect.</summary>
public sealed class ImpactReason
{
    [JsonPropertyName("schemaObject")]
    public string SchemaObject { get; set; } = string.Empty;

    /// <summary>e.g. <c>removed</c>, <c>type</c>, <c>nullability</c>, <c>maxLength</c>, <c>unmodeled</c>.</summary>
    [JsonPropertyName("changeKind")]
    public string ChangeKind { get; set; } = string.Empty;

    [JsonPropertyName("effect")]
    public string Effect { get; set; } = string.Empty;

    public ImpactReason() { }

    public ImpactReason(string schemaObject, string changeKind, string effect)
    {
        SchemaObject = schemaObject;
        ChangeKind = changeKind;
        Effect = effect;
    }
}
