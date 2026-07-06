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
                        if (seenSelect)
                        {
                            // Subquery (SELECT inside SELECT)
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

        // CTE detection: WITH keyword at position 0 (before SELECT)
        if (tokens.Count > 0 && tokens[0].Type == TokenType.Keyword && tokens[0].Value == "WITH")
        {
            if (!model.UnsupportedConstructs.Contains("CTE"))
                model.UnsupportedConstructs.Add("CTE");
        }
    }
}
