using Extrode.JauntyQ.Schema;

namespace Extrode.JauntyQ.Schema.Extraction;

public interface ISchemaExtractor
{
    Task<DatabaseSchema> ExtractAsync(string connectionString);
}
