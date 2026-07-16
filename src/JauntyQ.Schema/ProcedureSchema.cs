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
/// snapshot JSON via the generic, source-gen/AOT-safe string-enum converter
/// (the non-generic <c>JsonStringEnumConverter</c> is reflection-based and
/// rejected by the AOT analyzer once <see cref="DatabaseSchema"/> is read via
/// <see cref="SchemaJsonContext"/>, which is reachable at app runtime).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProcedureParamDirection>))]
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

    /// <summary>
    /// Numeric precision (total digits) for decimal/numeric/money parameters.
    /// Without this, an emitted OUT/INOUT decimal DbParameter carries no
    /// Precision/Scale at all, which several providers require to correctly
    /// size the return value -- an unset Precision/Scale can silently
    /// truncate or round the value the procedure actually returned.
    /// </summary>
    [JsonPropertyName("precision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Precision { get; set; }

    /// <summary>Numeric scale (fraction digits) for decimal/numeric/money parameters.</summary>
    [JsonPropertyName("scale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Scale { get; set; }
}
