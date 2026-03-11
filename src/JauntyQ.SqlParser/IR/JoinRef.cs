namespace JauntyQ.SqlParser.IR;

public class JoinRef
{
    public string LeftTable { get; set; } = string.Empty;
    public string LeftColumn { get; set; } = string.Empty;
    public string RightTable { get; set; } = string.Empty;
    public string RightColumn { get; set; } = string.Empty;
}
