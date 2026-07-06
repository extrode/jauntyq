namespace JauntyQ.SqlParser.IR;

/// <summary>
/// A common table expression declared with a leading WITH:
/// <c>name [(col, ...)] AS ( &lt;statement&gt; )</c>.
/// The body parses with the existing statement parsers; the CTE name becomes
/// an in-scope virtual table whose columns come from the declared column list,
/// or (when none is declared) from the body's SELECT projection / RETURNING
/// list. A body without either exposes no virtual columns, so referencing the
/// CTE in a FROM clause is then an error.
/// </summary>
public class CteRef
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Explicitly declared column names, e.g. WITH t (a, b) AS (...).</summary>
    public List<string> DeclaredColumns { get; } = new();

    /// <summary>The parsed body statement (SELECT/INSERT/UPDATE/DELETE).</summary>
    public QueryModel Body { get; set; } = new();

    /// <summary>
    /// Virtual column names this CTE exposes to later statements: the declared
    /// column list when present, otherwise the body's SELECT/RETURNING output
    /// names. Empty when the body returns no rows (e.g. a DELETE without
    /// RETURNING) — referencing such a CTE in FROM is an error.
    /// </summary>
    public List<string> VirtualColumns { get; } = new();
}
