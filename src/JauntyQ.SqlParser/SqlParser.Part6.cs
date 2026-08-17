using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.SqlParser;

public static partial class SqlParser
{
    /// <summary>
    /// DELETE FROM Table WHERE ... or DELETE Table WHERE ...
    /// </summary>
    private static void ParseDelete(List<Token> tokens, int pos, QueryModel model)
    {
        // Skip optional FROM
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "FROM")
            pos++;

        // Table name
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            string tableName = StripQualifier(tokens[pos].Value);
            model.TargetTable = tableName;
            model.Tables.Add(new TableRef { TableName = tableName, Alias = string.Empty });
        }

        // Parameter bindings handled by ExtractParameterBindings (col = @param in WHERE)
        ExtractParameterBindings(tokens, model);

        ExtractReturning(tokens, model);
    }

    /// <summary>
    /// Scans the token stream for a top-level RETURNING clause and parses its
    /// projection list (plain columns + Feature-A expressions) into
    /// <see cref="QueryModel.Returning"/>. Sets HasReturning even when the list
    /// is empty. Depth-tracked so a RETURNING keyword nested inside a subquery
    /// or parens is ignored.
    /// </summary>
    private static void ExtractReturning(List<Token> tokens, QueryModel model)
    {
        int depth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type == TokenType.Symbol && t.Value == "(") { depth++; continue; }
            if (t.Type == TokenType.Symbol && t.Value == ")") { depth--; continue; }
            if (depth == 0 && t.Type == TokenType.Keyword && t.Value == "RETURNING")
            {
                model.HasReturning = true;
                // Slice the projection tokens up to (but excluding) a top-level
                // statement terminator ';', appending an End sentinel so the
                // shared projection parser treats ';' exactly like end-of-input.
                // Without this a trailing ';' is swallowed into the last item,
                // demoting a plain column to an alias-less expression (JNT3004).
                var projTokens = new List<Token>();
                int innerDepth = 0;
                for (int j = i + 1; j < tokens.Count; j++)
                {
                    var pt = tokens[j];
                    if (pt.Type == TokenType.End)
                        break;
                    if (pt.Type == TokenType.Symbol && pt.Value == "(") innerDepth++;
                    else if (pt.Type == TokenType.Symbol && pt.Value == ")") innerDepth--;
                    else if (innerDepth == 0 && pt.Type == TokenType.Symbol && pt.Value == ";")
                        break;
                    projTokens.Add(pt);
                }
                projTokens.Add(new Token(TokenType.End, string.Empty));
                ParseProjectionList(projTokens, 0, model, model.Returning);
                return;
            }
        }
    }

    /// <summary>
    /// Records the MULTI_STATEMENT construct when a top-level <c>;</c> is
    /// followed by more content, i.e. the file holds a second statement.
    /// <para>
    /// This is a correctness guard, not a style rule. <see cref="Parse"/>'s
    /// main loop walks the WHOLE token list and dispatches on every
    /// SELECT/FROM/JOIN/ORDER keyword it meets, so a second SELECT statement's
    /// tables and projection items are appended to the FIRST statement's
    /// model: one merged <c>QueryModel</c> with both statements' columns, and
    /// a mapper generated against ordinals that no single result set has. The
    /// INSERT/UPDATE/DELETE arms are worse in the other direction -- they
    /// return early, so a second statement is dropped entirely and never
    /// emitted. Both outcomes were silent before this check.
    /// </para>
    /// Trailing terminators are fine: "SELECT 1;" and even "SELECT 1;;" have
    /// nothing but the End sentinel after the ';'. Comments never reach here
    /// (SqlTokenizer strips them), and a ';' inside a string literal is a
    /// Literal token, not a Symbol, so neither can trip this.
    /// </summary>
    private static void DetectMultipleStatements(List<Token> tokens, QueryModel model)
    {
        int depth = 0;
        bool terminated = false;
        foreach (var t in tokens)
        {
            if (t.Type == TokenType.Symbol && t.Value == "(") { depth++; continue; }
            if (t.Type == TokenType.Symbol && t.Value == ")") { depth--; continue; }

            // A ';' below depth 0 is malformed rather than a terminator; leave
            // it to the ordinary parsers rather than claiming a new statement.
            if (depth == 0 && t.Type == TokenType.Symbol && t.Value == ";")
            {
                terminated = true;
                continue;
            }

            if (!terminated)
                continue;
            if (t.Type == TokenType.End || t.Type == TokenType.Unterminated)
                continue;

            if (!model.UnsupportedConstructs.Contains("MULTI_STATEMENT"))
                model.UnsupportedConstructs.Add("MULTI_STATEMENT");
            return;
        }
    }

    private static void DetectUnsupportedConstructs(List<Token> tokens, QueryModel model)
    {
        // Lift the supported WHERE-clause predicate subqueries out of the token
        // stream FIRST. This both (a) parses+attaches them as their own scopes
        // and (b) removes their tokens so the enclosing FROM/SELECT/JOIN loop
        // and the nested-SELECT check below never see them. Whatever nested
        // SELECT remains after this is a genuinely unsupported subquery.
        ExtractPredicateSubqueries(tokens, model);

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Type == TokenType.Keyword)
            {
                switch (token.Value)
                {
                    case "SELECT":
                        // A SELECT immediately preceded by an opening paren is
                        // a nested, parenthesized subquery -- a scalar
                        // subquery in a projection/SET/VALUES expression, or a
                        // derived table in FROM. The enclosing statement's own
                        // top-level SELECT (a SELECT statement) and an
                        // INSERT...SELECT's row source (ParseInsertSelect) are
                        // never paren-preceded, so this correctly leaves both
                        // alone without needing to track statement kind here.
                        // WHERE-clause IN/EXISTS predicate subqueries were
                        // already lifted (tokens removed) above; a
                        // projection-list EXISTS(...) is excluded explicitly,
                        // since it is a supported expression projection
                        // (Feature A), not a free-standing subquery. Previously
                        // this used a "seenSelect" counter that assumed the
                        // first SELECT keyword in the stream is always the
                        // statement's own -- true for SELECT statements, but
                        // wrong for UPDATE/INSERT/DELETE, where the first (and
                        // possibly only) SELECT keyword in the whole statement
                        // IS the illegal subquery: it slipped through
                        // undetected, e.g. `UPDATE t SET x = (SELECT ...)`.
                        if (i > 0 && tokens[i - 1].Type == TokenType.Symbol && tokens[i - 1].Value == "(" &&
                            !IsExistsSubquery(tokens, i))
                        {
                            if (!model.UnsupportedConstructs.Contains("SUBQUERY"))
                                model.UnsupportedConstructs.Add("SUBQUERY");
                        }
                        break;
                    // The three set operations share one construct because they
                    // share one failure: only the first branch is modeled, so
                    // the generated mapper describes a column shape the query
                    // may not return. INTERSECT and EXCEPT were worse than
                    // UNION before this, not better -- neither was a tokenizer
                    // keyword, so "FROM a INTERSECT SELECT ..." read INTERSECT
                    // as a's ALIAS and the statement was silently mis-modeled
                    // with no construct recorded at all.
                    case "UNION":
                    case "INTERSECT":
                    case "EXCEPT":
                        if (!model.UnsupportedConstructs.Contains("UNION"))
                            model.UnsupportedConstructs.Add("UNION");
                        break;
                }
            }
        }

        // CTE (leading WITH) is parsed by ParseWith before this runs, so a
        // WITH file never reaches here; no CTE construct is emitted.
    }

    /// <summary>
    /// True when the SELECT at <paramref name="selectIndex"/> is the argument of
    /// an EXISTS (...) — the two preceding tokens are the EXISTS keyword and an
    /// opening paren. Such an EXISTS in the projection list is an expression
    /// projection (Feature A), never a standalone subquery.
    /// </summary>
    private static bool IsExistsSubquery(List<Token> tokens, int selectIndex)
    {
        if (selectIndex < 2)
            return false;
        return tokens[selectIndex - 1].Type == TokenType.Symbol && tokens[selectIndex - 1].Value == "(" &&
               tokens[selectIndex - 2].Type == TokenType.Keyword && tokens[selectIndex - 2].Value == "EXISTS";
    }

    /// <summary>
    /// Scans <paramref name="tokens"/> for top-level WHERE-clause predicate
    /// subqueries — <c>&lt;column&gt; [NOT] IN ( &lt;SELECT&gt; )</c> and
    /// <c>[NOT] EXISTS ( &lt;SELECT&gt; )</c> — and lifts each out of the stream:
    /// the inner SELECT (paren-delimited) is parsed recursively into its own
    /// <see cref="QueryModel"/>, attached to <see cref="QueryModel.Subqueries"/>,
    /// and its parameter bindings are merged onto the enclosing model so
    /// <c>col = @param</c> inside the subquery still resolves a type. The matched
    /// span (opening paren through matching close paren, plus the leading IN/
    /// EXISTS/NOT keywords) is then removed so the ordinary parsers never treat
    /// the subquery's tables/columns as the enclosing statement's own.
    /// Only the paren group opening with SELECT is lifted; an <c>IN (1,2,3)</c>
    /// value list is left untouched.
    /// </summary>
    private static void ExtractPredicateSubqueries(List<Token> tokens, QueryModel model)
    {
        // Only the WHERE region carries predicate subqueries. A projection-list
        // EXISTS(...) (Feature A expression) sits before FROM and must never be
        // lifted. Start scanning after the first top-level WHERE keyword; if the
        // statement has none, there is nothing to lift.
        int whereStart = -1;
        int scanDepth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            var tk = tokens[i];
            if (tk.Type == TokenType.Symbol && tk.Value == "(") { scanDepth++; continue; }
            if (tk.Type == TokenType.Symbol && tk.Value == ")") { scanDepth--; continue; }
            if (scanDepth == 0 && tk.Type == TokenType.Keyword && tk.Value == "WHERE")
            {
                whereStart = i + 1;
                break;
            }
        }
        if (whereStart < 0)
            return;

        for (int i = whereStart; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type != TokenType.Keyword)
                continue;

            // A supported subquery is introduced by either an IN or an EXISTS
            // keyword immediately followed by '(' SELECT. The span to remove
            // starts at the earliest introducer token so a leading NOT (and, for
            // IN, the preceding column reference stays but its predicate is
            // neutralised) is consumed too.
            int spanStart;
            SubqueryKind kind;

            if (t.Value == "IN" &&
                i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Symbol && tokens[i + 1].Value == "(" &&
                i + 2 < tokens.Count && tokens[i + 2].Type == TokenType.Keyword && tokens[i + 2].Value == "SELECT")
            {
                kind = SubqueryKind.In;
                // Include a preceding NOT so "NOT IN" is fully removed.
                spanStart = (i - 1 >= 0 && tokens[i - 1].Type == TokenType.Keyword && tokens[i - 1].Value == "NOT")
                    ? i - 1 : i;
            }
            else if (t.Value == "EXISTS" &&
                     i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Symbol && tokens[i + 1].Value == "(" &&
                     i + 2 < tokens.Count && tokens[i + 2].Type == TokenType.Keyword && tokens[i + 2].Value == "SELECT")
            {
                kind = SubqueryKind.Exists;
                spanStart = (i - 1 >= 0 && tokens[i - 1].Type == TokenType.Keyword && tokens[i - 1].Value == "NOT")
                    ? i - 1 : i;
            }
            else
            {
                continue;
            }

            int open = i + 1;
            int close = FindMatchingParen(tokens, open, tokens.Count);
            if (close < 0)
                continue; // malformed; leave for the generic detector

            // Slice the inner statement (between the parens) and parse it with a
            // fresh End sentinel so the sub-parser terminates cleanly. This
            // recursion also lifts any subqueries nested inside the inner SELECT.
            var innerTokens = new List<Token>();
            for (int j = open + 1; j < close; j++)
                innerTokens.Add(tokens[j]);
            innerTokens.Add(new Token(TokenType.End, string.Empty));

            var body = Parse(innerTokens, model.Name + "_sub" + model.Subqueries.Count);
            model.Subqueries.Add(new SubqueryRef { Kind = kind, Body = body });

            // Merge parameter bindings resolved inside the subquery so an outer
            // @param used only there still gets a type. The outer scan already
            // registered the ParameterRef by name; carry the binding if unbound.
            foreach (var p in body.Parameters)
            {
                var existing = model.Parameters.Find(x => x.Name == p.Name);
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

            // Remove [NOT] IN/EXISTS ... ( ... ) from the stream and rescan from
            // the removal point (indices shifted).
            tokens.RemoveRange(spanStart, close - spanStart + 1);
            i = spanStart - 1;
        }
    }
}
