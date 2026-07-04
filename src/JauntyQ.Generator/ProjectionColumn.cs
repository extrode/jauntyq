namespace JauntyQ.Generator;

public class ProjectionColumn
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Ordinal { get; set; }

    /// <summary>
    /// Column name as it appears in the result set (output alias when aliased,
    /// otherwise the SQL column name). Used by the emitted shape guard.
    /// </summary>
    public string SourceName { get; set; } = string.Empty;
}
