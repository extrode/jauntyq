using Extrode.JauntyQ.SqlParser.Tokens;

namespace Extrode.JauntyQ.SqlParser;

public readonly struct Token
{
    public TokenType Type { get; }
    public string Value { get; }

    /// <summary>True for an identifier written in double quotes or backticks, whose case the database keeps as written.</summary>
    public bool IsQuoted { get; }

    public Token(TokenType type, string value)
        : this(type, value, false)
    {
    }

    public Token(TokenType type, string value, bool isQuoted)
    {
        Type = type;
        Value = value;
        IsQuoted = isQuoted;
    }

    /// <summary>The sentinel that ends every token list, including the sub-lists sliced for CTE and subquery bodies.</summary>
    internal static Token End => new(TokenType.End, string.Empty);

    public override string ToString() => $"{Type}({Value})";
}
