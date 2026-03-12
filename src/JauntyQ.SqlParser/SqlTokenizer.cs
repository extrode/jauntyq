using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.SqlParser;

public static class SqlTokenizer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "JOIN", "ON", "AND", "OR",
        "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "FULL",
        "GROUP", "BY", "ORDER", "LIMIT", "OFFSET",
        "AS", "IN", "NOT", "NULL", "IS", "LIKE", "BETWEEN",
        "INSERT", "UPDATE", "DELETE", "SET", "INTO", "VALUES",
        "HAVING", "DISTINCT", "TOP", "UNION", "ALL", "EXISTS",
        "CASE", "WHEN", "THEN", "ELSE", "END",
        "ASC", "DESC", "COUNT", "SUM", "AVG", "MIN", "MAX",
        "CAST", "COALESCE", "NULLIF", "WITH"
    };

    public static List<Token> Tokenize(string sql)
    {
        var tokens = new List<Token>();
        int pos = 0;
        int len = sql.Length;

        while (pos < len)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(sql[pos]))
            {
                pos++;
                continue;
            }

            // Single-line comment: -- ...
            if (pos + 1 < len && sql[pos] == '-' && sql[pos + 1] == '-')
            {
                while (pos < len && sql[pos] != '\n')
                    pos++;
                continue;
            }

            // Block comment: /* ... */
            if (pos + 1 < len && sql[pos] == '/' && sql[pos + 1] == '*')
            {
                pos += 2;
                while (pos + 1 < len && !(sql[pos] == '*' && sql[pos + 1] == '/'))
                    pos++;
                if (pos + 1 < len) pos += 2; // skip */
                continue;
            }

            // Parameter: @name
            if (sql[pos] == '@')
            {
                pos++; // skip @
                int start = pos;
                while (pos < len && IsIdentifierChar(sql[pos]))
                    pos++;
                tokens.Add(new Token(TokenType.Parameter, sql.Substring(start, pos - start)));
                continue;
            }

            // Bracket-quoted identifier: [Name With Spaces]
            if (sql[pos] == '[')
            {
                pos++; // skip opening bracket
                int start = pos;
                while (pos < len && sql[pos] != ']')
                    pos++;
                tokens.Add(new Token(TokenType.Identifier, sql.Substring(start, pos - start)));
                if (pos < len) pos++; // skip closing bracket
                continue;
            }

            // String literal: 'text'
            if (sql[pos] == '\'')
            {
                pos++; // skip opening quote
                int start = pos;
                while (pos < len)
                {
                    if (sql[pos] == '\'' && pos + 1 < len && sql[pos + 1] == '\'')
                    {
                        pos += 2; // escaped quote
                        continue;
                    }
                    if (sql[pos] == '\'')
                        break;
                    pos++;
                }
                tokens.Add(new Token(TokenType.Literal, sql.Substring(start, pos - start)));
                if (pos < len) pos++; // skip closing quote
                continue;
            }

            // Numeric literal
            if (char.IsDigit(sql[pos]))
            {
                int start = pos;
                while (pos < len && (char.IsDigit(sql[pos]) || sql[pos] == '.'))
                    pos++;
                tokens.Add(new Token(TokenType.Literal, sql.Substring(start, pos - start)));
                continue;
            }

            // Multi-character symbols
            if (pos + 1 < len)
            {
                string two = sql.Substring(pos, 2);

                if (two is "!=" or "<>" or "<=" or ">=")
                {
                    tokens.Add(new Token(TokenType.Symbol, two));
                    pos += 2;
                    continue;
                }
            }

            // Single-character symbols
            if (IsSymbolChar(sql[pos]))
            {
                tokens.Add(new Token(TokenType.Symbol, sql[pos].ToString()));
                pos++;
                continue;
            }

            // Identifier or keyword
            if (IsIdentifierStart(sql[pos]))
            {
                int start = pos;
                while (pos < len && (IsIdentifierChar(sql[pos]) || sql[pos] == '.'))
                    pos++;
                string word = sql.Substring(start, pos - start);

                // Check if it's a keyword (only if it doesn't contain a dot — dot-qualified = identifier)
                if (!word.Contains(".") && Keywords.Contains(word))
                {
                    tokens.Add(new Token(TokenType.Keyword, word.ToUpperInvariant()));
                }
                else
                {
                    tokens.Add(new Token(TokenType.Identifier, word));
                }
                continue;
            }

            // Unknown character — skip
            pos++;
        }

        tokens.Add(new Token(TokenType.End, string.Empty));
        return tokens;
    }

    private static bool IsIdentifierStart(char c) =>
        char.IsLetter(c) || c == '_';

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    private static bool IsSymbolChar(char c) =>
        c is ',' or '=' or '(' or ')' or '*' or
        '<' or '>' or '+' or '-' or '/' or
        ';' or '.' or '!';
}
