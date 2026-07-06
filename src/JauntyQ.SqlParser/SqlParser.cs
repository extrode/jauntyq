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
    {
        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            var token = tokens[pos];

            // Stop at FROM, WHERE, or other major clause keywords
            if (token.Type == TokenType.Keyword && IsClauseKeyword(token.Value))
                break;

            // Star select
            if (token.Type == TokenType.Symbol && token.Value == "*")
            {
                model.Columns.Add(new ColumnRef
                {
                    TableAlias = string.Empty,
                    ColumnName = "*",
                    OutputAlias = string.Empty
                });
                pos++;
                continue;
            }

            // Skip commas
            if (token.Type == TokenType.Symbol && token.Value == ",")
            {
                pos++;
                continue;
            }

            // Column reference (possibly qualified)
            if (token.Type == TokenType.Identifier)
            {
                var (tableAlias, columnName) = SplitQualifiedName(token.Value);
                string outputAlias = string.Empty;

                // Check for AS alias
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
                        // Implicit alias (no AS keyword): SELECT col alias, ...
                        // But be careful not to eat the FROM keyword or table name
                        // Only treat as alias if it's not followed by a dot-qualified name
                        // For safety, we only do implicit alias if the next-next token is a comma or clause keyword
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

                model.Columns.Add(new ColumnRef
                {
                    TableAlias = tableAlias,
                    ColumnName = columnName,
                    OutputAlias = outputAlias
                });
                pos++;
                continue;
            }

            pos++;
        }

        return pos;
    }

}
