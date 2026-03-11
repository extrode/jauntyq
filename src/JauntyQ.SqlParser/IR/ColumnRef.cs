namespace JauntyQ.SqlParser.IR;

public class ColumnRef
{
    public string TableAlias { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string OutputAlias { get; set; } = string.Empty;
}
