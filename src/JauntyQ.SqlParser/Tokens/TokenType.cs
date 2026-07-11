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

    /// <summary>
    /// A character the tokenizer does not recognize (e.g. <c>$</c>, <c>\</c>,
    /// or a stray control character), emitted in place instead of being
    /// skipped — a skipped character silently corrupts the token stream and
    /// the parsed model no longer describes the query that will run. The
    /// generator refuses files containing Unknown tokens (JNT1004); lenient
    /// consumers keep tokenizing past them. <see cref="Token.Value"/> holds
    /// the offending character.
    /// </summary>
    Unknown,

    /// <summary>
    /// Parenthesis nesting exceeded <see cref="SqlTokenizer.MaxNestingDepth"/>.
    /// The recursive-descent parser copies each nested span before recursing,
    /// so parse cost is quadratic in depth; the tokenizer refuses pathologically
    /// deep input with this sentinel (mirroring <see cref="TooLarge"/>) rather
    /// than let a single crafted or machine-generated file hang the build. The
    /// generator refuses files containing it (JNT1005).
    /// <see cref="Token.Value"/> holds the depth that was reached.
    /// </summary>
    TooDeep,

    End
}
