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
                if (!model.Parameters.Any(p => p.Name == token.Value))
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
        // outermost parens enclose the whole run.
        int lo = 0, hi = count;
        while (hi - lo >= 2 &&
               run[lo].Type == TokenType.Symbol && run[lo].Value == "(" &&
               run[hi - 1].Type == TokenType.Symbol && run[hi - 1].Value == ")" &&
               EnclosesWholeRun(run, lo, hi))
        {
            lo++;
            hi--;
        }

        // A top-level comparison or IS [NOT] NULL yields boolean — checked
        // before count/exists heads so "count(*) > 0" infers boolean, not bigint.
        int depth = 0;
        for (int i = lo; i < hi; i++)
        {
            var t = run[i];
            if (t.Type == TokenType.Symbol && t.Value == "(") { depth++; continue; }
            if (t.Type == TokenType.Symbol && t.Value == ")") { depth--; continue; }
            if (depth != 0)
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

        // count(...) as the (unwrapped) head -> bigint NOT NULL.
        if (hi - lo >= 2 &&
            IsCountHead(run[lo]) &&
            run[lo + 1].Type == TokenType.Symbol && run[lo + 1].Value == "(")
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

    private static bool IsCountHead(Token t) =>
        (t.Type == TokenType.Keyword && t.Value == "COUNT") ||
        (t.Type == TokenType.Identifier && string.Equals(t.Value, "count", StringComparison.OrdinalIgnoreCase));

}
