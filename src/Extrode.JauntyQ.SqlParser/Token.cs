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

    public override string ToString() => $"{Type}({Value})";
}
