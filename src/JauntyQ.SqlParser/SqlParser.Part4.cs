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
        // Bound the WHERE region: from WHERE to GROUP/ORDER/HAVING or end.
        int start = -1;
        int end = tokens.Count;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.Keyword)
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
                }
            }
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
