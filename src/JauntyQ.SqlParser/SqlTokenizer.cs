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
        "CAST", "COALESCE", "NULLIF", "WITH", "RECURSIVE", "RETURNING"
    };

    /// <summary>
    /// Upper bound on the length of SQL text the tokenizer will process. 1 MiB
    /// is far larger than any legitimate hand-written query file, but caps the
    /// worst-case the notes the tokenizer will do on a pathological or
    /// accidentally-huge input.
    /// </summary>
    public const int MaxInputLength = 1_048_576;

    public static List<Token> Tokenize(string sql)
    {
        // Refuse oversized input outright: bail with a TooLarge sentinel rather
        // than tokenize, mirroring the two-token shape used by the unterminated
        // cases below.
        if (sql.Length > MaxInputLength)
        {
            return new List<Token>
            {
                new Token(TokenType.TooLarge, sql.Length.ToString()),
                new Token(TokenType.End, string.Empty)
            };
        }

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
                if (pos + 1 < len)
                {
                    pos += 2; // skip */
                    continue;
                }
                // Ran to end-of-input with no closing */: stop tokenizing
                // instead of silently swallowing the rest of the file as
                // comment text.
                tokens.Add(new Token(TokenType.Unterminated, "/* ... */"));
                tokens.Add(new Token(TokenType.End, string.Empty));
                return tokens;
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
                if (pos >= len)
                {
                    // Ran to end-of-input with no closing ]: stop tokenizing
                    // instead of silently treating the rest of the file as
                    // one giant identifier.
                    tokens.Add(new Token(TokenType.Unterminated, "[ ... ]"));
                    tokens.Add(new Token(TokenType.End, string.Empty));
                    return tokens;
                }
                tokens.Add(new Token(TokenType.Identifier, sql.Substring(start, pos - start)));
                pos++; // skip closing bracket
                continue;
            }

            // Double-quoted (ANSI/Postgres/SQLite/SQL Server) or backtick-quoted
            // (MySQL/MariaDB) identifier. Previously these characters were
            // silently skipped, so the quoted content mis-tokenized as bare
            // identifiers/keywords and validation ran off a corrupted stream.
            // The embedded delimiter is escaped by doubling ("" / ``), per each
            // engine's rules.
            if (sql[pos] == '"' || sql[pos] == '`')
            {
                char quote = sql[pos];
                pos++; // skip opening quote
                var name = new System.Text.StringBuilder();
                bool closed = false;
                while (pos < len)
                {
                    if (sql[pos] == quote)
                    {
                        if (pos + 1 < len && sql[pos + 1] == quote)
                        {
                            name.Append(quote); // escaped delimiter
                            pos += 2;
                            continue;
                        }
                        closed = true;
                        pos++; // skip closing quote
                        break;
                    }
                    name.Append(sql[pos]);
                    pos++;
                }
                if (!closed)
                {
                    // Same end-of-input contract as brackets and block comments.
                    tokens.Add(new Token(TokenType.Unterminated, quote == '"' ? "\" ... \"" : "` ... `"));
                    tokens.Add(new Token(TokenType.End, string.Empty));
                    return tokens;
                }
                tokens.Add(new Token(TokenType.Identifier, name.ToString()));
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
                if (pos >= len)
                {
                    // Ran to end-of-input with no closing ': stop tokenizing
                    // instead of silently swallowing the rest of the file as
                    // literal text (same contract as block comments and
                    // quoted identifiers).
                    tokens.Add(new Token(TokenType.Unterminated, "' ... '"));
                    tokens.Add(new Token(TokenType.End, string.Empty));
                    return tokens;
                }
                tokens.Add(new Token(TokenType.Literal, sql.Substring(start, pos - start)));
                pos++; // skip closing quote
                continue;
            }

            // Numeric literal (with optional exponent: 1e5, 2.5E-3). A bare
            // 'e' with no exponent digits is not consumed — '1e' stays Number
            // then Identifier.
            if (char.IsDigit(sql[pos]))
            {
                int start = pos;
                while (pos < len && (char.IsDigit(sql[pos]) || sql[pos] == '.'))
                    pos++;
                if (pos < len && (sql[pos] == 'e' || sql[pos] == 'E'))
                {
                    int expPos = pos + 1;
                    if (expPos < len && (sql[expPos] == '+' || sql[expPos] == '-'))
                        expPos++;
                    if (expPos < len && char.IsDigit(sql[expPos]))
                    {
                        pos = expPos;
                        while (pos < len && char.IsDigit(sql[pos]))
                            pos++;
                    }
                }
                tokens.Add(new Token(TokenType.Number, sql.Substring(start, pos - start)));
                continue;
            }

            // Multi-character symbols. Direct char comparison with interned
            // literals — the tokenizer runs per-keystroke in the IDE, so this
            // path must not allocate a throwaway substring on every token.
            char c0 = sql[pos];
            if (pos + 1 < len)
            {
                char c1 = sql[pos + 1];
                string? two =
                    (c0 == '!' && c1 == '=') ? "!=" :
                    (c0 == '<' && c1 == '>') ? "<>" :
                    (c0 == '<' && c1 == '=') ? "<=" :
                    (c0 == '>' && c1 == '=') ? ">=" :
                    (c0 == '|' && c1 == '|') ? "||" :
                    (c0 == ':' && c1 == ':') ? "::" : null;
                if (two != null)
                {
                    tokens.Add(new Token(TokenType.Symbol, two));
                    pos += 2;
                    continue;
                }
            }

            // Single-character symbols (interned; no per-token ToString alloc)
            string? sym = SymbolLiteral(c0);
            if (sym != null)
            {
                tokens.Add(new Token(TokenType.Symbol, sym));
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

            // Unknown character: emit an Unknown token instead of skipping it.
            // A skipped character silently corrupts the token stream — 'a || b'
            // once parsed as two plain columns — and the resulting model passes
            // validation while describing a different query than the one that
            // will run. The generator refuses files containing Unknown tokens
            // (JNT1004); lenient consumers (migration classification, usage
            // scanning) keep tokenizing past it, so later statements still
            // classify instead of vanishing.
            tokens.Add(new Token(TokenType.Unknown, sql[pos].ToString()));
            pos++;
        }

        MergeQualifiedIdentifiers(tokens);
        tokens.Add(new Token(TokenType.End, string.Empty));
        return tokens;
    }

    /// <summary>
    /// Merges quoted qualified references into single dot-joined Identifier
    /// tokens, matching the shape the bare scan already produces for
    /// <c>p.product_id</c>. Without this, <c>"u"."col"</c> / <c>[u].[col]</c>
    /// tokenize as identifier-dot-identifier and demote to an expression
    /// demanding a spurious AS alias. Conservative: parts that themselves
    /// contain a dot are left unmerged (visible as an expression) rather than
    /// risk a wrong split downstream.
    /// </summary>
    private static void MergeQualifiedIdentifiers(List<Token> tokens)
    {
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Type != TokenType.Identifier)
                continue;

            // "u"."col" / [u].[col] / "u".col — Identifier '.' Identifier
            if (i + 2 < tokens.Count &&
                tokens[i + 1].Type == TokenType.Symbol && tokens[i + 1].Value == "." &&
                tokens[i + 2].Type == TokenType.Identifier &&
                !tokens[i].Value.Contains(".") && !tokens[i + 2].Value.Contains("."))
            {
                tokens[i] = new Token(TokenType.Identifier, tokens[i].Value + "." + tokens[i + 2].Value);
                tokens.RemoveRange(i + 1, 2);
                i--;
                continue;
            }

            // u."col" — the bare scan consumed the dot into the left token
            if (tokens[i].Value.EndsWith(".", StringComparison.Ordinal) &&
                tokens[i + 1].Type == TokenType.Identifier &&
                tokens[i].Value.IndexOf('.') == tokens[i].Value.Length - 1 &&
                !tokens[i + 1].Value.Contains("."))
            {
                tokens[i] = new Token(TokenType.Identifier, tokens[i].Value + tokens[i + 1].Value);
                tokens.RemoveAt(i + 1);
                i--;
            }
        }
    }

    private static bool IsIdentifierStart(char c) =>
        char.IsLetter(c) || c == '_';

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    // Returns the interned single-char symbol string, or null when c is not a
    // symbol. Interned literals avoid a per-token ToString() allocation.
    private static string? SymbolLiteral(char c) => c switch
    {
        ',' => ",",
        '=' => "=",
        '(' => "(",
        ')' => ")",
        '*' => "*",
        '<' => "<",
        '>' => ">",
        '+' => "+",
        '-' => "-",
        '/' => "/",
        ';' => ";",
        '.' => ".",
        '!' => "!",
        // Operator characters that appear inside expressions (modulo, bitwise,
        // Postgres concat/cast/json halves, MySQL '#', SQLite '?'). They only
        // need to BE tokens: an identifier followed by any symbol makes the
        // projection item an expression, which routes it through the explicit
        // alias + -- @type flow instead of mis-parsing as plain columns.
        '%' => "%",
        '&' => "&",
        '|' => "|",
        '^' => "^",
        '~' => "~",
        '?' => "?",
        ':' => ":",
        '#' => "#",
        _ => null
    };
}
