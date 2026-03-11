using System.Text.Json;

namespace JauntyQ.Schema;

public static class SchemaLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static DatabaseSchema Load(string json)
    {
        return JsonSerializer.Deserialize<DatabaseSchema>(json, Options)
            ?? new DatabaseSchema();
    }

    public static string Serialize(DatabaseSchema schema)
    {
        return JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        });
    }
}
