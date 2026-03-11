using JauntyQ.SqlParser;
using JauntyQ.SqlParser.Tokens;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

public class TokenizerTests
{
    [Fact]
    public void SimpleSelect_ReturnsCorrectTokens()
    {
        var tokens = SqlTokenizer.Tokenize("select product_id, product_name from products");

        Assert.Equal(TokenType.Keyword, tokens[0].Type);
        Assert.Equal("SELECT", tokens[0].Value);

        Assert.Equal(TokenType.Identifier, tokens[1].Type);
        Assert.Equal("product_id", tokens[1].Value);

        Assert.Equal(TokenType.Symbol, tokens[2].Type);
        Assert.Equal(",", tokens[2].Value);

        Assert.Equal(TokenType.Identifier, tokens[3].Type);
        Assert.Equal("product_name", tokens[3].Value);

        Assert.Equal(TokenType.Keyword, tokens[4].Type);
        Assert.Equal("FROM", tokens[4].Value);

        Assert.Equal(TokenType.Identifier, tokens[5].Type);
        Assert.Equal("products", tokens[5].Value);

        Assert.Equal(TokenType.End, tokens[6].Type);
    }

    [Fact]
    public void QualifiedColumns_RecognizedAsIdentifiers()
    {
        var tokens = SqlTokenizer.Tokenize("select p.product_id from products p");

        Assert.Equal(TokenType.Keyword, tokens[0].Type);
        Assert.Equal("SELECT", tokens[0].Value);

        Assert.Equal(TokenType.Identifier, tokens[1].Type);
        Assert.Equal("p.product_id", tokens[1].Value);

        Assert.Equal(TokenType.Keyword, tokens[2].Type);
        Assert.Equal("FROM", tokens[2].Value);

        Assert.Equal(TokenType.Identifier, tokens[3].Type);
        Assert.Equal("products", tokens[3].Value);

        Assert.Equal(TokenType.Identifier, tokens[4].Type);
        Assert.Equal("p", tokens[4].Value);

        Assert.Equal(TokenType.End, tokens[5].Type);
    }

    [Fact]
    public void Parameters_RecognizedWithAtPrefix()
    {
        var tokens = SqlTokenizer.Tokenize("where p.category_id = @categoryId");

        Assert.Equal(TokenType.Keyword, tokens[0].Type);
        Assert.Equal("WHERE", tokens[0].Value);

        Assert.Equal(TokenType.Identifier, tokens[1].Type);
        Assert.Equal("p.category_id", tokens[1].Value);

        Assert.Equal(TokenType.Symbol, tokens[2].Type);
        Assert.Equal("=", tokens[2].Value);

        Assert.Equal(TokenType.Parameter, tokens[3].Type);
        Assert.Equal("categoryId", tokens[3].Value);

        Assert.Equal(TokenType.End, tokens[4].Type);
    }

    [Fact]
    public void JoinClause_TokenizedCorrectly()
    {
        var tokens = SqlTokenizer.Tokenize(
            "join categories c on p.category_id = c.category_id");

        Assert.Equal(TokenType.Keyword, tokens[0].Type);
        Assert.Equal("JOIN", tokens[0].Value);

        Assert.Equal(TokenType.Identifier, tokens[1].Type);
        Assert.Equal("categories", tokens[1].Value);

        Assert.Equal(TokenType.Identifier, tokens[2].Type);
        Assert.Equal("c", tokens[2].Value);

        Assert.Equal(TokenType.Keyword, tokens[3].Type);
        Assert.Equal("ON", tokens[3].Value);

        Assert.Equal(TokenType.Identifier, tokens[4].Type);
        Assert.Equal("p.category_id", tokens[4].Value);

        Assert.Equal(TokenType.Symbol, tokens[5].Type);
        Assert.Equal("=", tokens[5].Value);

        Assert.Equal(TokenType.Identifier, tokens[6].Type);
        Assert.Equal("c.category_id", tokens[6].Value);

        Assert.Equal(TokenType.End, tokens[7].Type);
    }

    [Fact]
    public void MultipleParameters_AllRecognized()
    {
        var tokens = SqlTokenizer.Tokenize(
            "where p.price > @minPrice and p.price < @maxPrice");

        var parameters = tokens
            .Where(t => t.Type == TokenType.Parameter)
            .Select(t => t.Value)
            .ToList();

        Assert.Equal(2, parameters.Count);
        Assert.Equal("minPrice", parameters[0]);
        Assert.Equal("maxPrice", parameters[1]);
    }

    [Fact]
    public void StringLiteral_RecognizedCorrectly()
    {
        var tokens = SqlTokenizer.Tokenize("where name = 'test'");

        Assert.Equal(TokenType.Keyword, tokens[0].Type);
        Assert.Equal("WHERE", tokens[0].Value);

        Assert.Equal(TokenType.Identifier, tokens[1].Type);
        Assert.Equal("name", tokens[1].Value);

        Assert.Equal(TokenType.Symbol, tokens[2].Type);
        Assert.Equal("=", tokens[2].Value);

        Assert.Equal(TokenType.Literal, tokens[3].Type);
        Assert.Equal("test", tokens[3].Value);
    }

    [Fact]
    public void StarSelect_RecognizedAsSymbol()
    {
        var tokens = SqlTokenizer.Tokenize("select * from products");

        Assert.Equal(TokenType.Keyword, tokens[0].Type);
        Assert.Equal("SELECT", tokens[0].Value);

        Assert.Equal(TokenType.Symbol, tokens[1].Type);
        Assert.Equal("*", tokens[1].Value);

        Assert.Equal(TokenType.Keyword, tokens[2].Type);
        Assert.Equal("FROM", tokens[2].Value);

        Assert.Equal(TokenType.Identifier, tokens[3].Type);
        Assert.Equal("products", tokens[3].Value);
    }

    [Fact]
    public void ReferenceQuery_FullTokenization()
    {
        var sql = @"
select p.product_id, p.product_name
from products p
join categories c on p.category_id = c.category_id
where p.category_id = @categoryId";

        var tokens = SqlTokenizer.Tokenize(sql);

        // Remove the End token for counting
        var meaningful = tokens.Where(t => t.Type != TokenType.End).ToList();

        // Verify key structural elements
        Assert.Equal("SELECT", meaningful[0].Value);
        Assert.Contains(meaningful, t => t.Type == TokenType.Identifier && t.Value == "p.product_id");
        Assert.Contains(meaningful, t => t.Type == TokenType.Identifier && t.Value == "p.product_name");
        Assert.Contains(meaningful, t => t.Type == TokenType.Keyword && t.Value == "FROM");
        Assert.Contains(meaningful, t => t.Type == TokenType.Identifier && t.Value == "products");
        Assert.Contains(meaningful, t => t.Type == TokenType.Keyword && t.Value == "JOIN");
        Assert.Contains(meaningful, t => t.Type == TokenType.Identifier && t.Value == "categories");
        Assert.Contains(meaningful, t => t.Type == TokenType.Keyword && t.Value == "ON");
        Assert.Contains(meaningful, t => t.Type == TokenType.Keyword && t.Value == "WHERE");
        Assert.Contains(meaningful, t => t.Type == TokenType.Parameter && t.Value == "categoryId");
    }

    [Fact]
    public void NumericLiteral_RecognizedCorrectly()
    {
        var tokens = SqlTokenizer.Tokenize("where price > 19.99");

        // WHERE(0) price(1) >(2) 19.99(3)
        Assert.Equal(TokenType.Literal, tokens[3].Type);
        Assert.Equal("19.99", tokens[3].Value);
    }

    [Fact]
    public void ComparisonOperators_RecognizedCorrectly()
    {
        var tokens = SqlTokenizer.Tokenize("where a != b and c <> d and e <= f and g >= h");

        var symbols = tokens.Where(t => t.Type == TokenType.Symbol).Select(t => t.Value).ToList();
        Assert.Contains("!=", symbols);
        Assert.Contains("<>", symbols);
        Assert.Contains("<=", symbols);
        Assert.Contains(">=", symbols);
    }

    [Fact]
    public void Comments_AreSkipped()
    {
        var tokens = SqlTokenizer.Tokenize(@"
-- this is a comment
select product_id /* inline comment */ from products");

        var meaningful = tokens.Where(t => t.Type != TokenType.End).ToList();
        Assert.Equal(4, meaningful.Count);
        Assert.Equal("SELECT", meaningful[0].Value);
        Assert.Equal("product_id", meaningful[1].Value);
        Assert.Equal("FROM", meaningful[2].Value);
        Assert.Equal("products", meaningful[3].Value);
    }

    [Fact]
    public void EscapedStringLiteral_HandledCorrectly()
    {
        var tokens = SqlTokenizer.Tokenize("where name = 'it''s a test'");

        Assert.Equal(TokenType.Literal, tokens[3].Type);
        Assert.Equal("it''s a test", tokens[3].Value);
    }

    [Fact]
    public void EndToken_AlwaysLast()
    {
        var tokens = SqlTokenizer.Tokenize("select 1");
        Assert.Equal(TokenType.End, tokens[^1].Type);

        tokens = SqlTokenizer.Tokenize("");
        Assert.Single(tokens);
        Assert.Equal(TokenType.End, tokens[0].Type);
    }
}
