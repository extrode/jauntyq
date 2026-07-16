namespace JauntyQ.SqlParser.IR;

public enum JoinKind
{
    /// <summary>The first FROM table, or an INNER/CROSS JOIN: a matched row
    /// requires this table's columns to be present.</summary>
    None,

    /// <summary>LEFT [OUTER] JOIN: this table's columns are NULL on the
    /// projected row whenever no match exists.</summary>
    Left,

    /// <summary>RIGHT [OUTER] JOIN: every table already in the FROM/JOIN
    /// chain becomes optional instead of this one.</summary>
    Right,

    /// <summary>FULL [OUTER] JOIN: both this table and everything already in
    /// the FROM/JOIN chain are optional.</summary>
    Full
}

public class TableRef
{
    public string TableName { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;

    /// <summary>How this table entered the FROM/JOIN chain. Drives whether
    /// its columns must project as nullable regardless of the schema's own
    /// NOT NULL constraint — see ProjectionBuilder's join-nullability pass.</summary>
    public JoinKind Join { get; set; } = JoinKind.None;
}
