using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.SqlParser;

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
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == "(") { boundaryDepth++; continue; }
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == ")") { boundaryDepth--; continue; }
            if (tokens[i].Type != TokenType.Keyword || boundaryDepth != 0)
                continue;
            if (start < 0 && tokens[i].Value == "WHERE")
            {
                start = i + 1;
                continue;
            }
            if (start >= 0 && tokens[i].Value is "GROUP" or "ORDER" or "HAVING")
            {
                end = i;
                break;
            }
        }
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
                if (close < 0)
                    continue;

                bool comparedAfter = close + 1 < end && tokens[close + 1].Type == TokenType.Symbol &&
                                     ComparisonOperators.Contains(tokens[close + 1].Value);
                bool comparedBefore = i - 1 >= start && tokens[i - 1].Type == TokenType.Symbol &&
                                      ComparisonOperators.Contains(tokens[i - 1].Value);
                if (!comparedAfter && !comparedBefore)
                {
                    i = close;
                    continue;
                }

                // first identifier inside the call is the wrapped column;
                // no identifier means the function is on the value side
                for (int j = i + 2; j < close; j++)
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
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == "(") { boundaryDepth++; continue; }
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == ")") { boundaryDepth--; continue; }
            if (tokens[i].Type != TokenType.Keyword || boundaryDepth != 0)
                continue;
            if (start < 0 && tokens[i].Value == "WHERE")
            {
                start = i + 1;
                continue;
            }
            if (start >= 0 && tokens[i].Value is "GROUP" or "ORDER" or "HAVING")
            {
                end = i;
                break;
            }
        }
        if (start < 0)
            return;

        bool hasTopLevelOr = false;
        int scanDepth = 0;
        for (int i = start; i < end; i++)
        {
            var t = tokens[i];
            if (t.Type == TokenType.Symbol && t.Value == "(") { scanDepth++; continue; }
            if (t.Type == TokenType.Symbol && t.Value == ")") { scanDepth--; continue; }
            if (scanDepth == 0 && t.Type == TokenType.Keyword && t.Value == "OR")
            {
                hasTopLevelOr = true;
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
