using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;

namespace Extrode.JauntyQ.SqlParser;

public static partial class SqlParser
{
    /// <summary>
    /// Parses a data-modifying CTE chain:
    ///   WITH name [(col, ...)] AS ( &lt;statement&gt; ) [, name2 AS ( ... )]* &lt;final statement&gt;
    /// Each CTE body and the final statement parse with the existing statement
    /// parsers. The final statement's type/fields are copied onto
    /// <paramref name="model"/> so a WITH file behaves like its final statement,
    /// while Ctes/VirtualColumns stay attached for validation.
    /// WITH RECURSIVE is out of scope and is reported as unsupported.
    /// </summary>
    private static void ParseWith(List<Token> tokens, QueryModel model)
    {
        int pos = 1; // skip WITH

        // Stryker disable once Equality : ParseWith is only called when tokens[0].Value=="WITH" (SqlParser.cs), and the tokenizer always appends a trailing End token, so tokens.Count is always >= 2 here -- pos(1) can never equal tokens.Count, making < and <= agree on every call
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "RECURSIVE")
        {
            model.WithRecursive = true;
            model.UnsupportedConstructs.Add("WITH RECURSIVE");
            return;
        }

        // Parse each CTE definition until a non-CTE token follows a completed
        // definition (i.e. the final statement begins).
        // Stryker disable once Equality : every entry to this while (the initial pos=1, or a re-entry via the comma-continue path below) leaves pos strictly less than tokens.Count -- the comma-continue path only advances past a real, non-sentinel Symbol token -- so < and <= always agree here
        while (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            var cte = new CteRef { Name = tokens[pos].Value };
            pos++;

            // Optional declared column list: ( a, b, c )
            // Stryker disable once Equality : pos here is always the index right after a just-consumed Identifier token (never the End sentinel, since an Identifier can't be End), so pos < tokens.Count always holds and <= agrees
            if (pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == "(")
            {
                // Stryker disable once Statement : removing this skip changes nothing observable -- the inner while's own body only skips non-Identifier tokens without recording them, so the "(" (never an Identifier) is silently absorbed as this loop's own first no-op iteration either way, leaving DeclaredColumns and the final pos identical
                pos++; // skip (
                while (pos < tokens.Count && !(tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == ")"))
                {
                    if (tokens[pos].Type == TokenType.Identifier)
                        cte.DeclaredColumns.Add(tokens[pos].Value);
                    pos++;
                }
                // Stryker disable once Equality : if the list never closes, pos reaches exactly tokens.Count here (one past the End sentinel index); every later use of pos is itself guarded by a fresh "pos < tokens.Count" check before any indexing, so letting pos become tokens.Count+1 instead of staying at tokens.Count changes no observable behavior
                if (pos < tokens.Count) pos++; // skip )
            }

            // Require AS ( ... )
            if (!(pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "AS"))
                break; // malformed; leave what we have
            pos++; // skip AS

            if (!(pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == "("))
                break;

            int bodyOpen = pos;
            int bodyClose = FindMatchingParen(tokens, bodyOpen, tokens.Count);
            // Stryker disable once Equality : bodyOpen is always >= 3 here (WITH, name, AS all precede it), so FindMatchingParen returns either -1 (not found) or a matched index > bodyOpen (>= 4) -- it can never be exactly 0, so < and <= agree
            if (bodyClose < 0)
                break;

            // Slice the body tokens (excluding the surrounding parens) and add
            // an End sentinel so the sub-parser terminates cleanly.
            var bodyTokens = new List<Token>();
            for (int i = bodyOpen + 1; i < bodyClose; i++)
                bodyTokens.Add(tokens[i]);
            // Stryker disable once String : an End token's Value is never read anywhere downstream (every consumer of an End token checks only its Type) -- the placeholder text here is inert
            bodyTokens.Add(new Token(TokenType.End, string.Empty));

            cte.Body = Parse(bodyTokens, model.Name + "_" + cte.Name);
            PopulateVirtualColumns(cte);
            model.Ctes.Add(cte);

            // Collect the body's parameters onto the outer model. When the outer
            // param already exists (added by the top-level token scan) but is
            // unbound, carry the body's binding so type inference can resolve it.
            foreach (var p in cte.Body.Parameters)
            {
                var existing = model.Parameters.Find(x => x.Name == p.Name);
                // Stryker disable once Initializer,Block : unreachable through Parse() -- the top-level scan (SqlParser.cs) registers every @parameter in the ORIGINAL token stream, including ones that only appear inside a CTE body, before ParseWith ever runs, so `existing` is never null here
                if (existing == null)
                {
                    model.Parameters.Add(new ParameterRef
                    {
                        Name = p.Name,
                        BoundTableAlias = p.BoundTableAlias,
                        BoundColumnName = p.BoundColumnName,
                        IsWriteTarget = p.IsWriteTarget,
                        ComparisonOp = p.ComparisonOp
                    });
                }
                else if (string.IsNullOrEmpty(existing.BoundColumnName) && !string.IsNullOrEmpty(p.BoundColumnName))
                {
                    existing.BoundTableAlias = p.BoundTableAlias;
                    existing.BoundColumnName = p.BoundColumnName;
                    existing.IsWriteTarget = p.IsWriteTarget;
                    existing.ComparisonOp = p.ComparisonOp;
                }
            }

            pos = bodyClose + 1;

            // A comma continues the CTE list; anything else starts the final statement.
            if (pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == ",")
            {
                pos++;
                continue;
            }
            break;
        }

        // Parse the final statement from `pos` to end as a standalone statement.
        // A trailing top-level ';' is a statement terminator, not part of the
        // final statement, so drop it before the End sentinel is (re)added —
        // otherwise it would be carried into the final statement's last clause.
        var finalTokens = new List<Token>();
        for (int i = pos; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == ";")
                continue;
            finalTokens.Add(tokens[i]);
        }
        // Stryker disable once Boolean,Equality : finalTokens is built by copying from the master tokens list, which always ends in a real End token (the tokenizer's own invariant) -- so whenever finalTokens is non-empty, its last element is always already End, making "last != End" unreachable; and Parse()'s dispatch loop stops at the FIRST End it sees, so an extra trailing End (from forcing this check true) or a missing one (from forcing it false on an empty list, which Parse() handles identically to a single-End list) changes nothing observable
        if (finalTokens.Count == 0 || finalTokens[finalTokens.Count - 1].Type != TokenType.End)
            // Stryker disable once Statement,String : reachable only when finalTokens is empty (the branch above), where Parse()'s dispatch loop already stops immediately regardless of whether this Add runs at all or what Value its End token carries -- an empty list and a single-End list are indistinguishable to that loop
            finalTokens.Add(new Token(TokenType.End, string.Empty));

        var finalModel = Parse(finalTokens, model.Name);
        CopyFinalStatement(finalModel, model);
    }

    /// <summary>Fills a CTE's exposed virtual columns from its declared list, else its body's SELECT/RETURNING output names.</summary>
    private static void PopulateVirtualColumns(CteRef cte)
    {
        if (cte.DeclaredColumns.Count > 0)
        {
            cte.VirtualColumns.AddRange(cte.DeclaredColumns);
            return;
        }

        // Stryker disable once Conditional : model.Columns is only ever populated by ParseSelect (SqlParser.cs), which only runs for a body whose statement actually reached a SELECT keyword -- so for any non-Select body reaching this branch, cte.Body.Columns is already empty, identical to the "new List<ColumnRef>()" fallback this condition guards against
        var source = cte.Body.HasReturning ? cte.Body.Returning
            : cte.Body.StatementType == StatementType.Select ? cte.Body.Columns
            : new List<ColumnRef>();

        foreach (var col in source)
        {
            string name = !string.IsNullOrEmpty(col.OutputAlias) ? col.OutputAlias : col.ColumnName;
            if (!string.IsNullOrEmpty(name) && name != "*")
                cte.VirtualColumns.Add(name);
        }
    }

    /// <summary>
    /// Copies the parsed final statement's shape onto the outer WITH model so a
    /// WITH file behaves exactly like its final statement (type, tables, joins,
    /// projection/returning, target). The outer model keeps its own Parameters
    /// and Ctes lists (already merged/attached).
    /// </summary>
    private static void CopyFinalStatement(QueryModel from, QueryModel to)
    {
        to.StatementType = from.StatementType;
        to.TargetTable = from.TargetTable;
        to.Tables.AddRange(from.Tables);
        to.Columns.AddRange(from.Columns);
        to.Joins.AddRange(from.Joins);
        to.Literals.AddRange(from.Literals);
        to.PerfHints.AddRange(from.PerfHints);
        to.PredicateAtoms.AddRange(from.PredicateAtoms);
        to.OrderBy.AddRange(from.OrderBy);
        to.Returning.AddRange(from.Returning);
        to.HasReturning = from.HasReturning;
        to.HasRowLimit = from.HasRowLimit;
        to.HasGroupBy = from.HasGroupBy;
        to.ExpressionsMissingAlias.AddRange(from.ExpressionsMissingAlias);
        to.Subqueries.AddRange(from.Subqueries);

        // Merge parameters (bindings resolved by the final statement's parse).
        foreach (var p in from.Parameters)
        {
            var existing = to.Parameters.Find(x => x.Name == p.Name);
            // Stryker disable once Statement,Block : unreachable through Parse() -- the outer model's own top-level scan (SqlParser.cs) already registered every @parameter in the ORIGINAL token stream, including ones that only appear in the final statement's own portion, before ParseWith (and this copy) ever runs, so `existing` is never null here
            if (existing == null)
            {
                to.Parameters.Add(p);
            }
            else if (string.IsNullOrEmpty(existing.BoundColumnName) && !string.IsNullOrEmpty(p.BoundColumnName))
            {
                existing.BoundTableAlias = p.BoundTableAlias;
                existing.BoundColumnName = p.BoundColumnName;
                existing.IsWriteTarget = p.IsWriteTarget;
                existing.ComparisonOp = p.ComparisonOp;
            }
        }

        // Carry unsupported constructs the final statement itself detected
        // (e.g. UNION), but not a spurious CTE flag — the WITH is supported.
        // Stryker disable once Logical,String : "CTE" is never actually added to UnsupportedConstructs anywhere in this codebase (grep confirms it), so from.UnsupportedConstructs can never contain it -- this guard is defensive dead code today, and forcing its comparison true/false or its literal to "" changes nothing reachable through the public Parse() API
        foreach (var c in from.UnsupportedConstructs)
            if (c != "CTE" && !to.UnsupportedConstructs.Contains(c))
                to.UnsupportedConstructs.Add(c);
    }
}
