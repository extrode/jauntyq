namespace JauntyQ.SqlParser.IR;

public enum PerfHintKind
{
    /// <summary>WHERE fn(column) = ... : the function call defeats any index on the column.</summary>
    FunctionOnColumn,

    /// <summary>WHERE column LIKE '%...' : a leading wildcard cannot seek an index.</summary>
    LeadingWildcardLike,

    /// <summary>
    /// WHERE column = column : an equality between two plain column references
    /// (an implicit join or a correlated subquery's back-reference to an outer
    /// alias, e.g. inside EXISTS/IN). Each side is captured as its own hint
    /// carrying just that side's alias/column; unlike an explicit JOIN...ON,
    /// this shape is only visible as a WHERE-clause token pattern.
    /// </summary>
    ColumnComparedToColumn
}

/// <summary>
/// A pattern in the WHERE clause that the JNT8xxx performance analyzer
/// surfaces as a warning. Captured by the parser (token-level patterns the
/// query model cannot express), resolved against the schema by the validator.
/// </summary>
public class PerfHint
{
    public PerfHintKind Kind { get; set; }
    public string FunctionName { get; set; } = string.Empty;
    public string BoundTableAlias { get; set; } = string.Empty;
    public string BoundColumnName { get; set; } = string.Empty;

    /// <summary>Extra context for the message (e.g. the LIKE pattern).</summary>
    public string Detail { get; set; } = string.Empty;
}
