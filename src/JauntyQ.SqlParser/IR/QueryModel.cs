namespace JauntyQ.SqlParser.IR;

public class QueryModel
{
    public string Name { get; set; } = string.Empty;
    public List<TableRef> Tables { get; } = new();
    public List<ColumnRef> Columns { get; } = new();
    public List<JoinRef> Joins { get; } = new();
    public List<ParameterRef> Parameters { get; } = new();
}
