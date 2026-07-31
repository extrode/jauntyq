using System.Text.Json;

namespace JauntyQ.Schema;

public static class SchemaLoader
{
    public static DatabaseSchema Load(string json)
    {
        // The JSON literal `null` deserializes to a null DatabaseSchema without
        // a JsonException. Substituting an empty schema here (the old behavior)
        // made a corrupted/blanked snapshot indistinguishable from a database
        // with no tables: the generator would validate every query against
        // nothing and `schema verify` would report every table as new. A null
        // snapshot is a parse failure, and every caller already handles
        // JsonException from malformed input on this same path.
        return JsonSerializer.Deserialize(json, SchemaJsonContext.Default.DatabaseSchema)
            ?? throw new JsonException("schema snapshot JSON was the literal 'null', not a snapshot object");
    }

    public static string Serialize(DatabaseSchema schema)
    {
        return JsonSerializer.Serialize(schema, SchemaJsonContext.Default.DatabaseSchema);
    }
}
