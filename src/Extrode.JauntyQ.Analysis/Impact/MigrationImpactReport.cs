using System.Text.Json;
using System.Text.Json.Serialization;

namespace Extrode.JauntyQ.Analysis.Impact;

/// <summary>The result of one migration impact analysis run: the baseline it was
/// computed against, the migration files in the set, and a per-query
/// classification entry for every hand-written and synthetic query.</summary>
public sealed class MigrationImpactReport
{
    [JsonPropertyName("baselineId")]
    public string BaselineId { get; set; } = string.Empty;

    [JsonPropertyName("migrationSet")]
    public List<string> MigrationSet { get; set; } = new();

    [JsonPropertyName("entries")]
    public List<ImpactEntry> Entries { get; set; } = new();

    /// <summary>
    /// Conditions that narrowed what this run actually analyzed: a configured
    /// input directory that does not exist, a migration statement the simulator
    /// could not model (JNT9001). Carried on the report itself, rather than
    /// beside it, because the text renderer had them and the JSON one did not —
    /// so a CI job reading JSON could not tell an all-Safe report over the whole
    /// corpus from an all-Safe report over nothing. Additive: an existing
    /// consumer that ignores the property is unaffected, and a clean run
    /// serializes it as an empty array.
    /// </summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    public MigrationImpactReport() { }

    public MigrationImpactReport(string baselineId, List<string> migrationSet, List<ImpactEntry> entries)
    {
        BaselineId = baselineId;
        MigrationSet = migrationSet;
        Entries = entries;
    }

    /// <summary>The most severe classification present, or SAFE when there are no entries.</summary>
    [JsonIgnore]
    public Classification Highest
    {
        get
        {
            var highest = Classification.Safe;
            foreach (var e in Entries)
                if (e.Classification > highest)
                    highest = e.Classification;
            return highest;
        }
    }

    /// <summary>Number of entries at the given classification.</summary>
    public int Count(Classification classification)
    {
        int n = 0;
        foreach (var e in Entries)
            if (e.Classification == classification)
                n++;
        return n;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>Serializes the report to the documented JSON shape.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Round-trips a report from its JSON form.</summary>
    public static MigrationImpactReport FromJson(string json) =>
        JsonSerializer.Deserialize<MigrationImpactReport>(json, JsonOptions)
        ?? throw new JsonException("Migration impact report deserialized to null.");
}
