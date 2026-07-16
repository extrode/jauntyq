using JauntyQ.SqlParser.IR;

namespace JauntyQ.Analysis.Impact;

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
    /// map, not the outer statement's. Without this, a migration that only
    /// touched a column referenced inside a subquery/CTE body, a WHERE/SET
    /// predicate, or an ORDER BY clause produced a false SAFE verdict: the
    /// query's dependency on that column was never recorded at all.
    /// </summary>
    private static void ResolveInto(QueryModel model, HashSet<string> tables, HashSet<ReferencedColumn> columns)
    {
        // alias -> real table name; also every real table name maps to itself.
        var aliasToTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            foreach (var table in inScope)
                columns.Add(new ReferencedColumn(table, column));
        }

        foreach (var c in model.Columns)
        {
            if (c.IsExpression)
            {
                // Only a pure aggregate over a single column exposes a real column dep.
                if (!string.IsNullOrEmpty(c.AggregateArgColumnName))
                    AddColumn(c.AggregateArgTableAlias, c.AggregateArgColumnName);
                continue;
            }
            AddColumn(c.TableAlias, c.ColumnName);
        }

        foreach (var c in model.Returning)
            if (!c.IsExpression)
                AddColumn(c.TableAlias, c.ColumnName);

        foreach (var j in model.Joins)
        {
            AddColumn(j.LeftTable, j.LeftColumn);
            AddColumn(j.RightTable, j.RightColumn);
        }

        // WHERE-bound predicate parameters and literals, and SET/VALUES write
        // targets: a query that only compares/writes a column (never
        // projects, joins or returns it) still depends on that column's shape.
        foreach (var p in model.Parameters)
            AddColumn(p.BoundTableAlias, p.BoundColumnName);

        foreach (var l in model.Literals)
            AddColumn(l.BoundTableAlias, l.BoundColumnName);

        // ORDER BY: only PlainColumn items carry a bound alias/column; the
        // others (Expression/Ordinal/ProjectedAlias) leave it empty, which
        // AddColumn already no-ops on.
        foreach (var o in model.OrderBy)
            AddColumn(o.BoundTableAlias, o.BoundColumnName);

        // Nested statement scopes: each resolved against its own FROM/JOIN
        // tables, not this statement's aliasToTable/inScope.
        foreach (var sq in model.Subqueries)
            ResolveInto(sq.Body, tables, columns);

        foreach (var cte in model.Ctes)
            ResolveInto(cte.Body, tables, columns);
    }

    private sealed class ReferencedColumnComparer : IEqualityComparer<ReferencedColumn>
    {
        public static readonly ReferencedColumnComparer Instance = new();

        public bool Equals(ReferencedColumn x, ReferencedColumn y) =>
            string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(ReferencedColumn obj)
        {
            unchecked
            {
                int h = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table ?? "");
                h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Column ?? "");
                return h;
            }
        }
    }
}
