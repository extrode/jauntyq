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

        // A second top-level statement is recorded before anything else parses,
        // and for the WITH path too: ParseWith strips every ';' from the final
        // statement's tokens, so a check inside that path would never see one.
        DetectMultipleStatements(tokens, model);

        // Leading WITH: parse the CTE chain and the final statement. The final
        // statement's type becomes the model's statement type. WITH RECURSIVE
        // is out of scope and short-circuits to an unsupported construct.
        if (tokens.Count > 0 && tokens[0].Type == TokenType.Keyword && tokens[0].Value == "WITH")
        {
            ParseWith(tokens, model);
            return model;
        }

        // Split the WHERE into comparable atoms for the -- @mirrors check
        // (JNT8011). Deliberately FIRST of the three extractors below:
        // DetectUnsupportedConstructs lifts the predicate subqueries out of the
        // token stream, and that lift takes each subquery's leading NOT with it,
        // so after this point EXISTS and NOT EXISTS look identical.
        ExtractPredicateAtoms(tokens, model);

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
                    // OUTER dispatches too, solely to reach T-SQL's OUTER
                    // APPLY. In every other form OUTER is preceded by LEFT,
                    // RIGHT or FULL, which dispatch first and consume it
                    // inside ParseJoin's keyword loop -- so a bare OUTER
                    // arriving here is the APPLY case or nothing.
                    case "OUTER":
                        pos = ParseJoin(tokens, pos, model);
                        break;
                    case "ORDER":
                        pos = ParseOrderBy(tokens, pos + 1, model);
                        break;
                    // LIMIT/OFFSET and GROUP are recorded as flags and then
                    // skipped exactly as the default arm skipped them before:
                    // their operands are not needed, only the fact that the
                    // statement pages or groups.
                    case "LIMIT":
                    case "OFFSET":
                        model.HasRowLimit = true;
                        pos++;
                        break;
                    case "GROUP":
                        model.HasGroupBy = true;
                        pos++;
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
        => ParseProjectionList(tokens, SkipProjectionModifiers(tokens, pos, model), model, model.Columns);

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
            // top-level comma or terminator; record one Expression item along
            // with every column it touches (see CollectExpressionColumnRefs),
            // so ORDER BY a.x + b.y depends on both a.x and b.y just like the
            // equivalent SELECT-list expression does.
            int exprStart = pos;
            pos = SkipOrderByExpression(tokens, pos);
            var orderByRef = new OrderByRef { Kind = OrderByItemKind.Expression };
            orderByRef.ReferencedColumns.AddRange(CollectExpressionColumnRefs(tokens, exprStart, pos));
            model.OrderBy.Add(orderByRef);
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
    ///
    /// PostgreSQL's <c>DISTINCT ON (expr, ...)</c> is the one form that is NOT
    /// merely skippable, and it is refused rather than skipped. Plain DISTINCT
    /// does not change the result SHAPE, so dropping it is free; DISTINCT ON
    /// takes a parenthesized expression list that sits between SELECT and the
    /// projection, and skipping only the DISTINCT leaves ON as the first
    /// projection token. Measured before the fix: the ON, its parenthesized
    /// list and the real first column glue into ONE opaque expression item
    /// (<c>ON ( d.id ) d.id</c>), so the query models one fewer real column
    /// than it returns. Unaliased that lands on JNT3004 ("expression needs an
    /// alias"), which is loud but blames the wrong thing; ALIASED it passes
    /// validation entirely and the generated mapper types its first property
    /// from expression-shape inference over that garbage instead of from the
    /// schema. Recorded as missing grammar (JNT1009) for the same reason
    /// LATERAL is: nothing about the consumer's schema is wrong.
    /// </summary>
    private static int SkipProjectionModifiers(List<Token> tokens, int pos, QueryModel model)
    {
        while (true)
        {
            if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "DISTINCT")
            {
                pos++;

                // DISTINCT ON (...) — record the refusal, then step over the
                // whole modifier so the rest of the projection still parses as
                // itself. Leaving it in place would add a second, misleading
                // JNT3004 beside the real diagnostic.
                if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword &&
                    string.Equals(tokens[pos].Value, "ON", StringComparison.OrdinalIgnoreCase))
                {
                    if (!model.UnsupportedConstructs.Contains("DISTINCT ON"))
                        model.UnsupportedConstructs.Add("DISTINCT ON");

                    pos++;
                    pos = SkipBalancedParens(tokens, pos);
                }

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
    /// Steps over a parenthesized group starting at <paramref name="pos"/>,
    /// returning the position just past its matching close paren. Nested
    /// parens are counted, so <c>DISTINCT ON (coalesce(a, b))</c> is consumed
    /// whole. A group left unclosed by malformed SQL consumes to the end of
    /// the stream rather than looping: the statement is refused either way,
    /// and a tokenizer that ran out of input has already emitted its own
    /// sentinel. Returns <paramref name="pos"/> unchanged when it does not
    /// point at an opening paren, so a caller cannot lose a token to it.
    /// </summary>
    private static int SkipBalancedParens(List<Token> tokens, int pos)
    {
        if (pos >= tokens.Count || tokens[pos].Type != TokenType.Symbol || tokens[pos].Value != "(")
            return pos;

        int depth = 0;
        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            var t = tokens[pos];
            if (t.Type == TokenType.Symbol && t.Value == "(")
                depth++;
            else if (t.Type == TokenType.Symbol && t.Value == ")")
            {
                depth--;
                if (depth == 0)
                    return pos + 1;
            }

            pos++;
        }
        return pos;
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
        col.ReferencedColumns.AddRange(CollectExpressionColumnRefs(run, 0, exprEnd));
        target.Add(col);
        return pos;
    }

    /// <summary>
    /// Walks an expression's token run (from <paramref name="start"/> to
    /// <paramref name="end"/>, exclusive) and returns every column reference it
    /// touches, for migration-impact dependency tracking. An Identifier token
    /// is a column reference unless it is a function-call head (immediately
    /// followed by "(" — e.g. <c>CONCAT</c>, <c>ROUND</c>, and any built-in
    /// aggregate not already tokenized as a keyword) or a CAST-style type name
    /// (immediately preceded by the keyword "AS", as in
    /// <c>CAST(a.x AS INT)</c> — that identifier names a type, not a column).
    /// Used for both SELECT/RETURNING expression projection items (see
    /// <see cref="ColumnRef.ReferencedColumns"/>) and ORDER BY expression items
    /// (see <see cref="OrderByRef.ReferencedColumns"/>): without this, an
    /// expression item like <c>count(email)</c>, <c>max(a.x)</c>,
    /// <c>concat(a.first, a.last)</c>, or <c>a.price * b.qty</c> — whether
    /// projected or sorted on — recorded NO column dependency at all (only the
    /// narrow SUM/AVG-single-bare-column shape did, via
    /// <see cref="ColumnRef.AggregateArgColumnName"/>), so a migration that
    /// removed or changed a column referenced only inside such an expression
    /// produced a false SAFE verdict from
    /// <c>JauntyQ.Analysis.Impact.ReferencedObjects</c>/<c>ImpactClassifier</c>.
    /// </summary>
    private static List<(string TableAlias, string ColumnName)> CollectExpressionColumnRefs(
        List<Token> tokens, int start, int end)
    {
        var result = new List<(string, string)>();
        for (int i = start; i < end; i++)
        {
            if (tokens[i].Type != TokenType.Identifier)
                continue;

            bool isFunctionHead = i + 1 < end &&
                tokens[i + 1].Type == TokenType.Symbol && tokens[i + 1].Value == "(";
            bool isCastTypeName = i > start &&
                tokens[i - 1].Type == TokenType.Keyword && tokens[i - 1].Value == "AS";
            if (isFunctionHead || isCastTypeName)
                continue;

            result.Add(SplitQualifiedName(tokens[i].Value));
        }
        return result;
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
        // The window form "count(...) OVER (...)" is the same claim: a window
        // COUNT returns the same type as the aggregate one, and still never
        // returns NULL (an empty frame counts 0, it does not yield NULL). The
        // OVER clause must consume the rest of the run for the same reason the
        // plain call must — "count(*) OVER () + 1" is not bigint-shaped.
        // Deliberately COUNT-only. ROW_NUMBER/RANK/DENSE_RANK are integral and
        // never-NULL too, but they do NOT share COUNT's dialect profile and so
        // cannot share its bigint claim: measured on SQL Server 2022,
        // COUNT(*) OVER() comes back int while ROW_NUMBER/RANK/DENSE_RANK come
        // back bigint, so ProjectionBuilder's bigint->int downgrade (right for
        // COUNT there) would be wrong for them. MySQL returns BIGINT UNSIGNED,
        // whose top of range does not fit long at all. They stay unresolved and
        // still require -- @type until there is a per-dialect width table.
        if (hi - lo >= 2 &&
            IsCountHead(run[lo]) &&
            run[lo + 1].Type == TokenType.Symbol && run[lo + 1].Value == "(" &&
            ConsumesRun(run, FindMatchingClose(run, lo + 1, hi), hi))
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

    /// <summary>
    /// True when a call whose arguments close at <paramref name="callClose"/>
    /// consumes the whole expression run ending at <paramref name="hi"/> —
    /// either directly, or through a trailing <c>OVER ( ... )</c> window clause
    /// that itself closes at the end. Returns false for a malformed
    /// <paramref name="callClose"/> of -1, and for <c>OVER w</c> (a named
    /// window reference), whose definition lives in a WINDOW clause this
    /// parser does not model.
    /// </summary>
    private static bool ConsumesRun(List<Token> run, int callClose, int hi)
    {
        if (callClose < 0)
            return false;
        if (callClose == hi - 1)
            return true;

        int over = callClose + 1;
        if (over + 1 >= hi)
            return false;
        if (!IsOverKeyword(run[over]))
            return false;
        if (run[over + 1].Type != TokenType.Symbol || run[over + 1].Value != "(")
            return false;

        return FindMatchingClose(run, over + 1, hi) == hi - 1;
    }

    /// <summary>OVER is not in the tokenizer's keyword set, so it arrives as an Identifier; accept either.</summary>
    private static bool IsOverKeyword(Token t) =>
        (t.Type == TokenType.Keyword || t.Type == TokenType.Identifier) &&
        string.Equals(t.Value, "OVER", StringComparison.OrdinalIgnoreCase);

    private static bool IsCountHead(Token t) =>
        (t.Type == TokenType.Keyword && t.Value == "COUNT") ||
        (t.Type == TokenType.Identifier && string.Equals(t.Value, "count", StringComparison.OrdinalIgnoreCase));

    private static bool IsAggregateHead(Token t, string a, string b) =>
        (t.Type == TokenType.Keyword && (t.Value == a || t.Value == b)) ||
        (t.Type == TokenType.Identifier &&
            (string.Equals(t.Value, a, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(t.Value, b, StringComparison.OrdinalIgnoreCase)));

}
