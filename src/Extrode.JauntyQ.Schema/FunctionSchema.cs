using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Extrode.JauntyQ.Schema;

/// <summary>
/// A scalar function that already lives in the database, captured by 'jaunty
/// schema pull' so the generator can emit a typed <c>db.Functions.{Name}(...)</c>
/// method instead of leaving the consumer to hand-write the SELECT wrapper.
/// The sibling of <see cref="SequenceSchema"/> and <see cref="ProcedureSchema"/>,
/// and the last of the three callable database objects to become first-class.
///
/// Captured for PostgreSQL, SQL Server and MySQL. SQLite has no stored
/// functions, so the dictionary stays empty there.
///
/// TABLE-VALUED functions are deliberately NOT modeled here. They need a
/// result-column shape and the SQL parser's FROM-clause handling, which is a
/// separate spec (014-plan.md SS6); carrying an always-empty resultColumns
/// field would read as a capability that does not exist.
///
/// User-defined AGGREGATES and WINDOW functions are excluded at capture, not
/// filtered here: they are out of scope for spec 014 (SS4), and an aggregate
/// reaching this entity would generate a method whose call semantics the
/// emitted SELECT cannot express.
/// </summary>
public class FunctionSchema
{
    /// <summary>Snapshot key: the function name as the database declares it.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Owning schema ("public", "dbo", the database name on MySQL). Recorded
    /// because a function name is only unique within one, and because the
    /// emitted SELECT has to qualify the call on dialects where the search
    /// path is not guaranteed to reach it.
    /// </summary>
    [JsonPropertyName("schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Schema { get; set; }

    /// <summary>Parameters in declaration order. Order is the contract: the
    /// emitted method binds positionally.</summary>
    [JsonPropertyName("params")]
    public List<FunctionParam> Params { get; set; } = new();

    /// <summary>
    /// The scalar value the function returns. Never null on a captured
    /// function -- a routine with no return is a procedure and belongs in
    /// <see cref="ProcedureSchema"/>.
    /// </summary>
    [JsonPropertyName("returnType")]
    public FunctionReturn Return { get; set; } = new();
}

/// <summary>
/// One parameter of a captured function. Deliberately <see cref="ProcedureParam"/>'s
/// shape minus <c>direction</c>: a scalar function has no OUT/INOUT analogue,
/// and modeling one would invite the emitter to grow a readback path that can
/// never fire.
///
/// A captured DEFAULT is deliberately absent. Spec 014 SS5.4 decided every
/// argument is required, because surfacing defaults as C# optional parameters
/// lets a re-pull silently change what an existing call site means.
/// </summary>
public class FunctionParam
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("dbType")]
    public string DbType { get; set; } = string.Empty;

    [JsonPropertyName("isNullable")]
    public bool IsNullable { get; set; }

    /// <summary>Max length for string/binary parameters; -1 means unbounded.</summary>
    [JsonPropertyName("maxLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; set; }

    /// <summary>Numeric precision (total digits) for decimal/numeric parameters.</summary>
    [JsonPropertyName("precision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Precision { get; set; }

    /// <summary>Numeric scale (fraction digits) for decimal/numeric parameters.</summary>
    [JsonPropertyName("scale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Scale { get; set; }

    /// <summary>
    /// Set when this parameter's declared type is a user-defined alias or
    /// DOMAIN that capture resolved to a primitive; <see cref="DbType"/> then
    /// carries the RESOLVED type. Null on ordinary parameters, exactly as
    /// <see cref="ColumnSchema.ResolvedFromUserType"/> is on ordinary columns.
    /// </summary>
    [JsonPropertyName("resolvedFromUserType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResolvedFromUserType { get; set; }
}

/// <summary>
/// A captured function's return type. Its own type rather than a bare string
/// because the facets matter for the same reason they do on a parameter: a
/// decimal return with no precision/scale is silently rounded by several
/// providers, which is the defect ProcedureParam.Precision exists to prevent.
/// </summary>
public class FunctionReturn
{
    [JsonPropertyName("dbType")]
    public string DbType { get; set; } = string.Empty;

    /// <summary>
    /// Whether the function may return NULL. Databases rarely declare this, so
    /// capture defaults it to true and the emitted method returns a nullable
    /// type -- the safe direction: a consumer can assert non-null, but cannot
    /// recover from a NullReferenceException inside generated code.
    /// </summary>
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
    /// Set when the declared return type is an alias/DOMAIN that capture
    /// resolved; <see cref="DbType"/> then carries the resolved primitive.
    /// </summary>
    [JsonPropertyName("resolvedFromUserType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResolvedFromUserType { get; set; }
}
