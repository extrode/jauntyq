using System.Text.Json;

namespace JauntyQ.Schema;

public static class SchemaLoader
{
    public static DatabaseSchema Load(string json)
    {
        return JsonSerializer.Deserialize(json, SchemaJsonContext.Default.DatabaseSchema)
            ?? new DatabaseSchema();
    }

    public static string Serialize(DatabaseSchema schema)
    {
        return JsonSerializer.Serialize(schema, SchemaJsonContext.Default.DatabaseSchema);
    }
}
