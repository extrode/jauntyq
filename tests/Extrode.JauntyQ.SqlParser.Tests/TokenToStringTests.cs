using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>Public-API coverage for <see cref="Token.ToString"/>.</summary>
public class TokenToStringTests
{
    [Fact]
    public void ToString_ShowsTypeAndValue()
    {
        var t = new Token(TokenType.Keyword, "SELECT");
        Assert.Equal("Keyword(SELECT)", t.ToString());
    }

    [Theory]
    [InlineData(TokenType.Identifier, "users")]
    [InlineData(TokenType.Symbol, "(")]
    [InlineData(TokenType.End, "")]
    public void ToString_RoundTripsEachTokenKind(TokenType type, string value)
    {
        Assert.Equal($"{type}({value})", new Token(type, value).ToString());
    }
}
