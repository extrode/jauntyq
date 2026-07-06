namespace JauntyQ.SqlParser.IR;

/// <summary>
/// The kind of WHERE-clause predicate subquery. Only these two forms are
/// supported; every other nested SELECT (scalar in a projection, derived table
/// in FROM, UNION arm) remains an unsupported construct.
/// </summary>
public enum SubqueryKind
{
    /// <summary><c>&lt;column&gt; [NOT] IN ( &lt;SELECT&gt; )</c>. Its inner SELECT must project exactly one column.</summary>
    In,

    /// <summary><c>[NOT] EXISTS ( &lt;SELECT&gt; )</c>. Its inner SELECT's projection shape is irrelevant.</summary>
    Exists
}

/// <summary>
/// A WHERE/AND/OR predicate subquery lifted out of the enclosing statement's
/// token stream. The <see cref="Body"/> is parsed with the ordinary SELECT
/// machinery and validated as its own statement scope: its tables never count
/// as unjoined outer tables (JNT8001) and never contribute to the outer result
/// shape, but its own columns, ambiguity and parameter bindings are checked
/// like any statement.
/// </summary>
public class SubqueryRef
{
    public SubqueryKind Kind { get; set; }

    /// <summary>The parsed inner SELECT statement.</summary>
    public QueryModel Body { get; set; } = new();
}
