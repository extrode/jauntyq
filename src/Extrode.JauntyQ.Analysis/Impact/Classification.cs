using System.Text.Json;
using System.Text.Json.Serialization;

namespace Extrode.JauntyQ.Analysis.Impact;

/// <summary>
/// A query's impact classification relative to a pending migration set.
/// Serialized as the uppercase tokens <c>SAFE</c> / <c>RISKY</c> / <c>BREAKING</c>.
/// </summary>
[JsonConverter(typeof(ClassificationJsonConverter))]
public enum Classification
{
    /// <summary>References no object the migration changed.</summary>
    Safe = 0,

    /// <summary>Still compiles, but a referenced object changed (type, nullability,
    /// length, precision) or was touched by an unmodeled statement.</summary>
    Risky = 1,

    /// <summary>The migration renders the query invalid (references a removed table or column).</summary>
    Breaking = 2
}

/// <summary>Serializes <see cref="Classification"/> as its uppercase wire token.</summary>
public sealed class ClassificationJsonConverter : JsonConverter<Classification>
{
    public override Classification Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var token = reader.GetString();
        return token?.ToUpperInvariant() switch
        {
            "SAFE" => Classification.Safe,
            "RISKY" => Classification.Risky,
            "BREAKING" => Classification.Breaking,
            _ => throw new JsonException($"Unknown classification '{token}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, Classification value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToUpperInvariant());
}
