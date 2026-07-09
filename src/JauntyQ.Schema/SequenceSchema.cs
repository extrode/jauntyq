using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

/// <summary>
/// A sequence object that lives in the database (SQL Server <c>sys.sequences</c>,
/// PostgreSQL <c>information_schema.sequences</c>). Captured by 'jaunty schema
/// pull' so the generator can emit a typed <c>db.Sequences.Next{Name}()</c>
/// accessor. Only SQL Server and PostgreSQL have a true sequence object; MySQL
/// and SQLite have none, so this dictionary is empty for those dialects.
/// </summary>
public class SequenceSchema
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("startValue")]
    public long StartValue { get; set; }

    [JsonPropertyName("increment")]
    public long Increment { get; set; }

    [JsonPropertyName("minValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MinValue { get; set; }

    [JsonPropertyName("maxValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxValue { get; set; }

    /// <summary>
    /// Current (last) value of the sequence at snapshot time, when the extractor
    /// can reliably read it. Nullable because not every catalog view exposes it
    /// for a never-used sequence.
    /// </summary>
    [JsonPropertyName("currentValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CurrentValue { get; set; }
}
