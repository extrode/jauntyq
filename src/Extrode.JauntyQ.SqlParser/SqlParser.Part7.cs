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

        if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "RECURSIVE")
        {
            model.WithRecursive = true;
            model.UnsupportedConstructs.Add("WITH RECURSIVE");
            return;
        }

        // Parse each CTE definition until a non-CTE token follows a completed
        // definition (i.e. the final statement begins).
        while (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            var cte = new CteRef { Name = tokens[pos].Value };
            pos++;

            // Optional declared column list: ( a, b, c )
            if (pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == "(")
            {
                // The "(" itself is skipped by the loop below as a non-Identifier.
                while (pos < tokens.Count && !(tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == ")"))
                {
                    if (tokens[pos].Type == TokenType.Identifier)
                        cte.DeclaredColumns.Add(tokens[pos].Value);
                    pos++;
                }
                // An unclosed list leaves pos at tokens.Count + 1; every later
                // use of pos re-checks "pos < tokens.Count" before indexing.
                pos++; // skip )
            }

            // Require AS ( ... )
            if (!(pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "AS"))
                break; // malformed; leave what we have
            pos++; // skip AS

            if (!(pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == "("))
                break;

            int bodyOpen = pos;
            int bodyClose = FindMatchingParen(tokens, bodyOpen, tokens.Count);
            if (bodyClose == -1)
                break;

            // Slice the body tokens (excluding the surrounding parens) and add
            // an End sentinel so the sub-parser terminates cleanly.
            var bodyTokens = new List<Token>();
            for (int i = bodyOpen + 1; i < bodyClose; i++)
                bodyTokens.Add(tokens[i]);
            // Stryker disable once Statement : every body-parser loop also stops at "pos < tokens.Count" and the Part3 scans are bounded to reach the last real token, so a body without the sentinel parses identically (an empty "AS ()" body included); kept so the sliced list matches the tokenizer's shape
            bodyTokens.Add(Token.End);

            cte.Body = Parse(bodyTokens, model.Name + "_" + cte.Name);
            PopulateVirtualColumns(cte);
            model.Ctes.Add(cte);

            // Collect the body's parameters onto the outer model. The outer
            // param always exists (the top-level token scan registers every
            // @parameter, CTE bodies included); when it is unbound, carry the
            // body's binding so type inference can resolve it.
            foreach (var p in cte.Body.Parameters)
            {
                var existing = model.Parameters.Find(x => x.Name == p.Name)!;
                if (string.IsNullOrEmpty(existing.BoundColumnName) && !string.IsNullOrEmpty(p.BoundColumnName))
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
        // final statement, so drop it -- otherwise it would be carried into the
        // final statement's last clause. The End sentinel comes along with the
        // copy; an empty tail parses exactly as a lone End would.
        var finalTokens = new List<Token>();
        for (int i = pos; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == ";")
                continue;
            finalTokens.Add(tokens[i]);
        }
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

        // Columns is only populated by ParseSelect, so it is empty for any
        // body that is not a SELECT.
        var source = cte.Body.HasReturning ? cte.Body.Returning : cte.Body.Columns;

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
            var existing = to.Parameters.Find(x => x.Name == p.Name)!;
            if (string.IsNullOrEmpty(existing.BoundColumnName) && !string.IsNullOrEmpty(p.BoundColumnName))
            {
                existing.BoundTableAlias = p.BoundTableAlias;
                existing.BoundColumnName = p.BoundColumnName;
                existing.IsWriteTarget = p.IsWriteTarget;
                existing.ComparisonOp = p.ComparisonOp;
            }
        }

        // Carry unsupported constructs the final statement itself detected
        // (e.g. UNION). No dedup is needed: the only construct on `to` so far
        // is MULTI_STATEMENT, and every ';' is stripped from the final tokens.
        to.UnsupportedConstructs.AddRange(from.UnsupportedConstructs);
    }
}
