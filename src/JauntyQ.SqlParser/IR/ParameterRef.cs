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
}
