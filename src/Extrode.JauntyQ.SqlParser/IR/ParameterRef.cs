namespace Extrode.JauntyQ.SqlParser.IR;

public class ParameterRef
{
    public string Name { get; set; } = string.Empty;
    public string BoundTableAlias { get; set; } = string.Empty;
    public string BoundColumnName { get; set; } = string.Empty;

    /// <summary>
    /// True when the parameter's value is written into its bound column
    /// (INSERT VALUES slot or UPDATE SET assignment) rather than compared
    /// against it. Write targets get client-side length guards and a fixed
    /// DbParameter.Size; comparison parameters must never be truncated (a
    /// truncated key could match the wrong row), so they size dynamically.
    /// </summary>
    public bool IsWriteTarget { get; set; }

    /// <summary>
    /// True when the parameter is assigned in an upsert's update branch
    /// (<c>ON CONFLICT ... DO UPDATE SET col = @p</c> or <c>ON DUPLICATE KEY
    /// UPDATE col = @p</c>). It is also a write target, but it writes only
    /// when the row already exists, so it does not prove what the INSERT
    /// itself writes.
    /// </summary>
    public bool IsUpsertAssignment { get; set; }

    /// <summary>
    /// The predicate operator that bound this parameter to its column: "=",
    /// "!=", "&lt;&gt;", "&lt;", "&gt;", "&lt;=", "&gt;=", "IN", "LIKE" or
    /// "BETWEEN". Empty when the parameter is unbound or is an INSERT value slot;
    /// an UPDATE SET parameter carries "=". Lets analyzers distinguish an equality
    /// point lookup from a range/set predicate on the same column (JNT8008).
    /// </summary>
    public string ComparisonOp { get; set; } = string.Empty;

    /// <summary>
    /// The statement scope whose tables <see cref="BoundTableAlias"/> and
    /// <see cref="BoundColumnName"/> refer to, when the binding was carried up
    /// out of a predicate subquery or CTE body; null when it was bound in this
    /// statement. SQL binds an unqualified column to the innermost scope, so
    /// <c>delete from orders where customer_id in (select id from customers
    /// where id = @id)</c> compares <c>@id</c> with customers.id, not
    /// orders.id. Consumers resolve against this scope's tables, and checks
    /// that walk each scope on its own skip a carried binding in the outer one.
    /// </summary>
    public QueryModel? BoundScope { get; set; }
}
