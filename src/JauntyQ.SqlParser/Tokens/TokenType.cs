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

    End
}
