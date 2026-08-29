using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

/// <summary>
/// Source-generated type metadata for <see cref="DatabaseSchema"/> and everything it
/// references, so <see cref="SchemaLoader"/> never falls back to reflection-based
/// <c>JsonSerializer</c>. Load-bearing at runtime, not just build time: this schema
/// snapshot is read via <c>JauntyQ.Schema.Contract</c>'s <c>SnapshotSource</c>/
/// <c>StartupSchemaGuard</c> inside apps that may themselves be Native-AOT published,
/// where reflection-based (de)serialization throws.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(DatabaseSchema))]
[JsonSerializable(typeof(TableSchema))]
[JsonSerializable(typeof(IndexSchema))]
[JsonSerializable(typeof(ColumnSchema))]
[JsonSerializable(typeof(ForeignKeySchema))]
[JsonSerializable(typeof(ProcedureSchema))]
[JsonSerializable(typeof(ProcedureParam))]
[JsonSerializable(typeof(ProcedureParamDirection))]
[JsonSerializable(typeof(SequenceSchema))]
[JsonSerializable(typeof(EnumSchema))]
[JsonSerializable(typeof(EnumMember))]
[JsonSerializable(typeof(FunctionSchema))]
[JsonSerializable(typeof(FunctionParam))]
[JsonSerializable(typeof(FunctionReturn))]
[JsonSerializable(typeof(UserTypeSchema))]
[JsonSerializable(typeof(UserTypeKind))]
[JsonSerializable(typeof(AcceptanceFile))]
[JsonSerializable(typeof(AcceptanceEntry))]
internal sealed partial class SchemaJsonContext : JsonSerializerContext
{
}
