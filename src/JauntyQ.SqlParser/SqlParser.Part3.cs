using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.SqlParser;
public static partial class SqlParser
{
    private static void ExtractParameterBindings(List<Token> tokens, QueryModel model)
    {
        // Scan for: identifier <op> @param  or  @param <op> identifier
        for (int i = 1; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Type != TokenType.Symbol || !ComparisonOperators.Contains(tokens[i].Value))
                continue;

            var left = tokens[i - 1];
            var right = tokens[i + 1];

            string? paramName = null;
            string tableAlias = string.Empty;
            string columnName = string.Empty;

            if (left.Type == TokenType.Identifier && right.Type == TokenType.Parameter)
            {
                paramName = right.Value;
                (tableAlias, columnName) = SplitQualifiedName(left.Value);
            }
            else if (left.Type == TokenType.Parameter && right.Type == TokenType.Identifier)
            {
                paramName = left.Value;
                (tableAlias, columnName) = SplitQualifiedName(right.Value);
            }

            if (paramName == null)
                continue;

            // Find the ParameterRef and bind it (first binding wins)
            var paramRef = model.Parameters.FirstOrDefault(p => p.Name == paramName);
            if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
            {
                paramRef.BoundTableAlias = tableAlias;
                paramRef.BoundColumnName = columnName;
                paramRef.ComparisonOp = tokens[i].Value;
            }
        }

        // Also scan for: identifier IN (@param) — common pattern
        for (int i = 0; i < tokens.Count - 4; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "IN" &&
                tokens[i + 2].Type == TokenType.Symbol && tokens[i + 2].Value == "(" &&
                tokens[i + 3].Type == TokenType.Parameter)
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                var paramRef = model.Parameters.FirstOrDefault(p => p.Name == tokens[i + 3].Value);
                if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
                {
                    paramRef.BoundTableAlias = tableAlias;
                    paramRef.BoundColumnName = columnName;
                    paramRef.ComparisonOp = "IN";
                }
            }
        }

        // Also scan for: identifier LIKE @param
        for (int i = 0; i < tokens.Count - 2; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "LIKE" &&
                tokens[i + 2].Type == TokenType.Parameter)
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                var paramRef = model.Parameters.FirstOrDefault(p => p.Name == tokens[i + 2].Value);
                if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
                {
                    paramRef.BoundTableAlias = tableAlias;
                    paramRef.BoundColumnName = columnName;
                    paramRef.ComparisonOp = "LIKE";
                }
            }
        }

        // Also scan for: identifier BETWEEN @param AND ...
        for (int i = 0; i < tokens.Count - 2; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "BETWEEN" &&
                tokens[i + 2].Type == TokenType.Parameter)
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                var paramRef = model.Parameters.FirstOrDefault(p => p.Name == tokens[i + 2].Value);
                if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
                {
                    paramRef.BoundTableAlias = tableAlias;
                    paramRef.BoundColumnName = columnName;
                    paramRef.ComparisonOp = "BETWEEN";
                }
            }
        }
    }

    /// <summary>
    /// Captures literals compared to or assigned into columns:
    /// col = 'x', col >= 19.99, SET col = 'x', col IN (1, 2, 3).
    /// LIKE patterns are deliberately skipped (wildcards make length
    /// reasoning unsound). Best-effort: unresolvable bindings are simply
    /// not validated.
    /// </summary>
    private static void ExtractLiteralBindings(List<Token> tokens, QueryModel model)
    {
        // identifier <op> literal  or  literal <op> identifier
        for (int i = 1; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Type != TokenType.Symbol || !ComparisonOperators.Contains(tokens[i].Value))
                continue;

            var left = tokens[i - 1];
            var right = tokens[i + 1];

            bool found = false;
            TokenType literalType = TokenType.Literal;
            string literalValue = string.Empty;
            string tableAlias = string.Empty;
            string columnName = string.Empty;

            if (left.Type == TokenType.Identifier &&
                (right.Type == TokenType.Literal || right.Type == TokenType.Number))
            {
                found = true;
                literalType = right.Type;
                literalValue = right.Value;
                (tableAlias, columnName) = SplitQualifiedName(left.Value);
            }
            else if (left.Type == TokenType.Identifier &&
                     right.Type == TokenType.Symbol && right.Value == "-" &&
                     i + 2 < tokens.Count && tokens[i + 2].Type == TokenType.Number)
            {
                // col <op> -N : unary minus (nothing precedes the '-' but the
                // comparison operator, so it cannot be a subtraction)
                found = true;
                literalType = TokenType.Number;
                literalValue = "-" + tokens[i + 2].Value;
                (tableAlias, columnName) = SplitQualifiedName(left.Value);
            }
            else if ((left.Type == TokenType.Literal || left.Type == TokenType.Number) &&
                     right.Type == TokenType.Identifier)
            {
                found = true;
                literalType = left.Type;
                literalValue = left.Value;
                (tableAlias, columnName) = SplitQualifiedName(right.Value);
            }

            if (!found)
                continue;

            model.Literals.Add(new LiteralBinding
            {
                Kind = literalType == TokenType.Literal ? LiteralKind.String : LiteralKind.Number,
                Value = literalValue,
                BoundTableAlias = tableAlias,
                BoundColumnName = columnName
            });
        }

        // identifier IN ( literal, literal, ... ) — every literal in the list is checked
        for (int i = 0; i < tokens.Count - 3; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "IN" &&
                tokens[i + 2].Type == TokenType.Symbol && tokens[i + 2].Value == "(")
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                int j = i + 3;
                while (j < tokens.Count && !(tokens[j].Type == TokenType.Symbol && tokens[j].Value == ")"))
                {
                    // -N in the list: unary minus (list items are comma-
                    // separated, so a '-' before a number is never subtraction)
                    if (tokens[j].Type == TokenType.Symbol && tokens[j].Value == "-" &&
                        j + 1 < tokens.Count && tokens[j + 1].Type == TokenType.Number)
                    {
                        model.Literals.Add(new LiteralBinding
                        {
                            Kind = LiteralKind.Number,
                            Value = "-" + tokens[j + 1].Value,
                            BoundTableAlias = tableAlias,
                            BoundColumnName = columnName
                        });
                        j += 2;
                        continue;
                    }
                    if (tokens[j].Type == TokenType.Literal || tokens[j].Type == TokenType.Number)
                    {
                        model.Literals.Add(new LiteralBinding
                        {
                            Kind = tokens[j].Type == TokenType.Literal ? LiteralKind.String : LiteralKind.Number,
                            Value = tokens[j].Value,
                            BoundTableAlias = tableAlias,
                            BoundColumnName = columnName
                        });
                    }
                    j++;
                }
            }
        }
    }

    // Function-call heads that the tokenizer classifies as keywords.
    private static readonly HashSet<string> FunctionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CAST", "COALESCE", "NULLIF", "COUNT", "SUM", "AVG", "MIN", "MAX"
    };

}
