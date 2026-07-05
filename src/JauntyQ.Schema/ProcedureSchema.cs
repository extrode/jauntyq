using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

/// <summary>
/// A stored procedure that already lives in the database. Unlike a table, the
/// procedure body is owned by the DBA, not JauntyQ; the snapshot records only
/// the callable contract — parameters and the shape of the result set it
/// returns — so the generator can emit a typed <c>CommandType.StoredProcedure</c>
/// call and validate it against the live database via drift detection.
/// </summary>
public class ProcedureSchema
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Input/output parameters in declaration order.</summary>
    [JsonPropertyName("params")]
    public List<ProcedureParam> Params { get; set; } = new();

    /// <summary>
    /// Columns of the first result set, in ordinal order. Empty when the
    /// procedure returns no rows (it is invoked for its side effects / row
    /// count / output parameters).
    /// </summary>
    [JsonPropertyName("results")]
    public List<ColumnSchema> Results { get; set; } = new();
}

/// <summary>
/// Direction of a stored-procedure parameter. Serialized as a string in the
/// snapshot JSON. This type is consumed only by the CLI (schema pull) and the
/// source generator (compiler-time) — never by the AOT-published runtime app —
/// so the string-enum converter's reflection is not on any AOT path.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProcedureParamDirection
{
    In,
    Out,
    InOut,
    ReturnValue
}

public class ProcedureParam
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("dbType")]
    public string DbType { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public ProcedureParamDirection Direction { get; set; } = ProcedureParamDirection.In;

    [JsonPropertyName("isNullable")]
    public bool IsNullable { get; set; }

    [JsonPropertyName("maxLength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLength { get; set; }
}
