using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;

namespace Extrode.JauntyQ.SqlParser;

public static partial class SqlParser
{
    /// <summary>
    /// Captures WHERE-clause patterns that defeat indexes: a function call
    /// wrapping a column that is compared to something (non-sargable), and
    /// LIKE with a leading wildcard. Token-level analysis, WHERE region only:
    /// a function in the SELECT list is fine.
    /// </summary>
    private static void ExtractPerfHints(List<Token> tokens, QueryModel model)
    {
        // Bound the WHERE region: from WHERE to GROUP/ORDER/HAVING or end. Depth-
        // gated (only a top-level, depth-0 keyword counts) so a projection-list
        // EXISTS(...) subquery -- a supported Feature A expression whose own
        // inner SELECT is deliberately left in the token stream (it is neither
        // lifted by ExtractPredicateSubqueries, which only handles WHERE-clause
        // IN/EXISTS predicates, nor flagged unsupported by DetectUnsupportedConstructs,
        // which explicitly excludes it) -- can't have its own nested WHERE/GROUP/
        // ORDER/HAVING keywords mistaken for the enclosing statement's own. Mirrors
        // the same scanDepth convention already used by ExtractPredicateSubqueries.
        int start = -1;
        int end = tokens.Count;
        int boundaryDepth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            // Stryker disable once Statement : removing this continue is a no-op -- the very next line's own "!= Keyword" check is already true for a Symbol token, so it continues regardless of boundaryDepth
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == "(") { boundaryDepth++; continue; }
            // Stryker disable once Statement : same argument as the "(" branch above
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == ")") { boundaryDepth--; continue; }
            if (tokens[i].Type != TokenType.Keyword || boundaryDepth != 0)
                continue;
            // Stryker disable once Equality : start is only ever -1 (initial) or i+1 for some i>=0 (i.e. >=1) -- it can never equal exactly 0, so start<0 and start<=0 always agree
            if (start < 0 && tokens[i].Value == "WHERE")
            {
                start = i + 1;
                // Stryker disable once Statement : removing this continue is a no-op -- the very next check below tests Value against "GROUP"/"ORDER"/"HAVING", which "WHERE" can never match, so it falls through to the same loop-end either way
                continue;
            }
            // Stryker disable once Equality : same never-exactly-0 argument as start<0 above applies symmetrically to start>=0 vs start>0
            if (start >= 0 && tokens[i].Value is "GROUP" or "ORDER" or "HAVING")
            {
                end = i;
                break;
            }
        }
        // Stryker disable once Equality : same never-exactly-0 argument as the loop's own start<0 check
        if (start < 0)
            return;

        for (int i = start; i < end; i++)
        {
            // fn( ... column ... ) <op> ...   or   ... <op> fn( ... column ... )
            bool isFunctionHead =
                (tokens[i].Type == TokenType.Identifier ||
                 (tokens[i].Type == TokenType.Keyword && FunctionKeywords.Contains(tokens[i].Value))) &&
                i + 1 < end && tokens[i + 1].Type == TokenType.Symbol && tokens[i + 1].Value == "(";

            if (isFunctionHead)
            {
                int close = FindMatchingParen(tokens, i + 1, end);
                // Stryker disable once Equality : close is only ever -1 (not found) or a matched index >= openIndex (>=1, since openIndex = i+1 for i>=start>=0) -- it can never equal exactly 0, so close<0 and close<=0 always agree
                if (close < 0)
                    continue;

                bool comparedAfter = close + 1 < end && tokens[close + 1].Type == TokenType.Symbol &&
                                     ComparisonOperators.Contains(tokens[close + 1].Value);
                // No i - 1 >= start bound: start is always the index just past
                // the WHERE keyword, so at i == start tokens[i - 1] is that
                // keyword -- in range, and never a comparison Symbol.
                bool comparedBefore = tokens[i - 1].Type == TokenType.Symbol &&
                                      ComparisonOperators.Contains(tokens[i - 1].Value);
                // Stryker disable once Statement : removing this continue is a no-op -- i is already set to close on the line above, so the identifier scan below (for j = i+2; j < close; ...) starts past its own upper bound and never executes either way
                if (!comparedAfter && !comparedBefore)
                {
                    i = close;
                    continue;
                }

                // first identifier inside the call is the wrapped column;
                // no identifier means the function is on the value side.
                // != rather than <: close >= i + 2 always (tokens[i + 1] is the
                // call's own "(" and close its match), and tokens[close] is ")"
                // -- never an Identifier -- so j < close and j <= close would be
                // indistinguishable.
                for (int j = i + 2; j != close; j++)
                {
                    if (tokens[j].Type == TokenType.Identifier)
                    {
                        var (tableAlias, columnName) = SplitQualifiedName(tokens[j].Value);
                        model.PerfHints.Add(new PerfHint
                        {
                            Kind = PerfHintKind.FunctionOnColumn,
                            FunctionName = tokens[i].Value,
                            BoundTableAlias = tableAlias,
                            BoundColumnName = columnName
                        });
                        break;
                    }
                }
                i = close;
                // Stryker disable once Statement : removing this continue is a no-op -- i is already set to close (a Symbol ")" token), and both the LIKE check and the column=column check just below require tokens[i].Type == Identifier, which a ")" token never is
                continue;
            }

            // column [NOT] LIKE '<pattern starting with % or _>'
            if (tokens[i].Type == TokenType.Identifier)
            {
                int k = i + 1;
                if (k < end && tokens[k].Type == TokenType.Keyword && tokens[k].Value == "NOT")
                    k++;
                if (k < end && tokens[k].Type == TokenType.Keyword && tokens[k].Value == "LIKE" &&
                    k + 1 < end && tokens[k + 1].Type == TokenType.Literal &&
                    tokens[k + 1].Value.Length > 0 &&
                    (tokens[k + 1].Value[0] == '%' || tokens[k + 1].Value[0] == '_'))
                {
                    var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                    model.PerfHints.Add(new PerfHint
                    {
                        Kind = PerfHintKind.LeadingWildcardLike,
                        BoundTableAlias = tableAlias,
                        BoundColumnName = columnName,
                        Detail = tokens[k + 1].Value
                    });
                    // Stryker disable once Statement : removing this continue is a no-op -- tokens[i + 1] was just matched above as Keyword "NOT" or "LIKE", so it can never simultaneously be the Symbol "=" the column=column check below requires, and that check is the only code reachable before this loop iteration's natural end
                    continue;
                }

                // column = column : an implicit join / correlated back-reference
                // (e.g. a predicate subquery's `atg.article_id = a.id`, where `a`
                // is the enclosing statement's alias). Explicit JOIN...ON columns
                // never reach here — that clause sits before WHERE, outside this
                // bounded region — so this only catches WHERE-clause shapes an
                // explicit join can't already surface.
                if (i + 2 < end &&
                    tokens[i + 1].Type == TokenType.Symbol && tokens[i + 1].Value == "=" &&
                    tokens[i + 2].Type == TokenType.Identifier)
                {
                    var (leftAlias, leftColumn) = SplitQualifiedName(tokens[i].Value);
                    var (rightAlias, rightColumn) = SplitQualifiedName(tokens[i + 2].Value);
                    model.PerfHints.Add(new PerfHint
                    {
                        Kind = PerfHintKind.ColumnComparedToColumn,
                        BoundTableAlias = leftAlias,
                        BoundColumnName = leftColumn
                    });
                    model.PerfHints.Add(new PerfHint
                    {
                        Kind = PerfHintKind.ColumnComparedToColumn,
                        BoundTableAlias = rightAlias,
                        BoundColumnName = rightColumn
                    });
                    i += 2;
                    // Stryker disable once Statement : removing this continue is a no-op -- it is the last statement in the loop body, so control reaches the same for-loop increment either way
                    continue;
                }
            }
        }
    }

    /// <summary>
    /// Splits the WHERE region into top-level AND-conjuncts and records each as
    /// a <see cref="PredicateAtom"/> of classified tokens. Read-only over
    /// <paramref name="tokens"/>; the stream it sees is the one every other
    /// extractor sees.
    ///
    /// Runs BEFORE <c>ExtractPredicateSubqueries</c>, deliberately: that pass
    /// removes the whole <c>[NOT] IN (SELECT ...)</c> / <c>[NOT] EXISTS
    /// (SELECT ...)</c> span including its leading NOT, and <see cref="SubqueryRef"/>
    /// has no negation flag — so after it runs, EXISTS and NOT EXISTS are
    /// indistinguishable. Reading the region first is the only place the NOT is
    /// still there to record.
    ///
    /// Two conservatisms, both erring toward one large atom rather than several
    /// wrong ones:
    /// <list type="bullet">
    /// <item>a depth-0 OR anywhere in the region makes the ENTIRE region a single
    /// atom — a disjunction does not decompose into conjuncts, and reasoning
    /// about the boolean algebra is out of scope;</item>
    /// <item>BETWEEN's own AND is not a splitter, so <c>x BETWEEN @lo AND @hi</c>
    /// stays one atom rather than becoming "x BETWEEN @lo" and a stray "@hi".</item>
    /// </list>
    /// </summary>
    private static void ExtractPredicateAtoms(List<Token> tokens, QueryModel model)
    {
        // Identical bounding to ExtractPerfHints: WHERE to GROUP/ORDER/HAVING or
        // end, depth-gated so a projection-list EXISTS(...) subquery's own
        // clause keywords are never mistaken for this statement's.
        int start = -1;
        int end = tokens.Count;
        int boundaryDepth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            // Stryker disable once Statement : removing this continue is a no-op -- the very next line's own "!= Keyword" check is already true for a Symbol token, so it continues regardless of boundaryDepth
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == "(") { boundaryDepth++; continue; }
            // Stryker disable once Statement : same argument as the "(" branch above
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == ")") { boundaryDepth--; continue; }
            if (tokens[i].Type != TokenType.Keyword || boundaryDepth != 0)
                continue;
            // Stryker disable once Equality : start is only ever -1 (initial) or i+1 for some i>=0 (i.e. >=1) -- it can never equal exactly 0, so start<0 and start<=0 always agree
            if (start < 0 && tokens[i].Value == "WHERE")
            {
                start = i + 1;
                // Stryker disable once Statement : removing this continue is a no-op -- the very next check below tests Value against "GROUP"/"ORDER"/"HAVING", which "WHERE" can never match, so it falls through to the same loop-end either way
                continue;
            }
            // Stryker disable once Equality : same never-exactly-0 argument as start<0 above applies symmetrically to start>=0 vs start>0
            if (start >= 0 && tokens[i].Value is "GROUP" or "ORDER" or "HAVING")
            {
                end = i;
                break;
            }
        }
        // Stryker disable once Equality : same never-exactly-0 argument as the loop's own start<0 check
        if (start < 0)
            return;

        bool hasTopLevelOr = false;
        int scanDepth = 0;
        for (int i = start; i < end; i++)
        {
            var t = tokens[i];
            if (t.Type == TokenType.Symbol && t.Value == "(")
                scanDepth++;
            else if (t.Type == TokenType.Symbol && t.Value == ")")
                scanDepth--;
            else if (scanDepth == 0 && t.Type == TokenType.Keyword && t.Value == "OR")
            {
                hasTopLevelOr = true;
                // Stryker disable once Statement : hasTopLevelOr is this loop's only output and it is already true here; removing this break just lets the loop keep updating scanDepth (unused after the loop) until it naturally ends, with no other observable effect
                break;
            }
        }

        var current = new PredicateAtom();
        int depth = 0;
        bool inBetween = false;
        for (int i = start; i < end; i++)
        {
            var t = tokens[i];

            // The End sentinel closes the stream; it carries an empty value and
            // would otherwise land in the last atom as a blank term, so two
            // otherwise-identical clauses would differ by whichever one had the
            // predicate in final position.
            if (t.Type == TokenType.End)
                break;

            if (t.Type == TokenType.Symbol && t.Value == "(")
                depth++;
            else if (t.Type == TokenType.Symbol && t.Value == ")")
                depth--;
            else if (depth == 0 && t.Type == TokenType.Keyword && t.Value == "BETWEEN")
                inBetween = true;
            else if (depth == 0 && t.Type == TokenType.Keyword && t.Value == "AND")
            {
                if (inBetween)
                {
                    // BETWEEN's second operand: part of this atom, not a split.
                    inBetween = false;
                }
                else if (!hasTopLevelOr)
                {
                    if (current.Terms.Count > 0)
                        model.PredicateAtoms.Add(current);
                    current = new PredicateAtom();
                    continue;
                }
            }

            current.Terms.Add(ClassifyAtomTerm(t));
        }
        if (current.Terms.Count > 0)
            model.PredicateAtoms.Add(current);
    }

    /// <summary>
    /// Classifies one WHERE-region token for <see cref="ExtractPredicateAtoms"/>.
    /// Identifiers keep their qualifier separately so the comparison can resolve
    /// <c>b.user_id</c> and a bare <c>user_id</c> to the same column; everything
    /// else is kept as written, with keywords upper-cased so two files that
    /// disagree only on casing still compare equal.
    /// </summary>
    private static AtomTerm ClassifyAtomTerm(Token token)
    {
        switch (token.Type)
        {
            case TokenType.Identifier:
                var (tableAlias, columnName) = SplitQualifiedName(token.Value);
                return new AtomTerm { Kind = AtomTermKind.Column, Text = columnName, TableAlias = tableAlias };

            case TokenType.Parameter:
                // The tokenizer drops the leading '@'; put it back so the
                // canonical form reads like the SQL the author wrote when it is
                // quoted in a diagnostic message.
                // Stryker disable once Conditional : the tokenizer's Parameter branch (SqlTokenizer) always skips the '@' character before recording the substring, so token.Value never starts with '@' for a real Parameter token -- the true branch is unreachable and forcing the false branch always is indistinguishable from any input
                return new AtomTerm
                {
                    Kind = AtomTermKind.Parameter,
                    Text = token.Value.StartsWith("@") ? token.Value : "@" + token.Value,
                    TableAlias = string.Empty
                };

            case TokenType.Literal:
            case TokenType.Number:
                return new AtomTerm { Kind = AtomTermKind.Literal, Text = token.Value, TableAlias = string.Empty };

            case TokenType.Symbol:
                return new AtomTerm { Kind = AtomTermKind.Operator, Text = token.Value, TableAlias = string.Empty };

            default:
                // The tokenizer already upper-cases keyword values, so for the
                // keywords this branch mostly sees, ToUpperInvariant is a
                // no-op -- verified by perturbation: removing it reddens
                // nothing. It stays for the degenerate token types that also
                // land here (Unterminated, TooLarge, Unknown), whose text is
                // whatever the input held.
                return new AtomTerm
                {
                    Kind = AtomTermKind.Keyword,
                    Text = token.Value.ToUpperInvariant(),
                    TableAlias = string.Empty
                };
        }
    }

    private static int FindMatchingParen(List<Token> tokens, int openIndex, int end)
    {
        int depth = 0;
        for (int i = openIndex; i < end; i++)
        {
            if (tokens[i].Type != TokenType.Symbol)
                continue;
            if (tokens[i].Value == "(")
                depth++;
            else if (tokens[i].Value == ")" && --depth == 0)
                return i;
        }
        return -1;
    }

}
