using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

public class DatabaseSchema
{
    /// <summary>
    /// Source dialect the snapshot was pulled from ("sqlserver", "postgres",
    /// "mysql", "sqlite"). Drives dialect-specific SQL synthesis (auto-CRUD).
    /// </summary>
    [JsonPropertyName("dialect")]
    public string Dialect { get; set; } = string.Empty;

    [JsonPropertyName("tables")]
    public Dictionary<string, TableSchema> Tables { get; set; } = new();

    [JsonPropertyName("foreignKeys")]
    public List<ForeignKeySchema> ForeignKeys { get; set; } = new();

    /// <summary>
    /// Stored procedures that live in the database, keyed by name. Captured by
    /// 'jaunty schema pull' so a -- @call query can bind to an existing proc
    /// with typed parameters and result columns. Empty for providers/databases
    /// without stored procedures (e.g. SQLite).
    /// </summary>
    [JsonPropertyName("procedures")]
    public Dictionary<string, ProcedureSchema> Procedures { get; set; } = new();

    /// <summary>
    /// Sequence objects that live in the database, keyed by name. Captured by
    /// 'jaunty schema pull' so the generator can emit a typed
    /// <c>db.Sequences.Next{Name}()</c> accessor. SQL Server, PostgreSQL, and
    /// MariaDB (mapped to the "mysql" dialect string, via its own
    /// <c>CREATE SEQUENCE</c> since 10.3) populate this; empty for real/Oracle
    /// MySQL and SQLite, which have no true sequence object.
    /// </summary>
    [JsonPropertyName("sequences")]
    public Dictionary<string, SequenceSchema> Sequences { get; set; } = new();
}
