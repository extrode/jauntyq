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
            model.TargetTable = tokens[pos].Value;
            model.Tables.Add(new TableRef { TableName = tokens[pos].Value, Alias = string.Empty });
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
                ParseProjectionList(tokens, i + 1, model, model.Returning);
                return;
            }
        }
    }

    private static void DetectUnsupportedConstructs(List<Token> tokens, QueryModel model)
    {
        bool seenSelect = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Type == TokenType.Keyword)
            {
                switch (token.Value)
                {
                    case "SELECT":
                        // A SELECT that is the argument of EXISTS(...) is an
                        // expression-projection subquery and is supported; only
                        // other nested SELECTs are rejected as free-standing
                        // subqueries.
                        if (seenSelect && !IsExistsSubquery(tokens, i))
                        {
                            if (!model.UnsupportedConstructs.Contains("SUBQUERY"))
                                model.UnsupportedConstructs.Add("SUBQUERY");
                        }
                        seenSelect = true;
                        break;
                    case "UNION":
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
    /// an EXISTS (...) — i.e. the two preceding non-skipped tokens are the
    /// EXISTS keyword and an opening paren.
    /// </summary>
    private static bool IsExistsSubquery(List<Token> tokens, int selectIndex)
    {
        if (selectIndex < 2)
            return false;
        return tokens[selectIndex - 1].Type == TokenType.Symbol && tokens[selectIndex - 1].Value == "(" &&
               tokens[selectIndex - 2].Type == TokenType.Keyword && tokens[selectIndex - 2].Value == "EXISTS";
    }
}
