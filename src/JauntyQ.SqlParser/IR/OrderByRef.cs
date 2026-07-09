namespace JauntyQ.SqlParser.IR;

/// <summary>
/// The shape of a single <c>ORDER BY</c> item, so the performance analyzer can
/// act on plain column references and deliberately skip everything it cannot map
/// to a base column.
/// </summary>
public enum OrderByItemKind
{
    /// <summary>A plain (optionally alias-qualified) base column reference — the only kind JNT8007 evaluates.</summary>
    PlainColumn,

    /// <summary>A computed expression (function call, arithmetic, CASE, …). Skipped by JNT8007.</summary>
    Expression,

    /// <summary>A positional reference such as <c>ORDER BY 1</c>. Skipped by JNT8007.</summary>
    Ordinal,

    /// <summary>A bare name that matches a SELECT-list output alias rather than a base column. Skipped by JNT8007.</summary>
    ProjectedAlias
}

/// <summary>
/// One item of an <c>ORDER BY</c> clause captured by the parser, in clause order.
/// For <see cref="OrderByItemKind.PlainColumn"/> the bound alias/column are the
/// (optionally qualified) reference; for the other kinds they are empty and the
/// item exists only so the analyzer can see and skip it.
/// </summary>
public class OrderByRef
{
    public string BoundTableAlias { get; set; } = string.Empty;
    public string BoundColumnName { get; set; } = string.Empty;
    public OrderByItemKind Kind { get; set; } = OrderByItemKind.PlainColumn;
}
