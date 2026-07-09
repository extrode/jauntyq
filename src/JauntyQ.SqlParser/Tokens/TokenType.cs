namespace JauntyQ.SqlParser.Tokens;

public enum TokenType
{
    Keyword,
    Identifier,
    Parameter,
    Symbol,

    /// <summary>String literal: 'text' (value excludes quotes, keeps doubled '' escapes).</summary>
    Literal,

    /// <summary>Numeric literal: 42, 19.99.</summary>
    Number,

    /// <summary>
    /// An unterminated block comment (<c>/* ...</c> with no closing <c>*/</c>)
    /// or bracket-quoted identifier (<c>[Name</c> with no closing <c>]</c>)
    /// that ran to end-of-input. The tokenizer stops at this point instead of
    /// silently treating the rest of the file as comment/identifier text;
    /// <see cref="Token.Value"/> holds a short description of what wasn't
    /// closed.
    /// </summary>
    Unterminated,

    /// <summary>
    /// The input SQL exceeded <see cref="SqlTokenizer.MaxInputLength"/>. The
    /// tokenizer refuses to run rather than do worst-case work on a pathological
    /// or accidentally-huge input; <see cref="Token.Value"/> holds the actual
    /// character count.
    /// </summary>
    TooLarge,

    End
}
