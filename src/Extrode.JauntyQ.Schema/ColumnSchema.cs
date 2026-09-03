using System.Text.Json.Serialization;

namespace Extrode.JauntyQ.Schema;

public class ColumnSchema
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("dbType")]
    public string DbType { get; set; } = string.Empty;

    [JsonPropertyName("isNullable")]
    public bool IsNullable { get; set; }

    [JsonPropertyName("isPrimaryKey")]
    public bool IsPrimaryKey { get; set; }

    [JsonPropertyName("isIdentity")]
    public bool IsIdentity { get; set; }

    /// <summary>
    /// Max length for string/binary columns: characters for text, bytes for
    /// binary; -1 means unbounded (varchar(max), text, bytea). Null when the
    /// type has no length or the snapshot predates value-safety metadata.
    /// </summary>
    [JsonPropertyName("maxLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; set; }

    /// <summary>Numeric precision (total digits) for decimal/numeric columns.</summary>
    [JsonPropertyName("precision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Precision { get; set; }

    /// <summary>Numeric scale (fraction digits) for decimal/numeric columns.</summary>
    [JsonPropertyName("scale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Scale { get; set; }

    /// <summary>
    /// True for Unicode text columns (nvarchar/nchar, or a Unicode charset),
    /// false for single-byte-charset text, null for non-text columns or
    /// snapshots that predate value-safety metadata.
    /// </summary>
    [JsonPropertyName("isUnicode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsUnicode { get; set; }

    /// <summary>
    /// True for database-maintained concurrency tokens (SQL Server
    /// rowversion/timestamp). Never written by the application; used to
    /// generate optimistic-concurrency WHERE clauses. Explicit flag because
    /// SQL Server reports rowversion as data type 'timestamp', which
    /// collides with the PostgreSQL datetime type of the same name.
    /// </summary>
    [JsonPropertyName("isRowVersion")]
    public bool IsRowVersion { get; set; }

    /// <summary>
    /// True for a computed/generated column (SQL Server <c>AS ...</c>
    /// [PERSISTED], PostgreSQL <c>GENERATED ALWAYS AS (...) STORED</c>, MySQL
    /// <c>GENERATED ALWAYS AS (...)</c>). The database rejects an INSERT/UPDATE
    /// that targets one, so auto-CRUD and BulkInsert must exclude it from
    /// their column/value lists the same way they already exclude
    /// <see cref="IsIdentity"/> and <see cref="IsRowVersion"/>.
    /// </summary>
    [JsonPropertyName("isComputed")]
    public bool IsComputed { get; set; }

    /// <summary>
    /// Key into <see cref="DatabaseSchema.Enums"/> when this column is of a
    /// database enum type, null otherwise. Resolving the type through this
    /// reference rather than re-parsing <see cref="DbType"/> is what lets the
    /// mapper reach the member list, which <see cref="DbType"/> alone never
    /// carries: PostgreSQL reports only the type name and MySQL only the bare
    /// word "enum". Null on every non-enum column and on every snapshot taken
    /// before spec 013, which is what makes those snapshots keep their old
    /// mapping unchanged.
    /// </summary>
    [JsonPropertyName("enumName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EnumName { get; set; }

    /// <summary>
    /// Name of the user-defined alias or DOMAIN this column was declared with,
    /// when capture resolved it to a primitive. <see cref="DbType"/> then
    /// carries the RESOLVED type, so every existing mapping rule applies to it
    /// unchanged and JNT2007 never sees the column.
    ///
    /// Kept rather than discarded for two reasons: dropping the alias would
    /// make the snapshot claim the column was declared varchar(11) when it was
    /// declared 'ssn', and the contract comparer needs the name to report
    /// "the domain behind this column changed" as drift.
    ///
    /// Null on every ordinary column and on every snapshot taken before spec
    /// 014, which is what makes those snapshots keep their old mapping -- the
    /// same property that makes <see cref="EnumName"/> back-compatible.
    /// </summary>
    [JsonPropertyName("resolvedFromUserType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResolvedFromUserType { get; set; }
}
