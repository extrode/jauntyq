namespace JauntyQ.SqlParser.IR;

public class ColumnRef
{
    public string TableAlias { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string OutputAlias { get; set; } = string.Empty;

    /// <summary>
    /// True when this projection item is an expression rather than a plain
    /// (optionally qualified) column reference — e.g. count(*),
    /// (hashed_passphrase IS NOT NULL). Expression items always carry an
    /// explicit alias in <see cref="OutputAlias"/> and their raw SQL in
    /// <see cref="ExpressionSql"/>; their internals are opaque to validation.
    /// </summary>
    public bool IsExpression { get; set; }

    /// <summary>
    /// The verbatim SQL of an expression projection item (space-joined token
    /// text), excluding the trailing <c>AS alias</c>. Empty for plain columns.
    /// </summary>
    public string ExpressionSql { get; set; } = string.Empty;

    /// <summary>
    /// Db type inferred by the parser for an expression projection item from
    /// its shape: bigint (count), boolean (IS NULL / EXISTS / comparison).
    /// Empty when the shape gives no built-in type; the generator then requires
    /// a <c>-- @type</c> directive. Plain columns leave this empty (typed from
    /// the schema).
    /// </summary>
    public string InferredDbType { get; set; } = string.Empty;

    /// <summary>
    /// True when the built-in inference for this expression yields a NOT NULL
    /// type (count(...), IS [NOT] NULL, EXISTS(...), top-level comparison).
    /// Governs whether the emitted property is a value type or its nullable
    /// form. Ignored for plain columns.
    /// </summary>
    public bool InferredNotNull { get; set; }
}
