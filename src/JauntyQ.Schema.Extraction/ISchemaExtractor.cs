using JauntyQ.Schema;

namespace JauntyQ.Schema.Extraction;

public interface ISchemaExtractor
{
    Task<DatabaseSchema> ExtractAsync(string connectionString);
}
