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

    /// <summary>
    /// Enumerated types the database enforces, keyed by name. Captured by
    /// 'jaunty schema pull' so the generator can emit a real C# enum for a
    /// column of that type. PostgreSQL native enums and MySQL inline column
    /// enums populate this; SQL Server and SQLite have no native enum, so it
    /// stays empty for them. A type declared but referenced by no column is
    /// still captured — it is schema, and dropping it is still drift — but
    /// only referenced ones are emitted.
    /// </summary>
    [JsonPropertyName("enums")]
    public Dictionary<string, EnumSchema> Enums { get; set; } = new();

    /// <summary>
    /// Scalar functions that live in the database, keyed by name. Captured by
    /// 'jaunty schema pull' so the generator can emit a typed
    /// <c>db.Functions.{Name}(...)</c> method. PostgreSQL, SQL Server and MySQL
    /// populate this; SQLite has no stored functions, so it stays empty there.
    /// Table-valued functions are NOT captured here (014-plan.md SS6).
    /// </summary>
    [JsonPropertyName("functions")]
    public Dictionary<string, FunctionSchema> Functions { get; set; } = new();

    /// <summary>
    /// User-defined types, keyed by name. Aliases and DOMAINs are captured
    /// WITH their resolved underlying primitive, which is what stops a column
    /// typed by one from generating <c>object</c> + JNT2007. Composites and
    /// SQL Server table types are captured unresolved, so the diagnostics that
    /// refuse them can name their shape. Empty for SQLite.
    /// </summary>
    [JsonPropertyName("userTypes")]
    public Dictionary<string, UserTypeSchema> UserTypes { get; set; } = new();
}
