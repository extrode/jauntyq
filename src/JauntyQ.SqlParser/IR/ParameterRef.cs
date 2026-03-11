namespace JauntyQ.SqlParser.IR;

public class ParameterRef
{
    public string Name { get; set; } = string.Empty;
    public string BoundTableAlias { get; set; } = string.Empty;
    public string BoundColumnName { get; set; } = string.Empty;
}
