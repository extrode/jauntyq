namespace JauntyQ.SqlParser.IR;

public class QueryModel
{
    public string Name { get; set; } = string.Empty;
    public StatementType StatementType { get; set; } = StatementType.Select;
    public string? TargetTable { get; set; }
    public List<TableRef> Tables { get; } = new();
    public List<ColumnRef> Columns { get; } = new();
    public List<JoinRef> Joins { get; } = new();
    public List<ParameterRef> Parameters { get; } = new();
    public List<LiteralBinding> Literals { get; } = new();
    public List<PerfHint> PerfHints { get; } = new();
    public List<string> UnsupportedConstructs { get; } = new();
}
