using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.SqlParser;

public static partial class SqlParser
{
    public static QueryModel Parse(List<Token> tokens, string queryName)
    {
        var model = new QueryModel { Name = queryName };
        int pos = 0;

        // Collect all parameters first (they can appear anywhere)
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.Parameter)
            {
                if (!model.Parameters.Exists(p => p.Name == token.Value))
                {
                    model.Parameters.Add(new ParameterRef { Name = token.Value });
                }
            }
        }

        // Leading WITH: parse the CTE chain and the final statement. The final
        // statement's type becomes the model's statement type. WITH RECURSIVE
        // is out of scope and short-circuits to an unsupported construct.
        if (tokens.Count > 0 && tokens[0].Type == TokenType.Keyword && tokens[0].Value == "WITH")
        {
            ParseWith(tokens, model);
            return model;
        }

        // Detect unsupported constructs
        DetectUnsupportedConstructs(tokens, model);

        // Capture literals bound to columns (compile-time value-safety checks)
        ExtractLiteralBindings(tokens, model);

        // Capture WHERE-clause anti-patterns for the JNT8xxx performance analyzer
        ExtractPerfHints(tokens, model);

        // Detect statement type from the first keyword
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.Keyword)
            {
                switch (tokens[i].Value)
                {
                    case "INSERT":
                        model.StatementType = StatementType.Insert;
                        ParseInsert(tokens, i + 1, model);
                        return model;
                    case "UPDATE":
                        model.StatementType = StatementType.Update;
                        ParseUpdate(tokens, i + 1, model);
                        return model;
                    case "DELETE":
                        model.StatementType = StatementType.Delete;
                        ParseDelete(tokens, i + 1, model);
                        return model;
                    case "SELECT":
                        break; // fall through to normal SELECT parsing
                }
                break;
            }
        }

        // Extract parameter-to-column bindings (col = @param or @param = col)
        ExtractParameterBindings(tokens, model);

        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            var token = tokens[pos];

            if (token.Type == TokenType.Keyword)
            {
                switch (token.Value)
                {
                    case "SELECT":
                        pos = ParseSelect(tokens, pos + 1, model);
                        break;
                    case "FROM":
                        pos = ParseFrom(tokens, pos + 1, model);
                        break;
                    case "JOIN":
                    case "INNER":
                    case "LEFT":
                    case "RIGHT":
                    case "CROSS":
                    case "FULL":
                        pos = ParseJoin(tokens, pos, model);
                        break;
                    case "ORDER":
                        pos = ParseOrderBy(tokens, pos + 1, model);
                        break;
                    default:
                        pos++;
                        break;
                }
            }
            else
            {
                pos++;
            }
        }

        return model;
    }

    private static int ParseSelect(List<Token> tokens, int pos, QueryModel model)
        => ParseProjectionList(tokens, SkipProjectionModifiers(tokens, pos), model, model.Columns);

    /// <summary>
    /// Captures the <c>ORDER BY</c> item list into <see cref="QueryModel.OrderBy"/>
    /// in clause order. <paramref name="pos"/> points just past the ORDER keyword.
    /// A plain (optionally qualified) column becomes <see cref="OrderByItemKind.PlainColumn"/>
    /// — unless its bare name matches a SELECT-list alias, in which case it is a
    /// <see cref="OrderByItemKind.ProjectedAlias"/>; an integer is an
    /// <see cref="OrderByItemKind.Ordinal"/>; anything else is an
    /// <see cref="OrderByItemKind.Expression"/>. ASC/DESC direction keywords are
    /// consumed and ignored. The list ends at LIMIT/OFFSET/UNION/another clause or End.
    /// </summary>
    private static int ParseOrderBy(List<Token> tokens, int pos, QueryModel model)
    {
        // consume the BY of "ORDER BY"
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "BY")
            pos++;

        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            var token = tokens[pos];

            if (IsOrderByTerminator(token))
                break;

            // commas between items and per-item direction keywords carry no column
            if ((token.Type == TokenType.Symbol && token.Value == ",") ||
                (token.Type == TokenType.Keyword && (token.Value == "ASC" || token.Value == "DESC")))
            {
                pos++;
                continue;
            }

            // ORDER BY <n> : a positional reference
            if (token.Type == TokenType.Number)
            {
                model.OrderBy.Add(new OrderByRef { Kind = OrderByItemKind.Ordinal });
                pos++;
                continue;
            }

            // A plain column is a lone (optionally qualified) identifier that ends
            // the item (comma / direction / clause / End). Anything else — a '(',
            // an operator, a function call — is an expression.
            if (token.Type == TokenType.Identifier && IsPlainOrderByItem(tokens, pos))
            {
                var (tableAlias, columnName) = SplitQualifiedName(token.Value);
                var kind = string.IsNullOrEmpty(tableAlias) && IsProjectedAlias(model, columnName)
                    ? OrderByItemKind.ProjectedAlias
                    : OrderByItemKind.PlainColumn;
                model.OrderBy.Add(new OrderByRef
                {
                    BoundTableAlias = tableAlias,
                    BoundColumnName = columnName,
                    Kind = kind
                });
                pos++;
                continue;
            }

            // Expression item: consume tokens at paren-depth 0 to the next
            // top-level comma or terminator; record one Expression item.
            pos = SkipOrderByExpression(tokens, pos);
            model.OrderBy.Add(new OrderByRef { Kind = OrderByItemKind.Expression });
        }

        return pos;
    }

    /// <summary>A token that ends the ORDER BY item list (a following clause or End).</summary>
    private static bool IsOrderByTerminator(Token token) =>
        token.Type == TokenType.End ||
        (token.Type == TokenType.Keyword && (IsClauseKeyword(token.Value) || token.Value == "OFFSET"));

    /// <summary>
    /// True when the identifier at <paramref name="pos"/> is a complete ORDER BY
    /// item: it is immediately followed by a comma, an ASC/DESC direction, a
    /// terminator, or End. A following '(' or operator makes it an expression.
    /// </summary>
    private static bool IsPlainOrderByItem(List<Token> tokens, int pos)
    {
        if (pos + 1 >= tokens.Count)
            return true;

        var next = tokens[pos + 1];
        if (next.Type == TokenType.End)
            return true;
        if (next.Type == TokenType.Symbol && next.Value == ",")
            return true;
        if (next.Type == TokenType.Keyword && (next.Value == "ASC" || next.Value == "DESC"))
            return true;
        return IsOrderByTerminator(next);
    }

    /// <summary>Consumes an expression ORDER BY item to the next top-level comma or terminator.</summary>
    private static int SkipOrderByExpression(List<Token> tokens, int pos)
    {
        int depth = 0;
        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            var t = tokens[pos];
            if (t.Type == TokenType.Symbol && t.Value == "(")
                depth++;
            else if (t.Type == TokenType.Symbol && t.Value == ")")
                depth--;
            else if (depth == 0 && t.Type == TokenType.Symbol && t.Value == ",")
                break;
            else if (depth == 0 && IsOrderByTerminator(t))
                break;

            pos++;
        }
        return pos;
    }

    /// <summary>True when <paramref name="name"/> matches a SELECT-list output alias (case-insensitive).</summary>
    private static bool IsProjectedAlias(QueryModel model, string name)
    {
        foreach (var col in model.Columns)
        {
            if (!string.IsNullOrEmpty(col.OutputAlias) &&
                string.Equals(col.OutputAlias, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Consumes SELECT-list modifiers — <c>DISTINCT</c> and T-SQL's <c>TOP n</c>
    /// / <c>TOP (n)</c> — immediately after SELECT, in either order, so they
    /// never reach <see cref="ParseProjectionList"/>. Left unhandled, a leading
    /// modifier is a Keyword token that doesn't match the plain-column check
    /// (which only matches Identifier tokens), so it falls through to
    /// <see cref="ParseExpressionItem"/> and gets glued onto the first real
    /// column as one opaque expression: for DISTINCT this silently loses the
    /// column's schema-based type inference (falls back to expression-shape
    /// inference), and for TOP it fails JNT3004 unconditionally (no directive
    /// workaround possible, since the diagnostic fires on the empty-alias
    /// condition before type inference runs). Neither modifier is needed
    /// downstream: the generator emits the original SQL text verbatim as the
    /// runtime CommandText, not a reconstruction from the parse tree.
    /// </summary>
    private static int SkipProjectionModifiers(List<Token> tokens, int pos)
    {
        while (true)
        {
            if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "DISTINCT")
            {
                pos++;
                continue;
            }

            if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "TOP")
            {
                pos++;
                if (pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == "(")
                {
                    pos++;
                    if (pos < tokens.Count && tokens[pos].Type == TokenType.Number)
                        pos++;
                    if (pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == ")")
                        pos++;
                }
                else if (pos < tokens.Count && tokens[pos].Type == TokenType.Number)
                {
                    pos++;
                }
                continue;
            }

            return pos;
        }
    }

    /// <summary>
    /// Parses a comma-separated projection list (used for SELECT lists and for
    /// RETURNING lists) into <paramref name="target"/>. A plain (optionally
    /// dot-qualified) column reference is captured as today; anything else is an
    /// expression item that runs to the next top-level comma or clause keyword,
    /// requires an explicit <c>AS alias</c> (missing alias -> ExpressionsMissingAlias),
    /// and carries its verbatim SQL plus a shape-based type inference. A '*'
    /// inside function-call parens is part of an expression and never registers
    /// as a star-select.
    /// </summary>
    private static int ParseProjectionList(List<Token> tokens, int pos, QueryModel model, List<ColumnRef> target)
    {
        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            var token = tokens[pos];

            // Stop at FROM, WHERE, or other major clause keywords
            if (token.Type == TokenType.Keyword && IsClauseKeyword(token.Value))
                break;

            // Skip commas between items
            if (token.Type == TokenType.Symbol && token.Value == ",")
            {
                pos++;
                continue;
            }

            // Top-level star-select: a bare '*' (not inside a function call).
            if (token.Type == TokenType.Symbol && token.Value == "*")
            {
                target.Add(new ColumnRef { ColumnName = "*" });
                pos++;
                continue;
            }

            // Qualified star-select: "alias.*". The tokenizer reads a
            // dot-qualified identifier's embedded '.' as part of the
            // identifier itself (so "u.name" is one Identifier token), but
            // '*' isn't an identifier char, so "u.*" tokenizes as the two
            // separate tokens Identifier("u.") + Symbol("*") rather than
            // merging. Without this check it falls through to the expression
            // path below and demands a nonsensical explicit alias (JNT3004)
            // instead of the purpose-built star-select rejection (JNT3002)
            // plain '*' already gets.
            if (token.Type == TokenType.Identifier && token.Value.EndsWith(".", StringComparison.Ordinal) &&
                pos + 1 < tokens.Count && tokens[pos + 1].Type == TokenType.Symbol && tokens[pos + 1].Value == "*")
            {
                string tableAlias = token.Value.Substring(0, token.Value.Length - 1);
                target.Add(new ColumnRef { TableAlias = tableAlias, ColumnName = "*" });
                pos += 2;
                continue;
            }

            // A plain column reference is a lone identifier whose next token
            // ends the item (comma / clause keyword / End) or begins an alias
            // (AS or an implicit-alias identifier). Anything else — a function
            // call, a parenthesised expression, an operator — is an expression.
            if (token.Type == TokenType.Identifier && IsPlainColumnItem(tokens, pos))
            {
                var (tableAlias, columnName) = SplitQualifiedName(token.Value);
                string outputAlias = string.Empty;

                if (pos + 1 < tokens.Count)
                {
                    if (tokens[pos + 1].Type == TokenType.Keyword && tokens[pos + 1].Value == "AS")
                    {
                        if (pos + 2 < tokens.Count && tokens[pos + 2].Type == TokenType.Identifier)
                        {
                            outputAlias = tokens[pos + 2].Value;
                            pos += 2; // skip AS and alias
                        }
                    }
                    else if (tokens[pos + 1].Type == TokenType.Identifier &&
                             !IsClauseKeyword(tokens[pos + 1].Value))
                    {
                        if (pos + 2 < tokens.Count)
                        {
                            var afterAlias = tokens[pos + 2];
                            if ((afterAlias.Type == TokenType.Symbol && afterAlias.Value == ",") ||
                                (afterAlias.Type == TokenType.Keyword && IsClauseKeyword(afterAlias.Value)) ||
                                afterAlias.Type == TokenType.End)
                            {
                                outputAlias = tokens[pos + 1].Value;
                                pos++;
                            }
                        }
                    }
                }

                target.Add(new ColumnRef
                {
                    TableAlias = tableAlias,
                    ColumnName = columnName,
                    OutputAlias = outputAlias
                });
                pos++;
                continue;
            }

            // Expression item: consume tokens at paren-depth 0 until a top-level
            // comma or a clause keyword. The trailing "AS alias" is required and
            // is peeled off the captured run.
            pos = ParseExpressionItem(tokens, pos, model, target);
        }

        return pos;
    }

    /// <summary>
    /// A projection item starting at <paramref name="pos"/> is a plain column
    /// when it is a single (optionally dot-qualified) identifier immediately
    /// followed by a comma, a clause keyword, End, an AS, or an implicit alias
    /// identifier. If the identifier is instead followed by '(' (function call)
    /// or an operator, the item is an expression.
    /// </summary>
    private static bool IsPlainColumnItem(List<Token> tokens, int pos)
    {
        if (pos + 1 >= tokens.Count)
            return true; // lone identifier at end of list

        var next = tokens[pos + 1];
        if (next.Type == TokenType.End)
            return true;
        if (next.Type == TokenType.Symbol && next.Value == ",")
            return true;
        if (next.Type == TokenType.Keyword && (next.Value == "AS" || IsClauseKeyword(next.Value)))
            return true;
        // implicit alias: identifier followed by a non-clause identifier
        if (next.Type == TokenType.Identifier && !IsClauseKeyword(next.Value))
            return true;
        // '(' -> function call, any operator/symbol -> expression
        return false;
    }

    private static int ParseExpressionItem(List<Token> tokens, int pos, QueryModel model, List<ColumnRef> target)
    {
        int depth = 0;
        var run = new List<Token>();
        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            var t = tokens[pos];
            if (t.Type == TokenType.Symbol && t.Value == "(")
                depth++;
            else if (t.Type == TokenType.Symbol && t.Value == ")")
                depth--;
            else if (depth == 0 && t.Type == TokenType.Symbol && t.Value == ",")
                break;
            else if (depth == 0 && t.Type == TokenType.Keyword && IsClauseKeyword(t.Value))
                break;

            run.Add(t);
            pos++;
        }

        // Peel a trailing "AS alias" off the captured run.
        string alias = string.Empty;
        int exprEnd = run.Count;
        if (run.Count >= 2 &&
            run[run.Count - 2].Type == TokenType.Keyword && run[run.Count - 2].Value == "AS" &&
            run[run.Count - 1].Type == TokenType.Identifier)
        {
            alias = run[run.Count - 1].Value;
            exprEnd = run.Count - 2;
        }

        string exprSql = RenderExpressionSql(run, exprEnd);

        if (string.IsNullOrEmpty(alias))
        {
            // Missing required AS alias — record for JNT3004; still capture the
            // item so downstream ordinal counts stay consistent.
            model.ExpressionsMissingAlias.Add(exprSql);
        }

        var col = new ColumnRef
        {
            IsExpression = true,
            ExpressionSql = exprSql,
            OutputAlias = alias
        };
        InferExpressionType(run, exprEnd, col);
        target.Add(col);
        return pos;
    }

    /// <summary>Space-joins the expression tokens (excluding a trailing AS alias) into verbatim-ish SQL for messages.</summary>
    private static string RenderExpressionSql(List<Token> run, int count)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(run[i].Value);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Built-in type inference from an expression's shape:
    ///   count(...)                -> bigint  NOT NULL
    ///   EXISTS(...)               -> boolean NOT NULL
    ///   top-level IS [NOT] NULL   -> boolean NOT NULL
    ///   top-level comparison op   -> boolean NOT NULL
    /// No match leaves InferredDbType empty; the generator then requires -- @type.
    /// </summary>
    private static void InferExpressionType(List<Token> run, int count, ColumnRef col)
    {
        // Strip a single fully-enclosing paren pair so a wrapped predicate like
        // "( x IS NOT NULL )" exposes its top-level operator. Repeats while the
        // outermost parens enclose the whole run. Never unwraps a parenthesized
        // SELECT (scalar subquery): its result type can't be inferred from
        // shape, and unwrapping would expose the subquery's own internal
        // predicates (e.g. a WHERE clause's "=") as if they were top-level
        // comparisons on the outer expression.
        int lo = 0, hi = count;
        while (hi - lo >= 2 &&
               run[lo].Type == TokenType.Symbol && run[lo].Value == "(" &&
               run[hi - 1].Type == TokenType.Symbol && run[hi - 1].Value == ")" &&
               EnclosesWholeRun(run, lo, hi))
        {
            if (lo + 1 < hi && run[lo + 1].Type == TokenType.Keyword && run[lo + 1].Value == "SELECT")
                break;

            lo++;
            hi--;
        }

        // A top-level comparison or IS [NOT] NULL yields boolean — checked
        // before count/exists heads so "count(*) > 0" infers boolean, not bigint.
        // CASE...END is tracked as its own nesting level alongside parens so a
        // comparison inside a WHEN clause (e.g. "CASE WHEN status = 1 THEN ...
        // END") is not mistaken for the enclosing expression's own top-level
        // operator — a CASE's result type is its THEN/ELSE branch type, not
        // boolean, and can't be inferred from shape at all.
        int depth = 0;
        int caseDepth = 0;
        for (int i = lo; i < hi; i++)
        {
            var t = run[i];
            if (t.Type == TokenType.Symbol && t.Value == "(") { depth++; continue; }
            if (t.Type == TokenType.Symbol && t.Value == ")") { depth--; continue; }
            if (t.Type == TokenType.Keyword && t.Value == "CASE") { caseDepth++; continue; }
            if (t.Type == TokenType.Keyword && t.Value == "END") { caseDepth--; continue; }
            if (depth != 0 || caseDepth != 0)
                continue;

            if (t.Type == TokenType.Keyword && t.Value == "IS")
            {
                col.InferredDbType = "boolean";
                col.InferredNotNull = true;
                return;
            }
            if (t.Type == TokenType.Symbol && ComparisonOperators.Contains(t.Value))
            {
                col.InferredDbType = "boolean";
                col.InferredNotNull = true;
                return;
            }
        }

        // count(...) as the *entire* (unwrapped) expression body -> bigint NOT
        // NULL. Unlike sum/avg below, count's result type never depends on its
        // argument's shape (count(*), count(col), count(DISTINCT col), a
        // multi-arg count(a,b) -- all return bigint regardless), so the
        // argument itself is intentionally not shape-checked here. But the
        // call must still consume the WHOLE expression: "count(*) || ' rows'"
        // or "count(a) + count(b)" combine a count(...) call with something
        // else via an operator that isn't a top-level comparison (already
        // handled above), so the overall expression is NOT bigint-shaped even
        // though it STARTS with one -- checking only the head let those slip
        // through as a false bigint claim.
        if (hi - lo >= 2 &&
            IsCountHead(run[lo]) &&
            run[lo + 1].Type == TokenType.Symbol && run[lo + 1].Value == "(" &&
            FindMatchingClose(run, lo + 1, hi) == hi - 1)
        {
            col.InferredDbType = "bigint";
            col.InferredNotNull = true;
            return;
        }

        // EXISTS(...) as the head -> boolean NOT NULL.
        if (hi - lo >= 2 &&
            run[lo].Type == TokenType.Keyword && run[lo].Value == "EXISTS" &&
            run[lo + 1].Type == TokenType.Symbol && run[lo + 1].Value == "(")
        {
            col.InferredDbType = "boolean";
            col.InferredNotNull = true;
            return;
        }

        // sum(<col>) / avg(<col>) as the *entire* expression body, with a
        // single bare (optionally qualified) column-reference argument — not
        // sum(a + b), sum(DISTINCT col), or a wrapping call like
        // round(sum(col), 2), which stay unresolved and still need -- @type.
        // Unlike count(...), the result type depends on the argument
        // column's own DB type, which the parser can't see (no schema
        // access), so only the shape is captured here; ProjectionBuilder
        // resolves the argument against the schema to type the result.
        if (hi - lo == 4 &&
            IsAggregateHead(run[lo], "SUM", "AVG") &&
            run[lo + 1].Type == TokenType.Symbol && run[lo + 1].Value == "(" &&
            run[lo + 2].Type == TokenType.Identifier &&
            run[lo + 3].Type == TokenType.Symbol && run[lo + 3].Value == ")")
        {
            var (argAlias, argColumn) = SplitQualifiedName(run[lo + 2].Value);
            col.AggregateFunction = run[lo].Value.ToUpperInvariant();
            col.AggregateArgTableAlias = argAlias;
            col.AggregateArgColumnName = argColumn;
            return;
        }
    }

    /// <summary>
    /// True when the paren at <paramref name="lo"/> matches the paren at
    /// <paramref name="hi"/>-1 and encloses the entire [lo, hi) range — i.e. the
    /// pair is a redundant outer wrap, not two separate groups like "(a)+(b)".
    /// </summary>
    private static bool EnclosesWholeRun(List<Token> run, int lo, int hi)
    {
        int depth = 0;
        for (int i = lo; i < hi; i++)
        {
            if (run[i].Type == TokenType.Symbol && run[i].Value == "(") depth++;
            else if (run[i].Type == TokenType.Symbol && run[i].Value == ")")
            {
                depth--;
                if (depth == 0)
                    return i == hi - 1; // closes only at the very end
            }
        }
        return false;
    }

    /// <summary>
    /// The index of the ")" that matches the "(" at <paramref name="openIndex"/>
    /// (which must itself be an open-paren token), scanning no further than
    /// <paramref name="hi"/>. Returns -1 if unmatched within range. Used to
    /// confirm a call's parens consume the rest of the expression body (i.e.
    /// close exactly at <c>hi - 1</c>), not just that the call appears first.
    /// </summary>
    private static int FindMatchingClose(List<Token> run, int openIndex, int hi)
    {
        int depth = 0;
        for (int i = openIndex; i < hi; i++)
        {
            if (run[i].Type == TokenType.Symbol && run[i].Value == "(") depth++;
            else if (run[i].Type == TokenType.Symbol && run[i].Value == ")")
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }

    private static bool IsCountHead(Token t) =>
        (t.Type == TokenType.Keyword && t.Value == "COUNT") ||
        (t.Type == TokenType.Identifier && string.Equals(t.Value, "count", StringComparison.OrdinalIgnoreCase));

    private static bool IsAggregateHead(Token t, string a, string b) =>
        (t.Type == TokenType.Keyword && (t.Value == a || t.Value == b)) ||
        (t.Type == TokenType.Identifier &&
            (string.Equals(t.Value, a, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(t.Value, b, StringComparison.OrdinalIgnoreCase)));

}
