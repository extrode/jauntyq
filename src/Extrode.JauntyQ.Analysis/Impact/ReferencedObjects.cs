using Extrode.JauntyQ.SqlParser.IR;

namespace Extrode.JauntyQ.Analysis.Impact;

/// <summary>One schema column a query depends on, as (table, column).</summary>
public readonly struct ReferencedColumn
{
    public string Table { get; }
    public string Column { get; }
    public ReferencedColumn(string table, string column)
    {
        Table = table;
        Column = column;
    }
}

/// <summary>
/// The schema objects a parsed query depends on: the tables it reads/writes and
/// the (table, column) pairs it projects, filters, joins or returns. Resolved
/// from the <see cref="QueryModel"/> alias map alone (no schema needed). An
/// unqualified column in a multi-table query is attributed to every in-scope
/// table (the safe over-approximation): impact classification must never call a
/// query that touches a changed object SAFE, so erring toward RISKY is correct.
/// </summary>
public sealed class ReferencedObjects
{
    public IReadOnlyCollection<string> Tables { get; }
    public IReadOnlyCollection<ReferencedColumn> Columns { get; }

    private ReferencedObjects(IReadOnlyCollection<string> tables, IReadOnlyCollection<ReferencedColumn> columns)
    {
        Tables = tables;
        Columns = columns;
    }

    public static ReferencedObjects Resolve(QueryModel model)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columns = new HashSet<ReferencedColumn>(ReferencedColumnComparer.Instance);
        ResolveInto(model, tables, columns);
        return new ReferencedObjects(tables, columns);
    }

    /// <summary>
    /// Resolves one statement's own scope (its FROM/JOIN tables and everything
    /// bound within it) into the shared accumulator sets, then recurses into
    /// every nested statement scope it carries — WHERE-clause predicate
    /// subqueries (<see cref="QueryModel.Subqueries"/>) and CTE bodies
    /// (<see cref="QueryModel.Ctes"/>) — each resolved against its own alias
    /// map, not the outer statement's. A predicate subquery's map starts from
    /// <paramref name="outerAliases"/>, the enclosing statement's, because a
    /// correlated reference (<c>o.total</c> inside
    /// <c>exists (select 1 from items i where ...)</c>) names an outer table;
    /// the subquery's own tables shadow it. A CTE body cannot be correlated
    /// and gets none. Without this, a migration that only
    /// touched a column referenced inside a subquery/CTE body, a WHERE/SET
    /// predicate, or an ORDER BY clause produced a false SAFE verdict: the
    /// query's dependency on that column was never recorded at all.
    /// </summary>
    private static void ResolveInto(QueryModel model, HashSet<string> tables, HashSet<ReferencedColumn> columns,
        Dictionary<string, string>? outerAliases = null, List<string>? outerScope = null)
    {
        // alias -> real table name; also every real table name maps to itself.
        var aliasToTable = outerAliases == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(outerAliases, StringComparer.OrdinalIgnoreCase);
        var inScope = new List<string>();
        foreach (var t in model.Tables)
        {
            if (string.IsNullOrEmpty(t.TableName))
                continue;
            aliasToTable[t.TableName] = t.TableName;
            if (!string.IsNullOrEmpty(t.Alias))
                aliasToTable[t.Alias] = t.TableName;
            inScope.Add(t.TableName);
        }
        if (!string.IsNullOrEmpty(model.TargetTable))
        {
            aliasToTable[model.TargetTable!] = model.TargetTable!;
            inScope.Add(model.TargetTable!);
        }

        foreach (var table in inScope)
            tables.Add(table);

        void AddColumn(string alias, string column)
        {
            if (string.IsNullOrEmpty(column) || column == "*")
                return;
            if (!string.IsNullOrEmpty(alias))
            {
                var table = aliasToTable.TryGetValue(alias, out var resolved) ? resolved : alias;
                columns.Add(new ReferencedColumn(table, column));
                return;
            }
            // Unqualified: attribute to every in-scope table (over-approximation).
            // Inside a predicate subquery that includes the enclosing
            // statement's tables, since an unqualified column the subquery's own
            // tables lack is a correlated reference to one of them.
            foreach (var table in inScope)
                columns.Add(new ReferencedColumn(table, column));
            if (outerScope != null)
            {
                foreach (var table in outerScope)
                    columns.Add(new ReferencedColumn(table, column));
            }
        }

        foreach (var c in model.Columns)
        {
            if (c.IsExpression)
            {
                // Every column touched anywhere in the expression's shape —
                // count(email), max(a.x), concat(a.first, a.last),
                // p.price * p.qty, a CASE branch's column, etc. — not just
                // the narrow SUM/AVG-single-bare-column special case.
                foreach (var (tableAlias, columnName) in c.ReferencedColumns)
                    AddColumn(tableAlias, columnName);
                continue;
            }
            AddColumn(c.TableAlias, c.ColumnName);
        }

        foreach (var c in model.Returning)
        {
            if (c.IsExpression)
            {
                foreach (var (tableAlias, columnName) in c.ReferencedColumns)
                    AddColumn(tableAlias, columnName);
                continue;
            }
            AddColumn(c.TableAlias, c.ColumnName);
        }

        foreach (var j in model.Joins)
        {
            AddColumn(j.LeftTable, j.LeftColumn);
            AddColumn(j.RightTable, j.RightColumn);
        }

        // WHERE-bound predicate parameters and literals, and SET/VALUES write
        // targets: a query that only compares/writes a column (never
        // projects, joins or returns it) still depends on that column's shape.
        // A binding carried up from a subquery or CTE body is added when that
        // scope is walked below, against its own tables.
        foreach (var p in model.Parameters)
        {
            if (p.BoundScope == null)
                AddColumn(p.BoundTableAlias, p.BoundColumnName);
        }

        foreach (var l in model.Literals)
            AddColumn(l.BoundTableAlias, l.BoundColumnName);

        // WHERE-clause shapes the parameter/literal binders can't see because
        // the column isn't directly adjacent to the comparison operator:
        // fn(column) = ... (FunctionOnColumn) and column = column, an
        // implicit join or a correlated subquery's back-reference to an outer
        // alias (ColumnComparedToColumn — every entry already carries just its
        // own side's alias/column, see ExtractPerfHints). LeadingWildcardLike
        // also carries a bound alias/column and is included for the same
        // reason. Without this, a migration that only touched a column
        // referenced solely via one of these shapes (e.g. `WHERE
        // UPPER(email) = ?` or a correlated `EXISTS (... WHERE oi.order_id =
        // o.id)`) produced a false SAFE verdict: the column was captured by
        // the parser (as a PerfHint, for the JNT8xxx performance analyzer) but
        // never carried into this dependency accumulator.
        foreach (var h in model.PerfHints)
            AddColumn(h.BoundTableAlias, h.BoundColumnName);

        // ORDER BY: PlainColumn items carry a bound alias/column directly.
        // Expression items (e.g. ORDER BY a.x + b.y, ORDER BY count(email))
        // carry no bound alias/column (AddColumn no-ops on those) but DO carry
        // every column the expression touches in ReferencedColumns — without
        // this, a migration that only touched a column referenced inside an
        // ORDER BY expression produced a false SAFE verdict. Ordinal/
        // ProjectedAlias items carry neither and are correctly inert.
        foreach (var o in model.OrderBy)
        {
            AddColumn(o.BoundTableAlias, o.BoundColumnName);
            foreach (var (tableAlias, columnName) in o.ReferencedColumns)
                AddColumn(tableAlias, columnName);
        }

        // Nested statement scopes: each resolved against its own FROM/JOIN
        // tables, not this statement's aliasToTable/inScope.
        foreach (var sq in model.Subqueries)
            ResolveInto(sq.Body, tables, columns, aliasToTable, Concat(inScope, outerScope));

        foreach (var body in model.ExistsExpressions)
            ResolveInto(body, tables, columns, aliasToTable, Concat(inScope, outerScope));

        foreach (var cte in model.Ctes)
            ResolveInto(cte.Body, tables, columns);
    }

    private static List<string> Concat(List<string> inner, List<string>? outer)
    {
        var all = new List<string>(inner);
        if (outer != null)
            all.AddRange(outer);
        return all;
    }

    private sealed class ReferencedColumnComparer : IEqualityComparer<ReferencedColumn>
    {
        public static readonly ReferencedColumnComparer Instance = new();

        public bool Equals(ReferencedColumn x, ReferencedColumn y) =>
            string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);

        // Stryker disable once Block : an empty body yields a constant hash, which still satisfies GetHashCode's contract (equal objects => equal hashes); only bucket distribution, not any observable result, changes
        public int GetHashCode(ReferencedColumn obj)
        {
            int h = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table);
            // Stryker disable once Arithmetic,Bitwise : GetHashCode's only contract is equal objects => equal hashes, which holds under any fixed combining transform
            return unchecked((h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column));
        }
    }
}
