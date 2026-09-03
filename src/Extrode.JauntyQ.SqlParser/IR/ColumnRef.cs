namespace Extrode.JauntyQ.SqlParser.IR;

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

    /// <summary>
    /// "SUM" or "AVG" when this expression's entire body is that aggregate
    /// applied to a single bare (optionally qualified) column reference —
    /// e.g. <c>sum(p.amount)</c>, not <c>sum(p.amount * 2)</c> or
    /// <c>round(sum(p.amount), 2)</c>. Empty otherwise. Unlike COUNT (always
    /// bigint), SUM/AVG's result type depends on the argument column's own
    /// DB type, so the parser only captures the shape here; the generator
    /// (which has schema access) resolves <see cref="AggregateArgColumnName"/>
    /// against the schema to type the result.
    /// </summary>
    public string AggregateFunction { get; set; } = string.Empty;

    /// <summary>Table alias qualifying <see cref="AggregateArgColumnName"/>, if any.</summary>
    public string AggregateArgTableAlias { get; set; } = string.Empty;

    /// <summary>The single column argument of <see cref="AggregateFunction"/>.</summary>
    public string AggregateArgColumnName { get; set; } = string.Empty;

    /// <summary>
    /// Every (table alias, column name) pair referenced anywhere inside this
    /// expression's token run — e.g. both entries for <c>p.price * p.qty</c>,
    /// or the one argument for <c>count(email)</c>, <c>max(a.x)</c>,
    /// <c>concat(a.first, a.last)</c>, a CASE branch's column, etc. Populated
    /// for every expression projection item regardless of shape. Unlike
    /// <see cref="AggregateArgColumnName"/> (narrowly scoped to the SUM/AVG
    /// schema-type-resolution case), this is a safe over-approximation used
    /// only for dependency tracking (migration-impact classification, see
    /// <c>Extrode.JauntyQ.Analysis.Impact.ReferencedObjects</c>) — it is never used to
    /// type the expression's result. Empty for plain columns.
    /// </summary>
    public List<(string TableAlias, string ColumnName)> ReferencedColumns { get; } = new();
}
