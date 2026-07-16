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
    /// True when <paramref name="word"/> tokenizes as a <see cref="TokenType.Keyword"/>
    /// rather than a plain identifier (case-insensitive). Callers that need to
    /// emit a bare (unquoted) column/table reference — e.g. <c>AutoCrud</c>,
    /// which skips names it can't safely emit unquoted — use this to reject
    /// reserved words before they ever reach the tokenizer, since an unquoted
    /// reserved word in that position silently mis-parses (see AutoCrud.cs).
    /// </summary>
    public static bool IsReservedKeyword(string word) => Keywords.Contains(word);

    /// <summary>
    /// Upper bound on the length of SQL text the tokenizer will process. 1 MiB
    /// is far larger than any legitimate hand-written query file, but caps the
    /// worst-case the notes the tokenizer will do on a pathological or
    /// accidentally-huge input.
    /// </summary>
    public const int MaxInputLength = 1_048_576;

    /// <summary>
    /// Upper bound on parenthesis nesting depth. The recursive-descent parser
    /// slices and re-copies each nested span before recursing, so parse cost is
    /// O(n²) in depth — a ~1 MiB file of nested parens (well under
    /// <see cref="MaxInputLength"/>) would hang the build for minutes. Real SQL,
    /// including machine-generated queries, never approaches this; 1000 leaves
    /// vast headroom while capping worst-case parse work at a few tens of ms.
    /// </summary>
    public const int MaxNestingDepth = 1000;

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

            // "@@": T-SQL server variables (@@IDENTITY, @@ROWCOUNT, etc.) or
            // PostgreSQL's full-text-search match operator (tsvector @@
            // tsquery) -- the tokenizer has no dialect context to tell which,
            // and neither is a bindable parameter. Without this check, the
            // '@' handling below treats the second '@' as "not an identifier
            // char", producing a garbage empty-named Parameter token (T-SQL
            // form: immediately followed by a second, unrelated
            // Parameter("IDENTITY") token). Emitted as Unknown (like any
            // other unsupported construct the tokenizer recognizes but can't
            // model) so the generator's existing JNT1004 refusal covers it
            // with a clear message, instead of the misleading "Parameter
            // '@' is not a valid C# identifier" that fell out of the garbage
            // empty-named token. The message names both possibilities rather
            // than asserting T-SQL, since asserting it is simply wrong for a
            // Postgres file.
            if (sql[pos] == '@' && pos + 1 < len && sql[pos + 1] == '@')
            {
                int start = pos;
                pos += 2;
                while (pos < len && IsIdentifierChar(sql[pos]))
                    pos++;
                tokens.Add(new Token(TokenType.Unknown,
                    $"'{sql.Substring(start, pos - start)}' (not supported: either a T-SQL server variable " +
                    "such as @@IDENTITY/@@ROWCOUNT, or PostgreSQL's full-text-search @@ match operator)"));
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

            // National string literal prefix: N'text' or n'text' (SQL Server/
            // ANSI SQL Unicode string constant -- ordinary, extremely common
            // T-SQL syntax). Without this, "N" falls through to the
            // identifier branch far below as Identifier("N") and the
            // following '...' becomes a wholly separate Literal token,
            // corrupting the stream for valid SQL: "SELECT N'Active' AS
            // Status" then fails type inference (JNT3005 "unresolved
            // expression alias"), and a WHERE clause's N'...' operand isn't
            // recognized as a Literal by ExtractLiteralBindings, so it
            // silently bypasses JNT5001/JNT5002 value-safety validation
            // entirely. Only consumes the prefix letter -- the '...' itself
            // is tokenized by the ordinary string-literal branch immediately
            // below.
            if ((sql[pos] == 'N' || sql[pos] == 'n') && pos + 1 < len && sql[pos + 1] == '\'')
                pos++;

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

            // Hex literal: 0x1F, 0XFF (SQL Server/MySQL binary literal syntax).
            // Without this, the digit-scan loop below stops at '0' (the
            // following 'x' is neither a digit nor '.'), splitting it into a
            // Number("0") token immediately followed by an Identifier
            // starting with "x" -- two unrelated tokens instead of the one
            // literal a query or migration DEFAULT actually wrote.
            if (sql[pos] == '0' && pos + 2 < len && (sql[pos + 1] == 'x' || sql[pos + 1] == 'X') &&
                IsHexDigit(sql[pos + 2]))
            {
                int hexStart = pos;
                pos += 2;
                while (pos < len && IsHexDigit(sql[pos]))
                    pos++;
                tokens.Add(new Token(TokenType.Number, sql.Substring(hexStart, pos - hexStart)));
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

        // Refuse pathologically deep parenthesis nesting before the quadratic
        // parser ever sees it: a single linear pass over the already-tokenized
        // symbols (cheap; tokenization is O(n)) finds the maximum depth, and a
        // two-token sentinel — same shape as TooLarge — stops here if it is
        // exceeded. Counts structural parens only; parens inside strings,
        // comments and bracket-quoted identifiers were never emitted as tokens.
        int depth = 0;
        foreach (var t in tokens)
        {
            if (t.Type != TokenType.Symbol)
                continue;
            if (t.Value == "(")
            {
                if (++depth > MaxNestingDepth)
                    return new List<Token>
                    {
                        new Token(TokenType.TooDeep, depth.ToString()),
                        new Token(TokenType.End, string.Empty)
                    };
            }
            else if (t.Value == ")" && depth > 0)
            {
                depth--;
            }
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
    /// demanding a spurious AS alias.
    ///
    /// <paramref name="tokens"/> is walked left to right, re-merging the same
    /// position after each successful merge (<c>i--</c> then the loop's own
    /// <c>i++</c> nets to no movement) so an arbitrary-length chain —
    /// <c>"db"."schema"."table"</c>, <c>[db].[schema].[table].[col]</c>, or a
    /// mixed bare-then-quoted tail like <c>dbo.users."first_name"</c> —
    /// collapses into one dotted Identifier token the same way
    /// <see cref="SqlParser.SplitQualifiedName"/> and
    /// <see cref="SqlParser.StripQualifier"/> already resolve N-part names.
    ///
    /// <c>continuingChain</c> tracks whether the CURRENT position was just
    /// produced by a merge on the previous iteration: only then is it safe to
    /// treat an already-dotted left operand as "more of the same chain"
    /// rather than a fresh identifier. Without that distinction, a quoted
    /// name that itself contains a literal dot (e.g. a table pathologically
    /// named <c>"weird.name"</c>) encountered fresh — not via chain
    /// continuation — would risk folding into a further qualifier with a
    /// silently wrong table/column split; that case is deliberately left
    /// unmerged, same as before this method supported multi-part chains.
    /// </summary>
    private static void MergeQualifiedIdentifiers(List<Token> tokens)
    {
        bool continuingChain = false;
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            bool leftIsChainContinuation = continuingChain;
            continuingChain = false;

            if (tokens[i].Type != TokenType.Identifier)
                continue;

            // "u"."col" / [u].[col] / "u".col — Identifier '.' Identifier.
            // The right operand must still be a single un-dotted segment; the
            // left operand may be an already-dotted chain only when this
            // position is a direct continuation of a chain merge just done.
            if (i + 2 < tokens.Count &&
                tokens[i + 1].Type == TokenType.Symbol && tokens[i + 1].Value == "." &&
                tokens[i + 2].Type == TokenType.Identifier &&
                (leftIsChainContinuation || !tokens[i].Value.Contains(".")) &&
                !tokens[i + 2].Value.Contains("."))
            {
                tokens[i] = new Token(TokenType.Identifier, tokens[i].Value + "." + tokens[i + 2].Value);
                tokens.RemoveRange(i + 1, 2);
                i--;
                continuingChain = true;
                continue;
            }

            // u."col" / dbo.users."col" — the bare scan consumed the dot(s)
            // into the left token. Unlike branch 1's quoted left operand, a
            // bare-scanned run ending in "." can never contain a *literal*
            // dot (SQL's bare/unquoted identifier grammar has no way to
            // express one — a literal dot always requires quoting), so an
            // arbitrarily multi-part bare prefix ("dbo.users.") is always
            // safe here; no chain-continuation guard needed.
            if (tokens[i].Value.EndsWith(".", StringComparison.Ordinal) &&
                tokens[i + 1].Type == TokenType.Identifier &&
                !tokens[i + 1].Value.Contains("."))
            {
                tokens[i] = new Token(TokenType.Identifier, tokens[i].Value + tokens[i + 1].Value);
                tokens.RemoveAt(i + 1);
                i--;
                continuingChain = true;
            }
        }
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

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
