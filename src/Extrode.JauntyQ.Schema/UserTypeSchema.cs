using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Extrode.JauntyQ.Schema;

/// <summary>
/// A user-defined type the database declares, captured by 'jaunty schema pull'.
///
/// Before spec 014 every one of these reached <c>DialectMapper</c>'s <c>_ =&gt;</c>
/// fallback and generated <c>object</c> with a JNT2007 warning -- three
/// different defects wearing one diagnostic. Capturing them splits the three:
///
///   * <see cref="UserTypeKind.Alias"/> / <see cref="UserTypeKind.Domain"/> --
///     RESOLVED at capture to the primitive underneath, so a column typed
///     'ssn' generates string exactly as varchar(11) would. This is the whole
///     of spec 014's UDT work.
///   * <see cref="UserTypeKind.Composite"/> / <see cref="UserTypeKind.TableType"/>
///     -- captured and never resolved. Capturing a type the generator will
///     not emit for looks redundant until you read JNT2025: the refusal names
///     the type AND its column list, and it can only do that because the shape
///     is in the snapshot. Doing the consumer's schema lookup for them is the
///     difference between a refusal and a dead end.
///
/// Empty for SQLite, which has no user-defined types at all.
/// </summary>
public class UserTypeSchema
{
    /// <summary>Snapshot key: the type name as declared.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Owning schema, for the reason <see cref="FunctionSchema.Schema"/> records one.</summary>
    [JsonPropertyName("schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Schema { get; set; }

    [JsonPropertyName("kind")]
    public UserTypeKind Kind { get; set; } = UserTypeKind.Alias;

    /// <summary>
    /// The primitive underneath, for <see cref="UserTypeKind.Alias"/> and
    /// <see cref="UserTypeKind.Domain"/>. Null for composites and table types,
    /// which have members rather than an underlying scalar.
    /// </summary>
    [JsonPropertyName("underlyingDbType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UnderlyingDbType { get; set; }

    /// <summary>Whether the declaration permits NULL. Postgres DOMAINs can carry NOT NULL.</summary>
    [JsonPropertyName("isNullable")]
    public bool IsNullable { get; set; } = true;

    [JsonPropertyName("maxLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; set; }

    [JsonPropertyName("precision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Precision { get; set; }

    [JsonPropertyName("scale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Scale { get; set; }

    /// <summary>
    /// Ordered members, for <see cref="UserTypeKind.Composite"/> and
    /// <see cref="UserTypeKind.TableType"/>. Empty for aliases and domains.
    /// This is the list JNT2025 prints so a consumer refused a TVP method has
    /// the shape they need to hand-write it.
    /// </summary>
    [JsonPropertyName("members")]
    public List<ColumnSchema> Members { get; set; } = new();
}

/// <summary>
/// What kind of user-defined type was captured. Serialized as a string through
/// the GENERIC converter: the non-generic <c>JsonStringEnumConverter</c> is
/// reflection-based and the AOT analyzer rejects it once
/// <see cref="DatabaseSchema"/> is read via <c>SchemaJsonContext</c>, which is
/// reachable at app runtime through Extrode.JauntyQ.Schema.Contract. Same reasoning as
/// <see cref="ProcedureParamDirection"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<UserTypeKind>))]
public enum UserTypeKind
{
    /// <summary>SQL Server <c>CREATE TYPE ssn FROM varchar(11)</c>. Resolved.</summary>
    Alias,

    /// <summary>PostgreSQL <c>CREATE DOMAIN ssn AS varchar(11)</c>. Resolved.</summary>
    Domain,

    /// <summary>PostgreSQL <c>CREATE TYPE t AS (a int, b text)</c>. Captured, not resolved.</summary>
    Composite,

    /// <summary>SQL Server <c>CREATE TYPE t AS TABLE (...)</c>. Captured, not resolved; JNT2025.</summary>
    TableType
}
