using Extrode.JauntyQ.SqlParser.Tokens;

namespace Extrode.JauntyQ.SqlParser;

public readonly struct Token
{
    public TokenType Type { get; }
    public string Value { get; }

    public Token(TokenType type, string value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>The sentinel that ends every token list, including the sub-lists sliced for CTE and subquery bodies.</summary>
    internal static Token End => new(TokenType.End, string.Empty);

    public override string ToString() => $"{Type}({Value})";
}
