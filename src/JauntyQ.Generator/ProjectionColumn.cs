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

    /// <summary>
    /// True when this projection column came from an expression item (Feature A).
    /// Its type was resolved from built-in inference or a -- @type directive, not
    /// the schema.
    /// </summary>
    public bool IsExpression { get; set; }

    /// <summary>
    /// For an expression column whose type could NOT be resolved (no inference,
    /// no matching -- @type), holds the expression's alias so the generator can
    /// raise JNT3005. Empty when the type resolved.
    /// </summary>
    public string UnresolvedExpressionAlias { get; set; } = string.Empty;
}
