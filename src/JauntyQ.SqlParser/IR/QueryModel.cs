namespace JauntyQ.SqlParser.IR;

public class QueryModel
{
    public string Name { get; set; } = string.Empty;
    public StatementType StatementType { get; set; } = StatementType.Select;
    public string? TargetTable { get; set; }
    public List<TableRef> Tables { get; } = new();
    public List<ColumnRef> Columns { get; } = new();
    public List<JoinRef> Joins { get; } = new();
    public List<ParameterRef> Parameters { get; } = new();
    public List<LiteralBinding> Literals { get; } = new();
    public List<PerfHint> PerfHints { get; } = new();
    public List<OrderByRef> OrderBy { get; } = new();

    /// <summary>
    /// The WHERE clause split into top-level AND-conjuncts, each kept as
    /// classified tokens. Populated for every statement that has a WHERE, and
    /// consumed only by the <c>-- @mirrors</c> comparison (JNT8011): the rest of
    /// the IR cannot see an unparameterized predicate such as
    /// <c>deleted_at IS NULL</c>, so nothing else could compare two WHERE
    /// clauses honestly.
    /// </summary>
    public List<PredicateAtom> PredicateAtoms { get; } = new();
    public List<string> UnsupportedConstructs { get; } = new();

    /// <summary>
    /// Aliases of expression projection items that were captured without a
    /// required <c>AS alias</c>. Each becomes a JNT3004 error. (The value is
    /// the offending expression's raw SQL, used to build the message.)
    /// </summary>
    public List<string> ExpressionsMissingAlias { get; } = new();

    /// <summary>
    /// RETURNING projection on an INSERT/UPDATE/DELETE statement. Non-empty
    /// makes the statement row-returning (works with <c>-- @first</c>).
    /// Populated only for CRUD statements that carry a user-written RETURNING.
    /// </summary>
    public List<ColumnRef> Returning { get; } = new();

    /// <summary>
    /// True when the parsed statement carried a user-written RETURNING clause
    /// (even if the projection list resolved to zero items). Distinguishes a
    /// row-returning CRUD statement from a plain rows-affected one, and is the
    /// signal that <c>-- @identity</c> must not also be present.
    /// </summary>
    public bool HasReturning { get; set; }

    /// <summary>
    /// True when the statement takes a page: it carries <c>LIMIT</c> or
    /// <c>OFFSET</c>. OFFSET on its own is what makes T-SQL's
    /// <c>OFFSET n ROWS FETCH NEXT m ROWS ONLY</c> detectable, since
    /// FETCH/NEXT/ROWS/ONLY are not tokenizer keywords. <c>TOP n</c> is
    /// deliberately excluded — it is consumed and discarded by
    /// <c>SkipProjectionModifiers</c>, and a bare TOP with no OFFSET takes the
    /// whole result's head rather than a page of it.
    /// </summary>
    public bool HasRowLimit { get; set; }

    /// <summary>
    /// True when the statement carries a <c>GROUP BY</c>. Grouping collapses
    /// rows, so per-row uniqueness says nothing about the uniqueness of the
    /// result — which is why the pagination-stability check declines to reason
    /// about a grouped statement.
    /// </summary>
    public bool HasGroupBy { get; set; }

    /// <summary>
    /// Common table expressions declared with a leading WITH, in declaration
    /// order. Each contributes an in-scope virtual table for validation and,
    /// for the final statement, may be referenced in FROM/JOIN.
    /// </summary>
    public List<CteRef> Ctes { get; } = new();

    /// <summary>
    /// True when the statement opened with <c>WITH RECURSIVE</c>. Recursive
    /// CTEs are out of scope; the generator reports this as an unsupported
    /// construct rather than attempting to parse the body.
    /// </summary>
    public bool WithRecursive { get; set; }

    /// <summary>
    /// WHERE-clause predicate subqueries — <c>[NOT] IN (SELECT ...)</c> and
    /// <c>[NOT] EXISTS (SELECT ...)</c> — lifted out of this statement's token
    /// stream during parsing. Each carries its own parsed SELECT body validated
    /// as an independent statement scope; the body contributes nothing to this
    /// statement's result shape.
    /// </summary>
    public List<SubqueryRef> Subqueries { get; } = new();
}
