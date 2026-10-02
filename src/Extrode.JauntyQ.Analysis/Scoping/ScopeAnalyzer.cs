using Extrode.JauntyQ.SqlParser.IR;

namespace Extrode.JauntyQ.Analysis.Scoping;

/// <summary>
/// One reach of a scoped table that the query does not prove is filtered by
/// its scope column. Spec 021, R8: each becomes its own JNT4005.
/// </summary>
public sealed class UnprovenReach
{
    public string Table { get; }
    public string Column { get; }
    public string Message { get; }

    public UnprovenReach(string table, string column, string message)
    {
        Table = table;
        Column = column;
        Message = message;
    }
}

/// <summary>
/// Finds every reach of a scoped table in a hand-written query that is not
/// proven to be filtered by the table's scope column (spec 021, R6 and R7).
///
/// A proof is a top-level AND conjunct of exactly <c>&lt;ref&gt;.&lt;column&gt; =
/// @param</c> (either way round), so OR, NOT, a literal, another column,
/// <c>IS NULL</c> and <c>IN (@ids)</c> fail it by shape, with no special case
/// each. Every statement scope is checked on its own terms: a CTE body or a
/// WHERE subquery needs its own proof, and an outer proof does not cover it.
/// The query is never rewritten; this only reports.
/// </summary>
public static class ScopeAnalyzer
{
    public static List<UnprovenReach> FindUnproven(QueryModel model, IReadOnlyList<ScopeColumn> scopes)
    {
        var result = new List<UnprovenReach>();
        Analyze(model, scopes, null, result);
        return result;
    }

    private static void Analyze(QueryModel model, IReadOnlyList<ScopeColumn> scopes, string? scopeName, List<UnprovenReach> result)
    {
        foreach (var cte in model.Ctes)
            Analyze(cte.Body, scopes, $"CTE '{cte.Name}'", result);
        foreach (var sub in model.Subqueries)
            Analyze(sub.Body, scopes, sub.Kind == SubqueryKind.In ? "an IN subquery" : "an EXISTS subquery", result);

        var real = new List<int>();
        for (int i = 0; i < model.Tables.Count; i++)
            if (!IsCte(model, model.Tables[i].TableName))
                real.Add(i);

        foreach (int i in real)
        {
            var table = model.Tables[i];
            foreach (var scope in scopes)
            {
                if (!string.Equals(scope.Table, table.TableName, StringComparison.OrdinalIgnoreCase))
                    continue;
                string? message = Check(model, i, real.Count == 1, scope, scopeName);
                if (message != null)
                    result.Add(new UnprovenReach(scope.Table, scope.Column, message));
            }
        }
    }

    private static bool IsCte(QueryModel model, string name)
    {
        foreach (var cte in model.Ctes)
            if (string.Equals(cte.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string? Check(QueryModel model, int index, bool onlyTable, ScopeColumn scope, string? scopeName)
    {
        var table = model.Tables[index];
        string reference = table.Alias.Length > 0 ? table.Alias : table.TableName;
        string param = "@" + CamelCase(scope.Column);
        string qualified = $"{reference}.{scope.Column} = {param}";
        string asAlias = table.Alias.Length > 0 ? $" (as '{table.Alias}')" : "";
        string inScope = scopeName == null ? "" : $" in {scopeName}";
        string whereClause = scopeName == null ? "the WHERE clause" : $"the WHERE clause of {scopeName}";
        string hatch = " If this query must reach every tenant's rows, mark it -- @unscoped <reason>.";

        bool isTarget = index == 0 && model.TargetTable != null;

        if (isTarget && model.StatementType == StatementType.Insert)
        {
            if (InsertBindsParameter(model, scope.Column))
                return null;
            return $"Scoped table '{scope.Table}' is the INSERT target{inScope}, and its scope column '{scope.Column}' is not given a parameter value. "
                + $"List '{scope.Column}' in the column list with a parameter such as {param}; a literal or an omitted column is not proof.{hatch}";
        }

        if (isTarget)
        {
            if (AnyProof(model.PredicateAtoms, scope.Column, a => TargetQualifier(model, index, a)))
                return null;
            string verb = model.StatementType == StatementType.Update ? "UPDATE" : "DELETE";
            return $"Scoped table '{scope.Table}' is the {verb} target{inScope} without a filter on its scope column '{scope.Column}'. "
                + $"Add {scope.Table}.{scope.Column} = {param} as a top-level AND condition of {whereClause}.{hatch}";
        }

        Func<string, bool> refersHere = q => q.Length == 0
            ? onlyTable
            : string.Equals(q, reference, StringComparison.OrdinalIgnoreCase);

        // A FULL JOIN keeps unmatched rows from both sides, so no ON condition
        // filters this table; only WHERE does.
        if (InFullJoin(model, index))
        {
            if (AnyProof(model.PredicateAtoms, scope.Column, refersHere))
                return null;
            return $"Scoped table '{scope.Table}'{asAlias} is in a FULL JOIN{inScope} without a filter on its scope column '{scope.Column}' in {whereClause}. "
                + $"A FULL JOIN keeps unmatched rows from both sides, so an ON condition filters nothing. Add {qualified} as a top-level AND condition of {whereClause}.{hatch}";
        }

        // The table's own ON filters it unless its own join is RIGHT, which
        // keeps every row of this table whatever the ON says.
        string? nullableBy = NullableBy(model, index);
        if (nullableBy != null)
        {
            if (table.Join != JoinKind.Right && AnyProof(table.OnAtoms, scope.Column, refersHere))
                return null;
            for (int j = index + 1; j < model.Tables.Count; j++)
            {
                var later = model.Tables[j];
                if (later.Join == JoinKind.Right && AnyProof(later.OnAtoms, scope.Column, refersHere))
                    return null;
            }
            return $"Scoped table '{scope.Table}'{asAlias} is on the nullable side of a {nullableBy}{inScope} without a filter on its scope column '{scope.Column}' in that join's ON clause. "
                + $"Add {qualified} to the ON clause; in WHERE it would turn the outer join into an inner join.{hatch}";
        }

        if (AnyProof(model.PredicateAtoms, scope.Column, refersHere))
            return null;
        foreach (var joined in model.Tables)
            if (joined.Join == JoinKind.None && AnyProof(joined.OnAtoms, scope.Column, refersHere))
                return null;

        string how = index == (model.TargetTable != null ? 1 : 0) ? "in FROM" : "in a JOIN";
        return $"Scoped table '{scope.Table}'{asAlias} is read {how}{inScope} without a filter on its scope column '{scope.Column}'. "
            + $"Add {qualified} as a top-level AND condition of {whereClause}.{hatch}";
    }

    private static bool InFullJoin(QueryModel model, int index)
    {
        for (int j = index; j < model.Tables.Count; j++)
            if (model.Tables[j].Join == JoinKind.Full)
                return true;
        return false;
    }

    private static string? NullableBy(QueryModel model, int index)
    {
        if (model.Tables[index].Join == JoinKind.Left)
            return "LEFT JOIN";
        for (int j = index + 1; j < model.Tables.Count; j++)
            if (model.Tables[j].Join == JoinKind.Right)
                return "RIGHT JOIN";
        return null;
    }

    private static bool TargetQualifier(QueryModel model, int index, string qualifier) =>
        qualifier.Length == 0
        || string.Equals(qualifier, model.Tables[index].TableName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(qualifier, model.TargetAlias, StringComparison.OrdinalIgnoreCase);

    private static bool InsertBindsParameter(QueryModel model, string column)
    {
        foreach (var literal in model.Literals)
            if (string.Equals(literal.BoundColumnName, column, StringComparison.OrdinalIgnoreCase))
                return false;
        foreach (var p in model.Parameters)
            if (p.IsWriteTarget && string.Equals(p.BoundColumnName, column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool AnyProof(List<PredicateAtom> atoms, string column, Func<string, bool> refersHere)
    {
        foreach (var atom in atoms)
        {
            if (atom.Terms.Count != 3)
                continue;
            var t = atom.Terms;
            if (t[1].Text != "=")
                continue;
            AtomTerm col;
            if (t[0].Kind == AtomTermKind.Column && t[2].Kind == AtomTermKind.Parameter)
                col = t[0];
            else if (t[0].Kind == AtomTermKind.Parameter && t[2].Kind == AtomTermKind.Column)
                col = t[2];
            else
                continue;
            if (string.Equals(col.Text, column, StringComparison.OrdinalIgnoreCase) && refersHere(col.TableAlias ?? ""))
                return true;
        }
        return false;
    }

    private static string CamelCase(string column)
    {
        string pascal = DialectMapper.ToPascalCase(column);
        return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
    }
}
