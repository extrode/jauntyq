namespace JauntyQ.SqlParser.IR;

public class ParameterRef
{
    public string Name { get; set; } = string.Empty;
    public string BoundTableAlias { get; set; } = string.Empty;
    public string BoundColumnName { get; set; } = string.Empty;

    /// <summary>
    /// True when the parameter's value is written into its bound column
    /// (INSERT VALUES slot or UPDATE SET assignment) rather than compared
    /// against it. Write targets get client-side length guards and a fixed
    /// DbParameter.Size; comparison parameters must never be truncated (a
    /// truncated key could match the wrong row), so they size dynamically.
    /// </summary>
    public bool IsWriteTarget { get; set; }

    /// <summary>
    /// The predicate operator that bound this parameter to its column: "=",
    /// "!=", "&lt;&gt;", "&lt;", "&gt;", "&lt;=", "&gt;=", "IN", "LIKE" or
    /// "BETWEEN". Empty when the parameter is unbound or is a write target
    /// (INSERT/UPDATE value slot). Lets analyzers distinguish an equality
    /// point lookup from a range/set predicate on the same column (JNT8008).
    /// </summary>
    public string ComparisonOp { get; set; } = string.Empty;
}
