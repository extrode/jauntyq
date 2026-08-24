using CsCheck;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.Tokens;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

public class ParserPropertyTests
{
    private const int Iterations = 10_000;

    private static readonly string[] Keywords =
    {
        "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "INNER", "ON", "AS", "AND", "OR", "NOT",
        "NULL", "IS", "IN", "LIKE", "BETWEEN", "CASE", "WHEN", "THEN", "ELSE", "END",
        "GROUP", "BY", "HAVING", "ORDER", "LIMIT", "OFFSET", "WITH", "RECURSIVE", "UNION",
        "ALL", "DISTINCT", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE",
        "RETURNING", "EXISTS", "COUNT", "SUM", "AVG", "MIN", "MAX", "CAST",
    };

    private static readonly string[] Operators =
    {
        "=", "<>", "!=", "<", ">", "<=", ">=", "+", "-", "*", "/", "%", "||",
        ",", ".", ";", "(", ")",
    };

    private static readonly string[] Literals =
    {
        "42", "0", "-1", "19.99", "1e3", "'text'", "''", "'it''s'", "N'unicode'",
        "'((((('", "'--not a comment'", "'/* not a comment */'",
    };

    private static readonly string[] Identifiers =
    {
        "t", "u", "col", "my_table", "MyTable", "\"quoted\"", "`backtick`", "[bracket]",
        "t.col", "\"u\".\"col\"", "[u].[col]", "x1", "_leading",
    };

    private static readonly string[] Parameters = { "@id", "@Name", "@p0", "@each_ids" };

    private static readonly string[] Trivia =
    {
        " ", "\t", "\n", "\r\n", "\u0085", "\u2028", "\u2029", "-- line comment\n", "/* block */",
    };

    private static readonly Gen<string> Fragment = Gen.OneOf(
        Gen.OneOfConst(Keywords),
        Gen.OneOfConst(Operators),
        Gen.OneOfConst(Literals),
        Gen.OneOfConst(Identifiers),
        Gen.OneOfConst(Parameters));

    private static readonly Gen<string> Statement =
        Gen.Select(Fragment.Array[1, 40], Gen.OneOfConst(Trivia),
            (fragments, separator) => string.Join(separator, fragments));

    private static bool HasSentinel(List<Token> tokens) =>
        tokens.Exists(t => t.Type is TokenType.Unterminated or TokenType.Unknown
                                  or TokenType.TooLarge or TokenType.TooDeep);

    [Fact]
    public void Tokenize_IsTotal_OverTheTokenVocabulary()
    {
        Statement.Sample(sql =>
        {
            var tokens = SqlTokenizer.Tokenize(sql);
            Assert.NotEmpty(tokens);
            Assert.Equal(TokenType.End, tokens[tokens.Count - 1].Type);
        }, iter: Iterations);
    }

    [Fact]
    public void Parse_IsTotal_OverEveryCleanlyTokenizedStatement()
    {
        Statement.Sample(sql =>
        {
            var tokens = SqlTokenizer.Tokenize(sql);
            if (HasSentinel(tokens))
                return;
            var model = SqlParser.Parse(tokens, "PropertyQuery");
            Assert.NotNull(model);
        }, iter: Iterations);
    }

    [Fact]
    public void Parse_IsTotal_EvenWhenHandedASentinelBearingTokenList()
    {
        Statement.Sample(sql =>
        {
            var model = SqlParser.Parse(SqlTokenizer.Tokenize(sql), "PropertyQuery");
            Assert.NotNull(model);
        }, iter: Iterations);
    }

    [Fact]
    public void LineSeparatorTrivia_TokenizesIdenticallyToASpace()
    {
        Gen.Select(Fragment.Array[1, 20], Gen.OneOfConst("\u0085", "\u2028", "\u2029"),
            (fragments, separator) => (string.Join(" ", fragments), string.Join(separator, fragments)))
            .Sample(pair =>
            {
                var (withSpaces, withSeparators) = pair;
                var spaced = SqlTokenizer.Tokenize(withSpaces);
                var separated = SqlTokenizer.Tokenize(withSeparators);
                Assert.Equal(spaced.Count, separated.Count);
                for (int i = 0; i < spaced.Count; i++)
                {
                    Assert.Equal(spaced[i].Type, separated[i].Type);
                    Assert.Equal(spaced[i].Value, separated[i].Value);
                }
            }, iter: 2_000);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(999)]
    [InlineData(SqlTokenizer.MaxNestingDepth)]
    public void NestingAtOrBelowTheCap_TokenizesWithoutTheTooDeepSentinel(int depth)
    {
        string sql = "SELECT a FROM t WHERE " + new string('(', depth) + "1" + new string(')', depth);

        Assert.DoesNotContain(SqlTokenizer.Tokenize(sql), t => t.Type == TokenType.TooDeep);
    }

    [Theory]
    [InlineData(SqlTokenizer.MaxNestingDepth + 1)]
    [InlineData(SqlTokenizer.MaxNestingDepth + 2)]
    [InlineData(SqlTokenizer.MaxNestingDepth * 2)]
    public void NestingPastTheCap_YieldsTheTwoTokenTooDeepSentinel(int depth)
    {
        string sql = "SELECT a FROM t WHERE " + new string('(', depth) + "1" + new string(')', depth);

        var tokens = SqlTokenizer.Tokenize(sql);

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.TooDeep, tokens[0].Type);
        Assert.Equal(TokenType.End, tokens[1].Type);
    }

    [Theory]
    [InlineData("SELECT a FROM t WHERE b = '{0}'")]
    [InlineData("SELECT a FROM t /* {0} */ WHERE b = 1")]
    [InlineData("SELECT a FROM t WHERE [{0}] = 1")]
    [InlineData("SELECT a FROM t WHERE b = 1 -- {0}")]
    public void ParensInsideQuotingAndComments_NeverCountTowardTheDepthCap(string template)
    {
        string sql = string.Format(template, new string('(', SqlTokenizer.MaxNestingDepth * 3));

        Assert.DoesNotContain(SqlTokenizer.Tokenize(sql), t => t.Type == TokenType.TooDeep);
    }

    [Fact]
    public void StructuralParensAndLiteralParens_AreCountedSeparately()
    {
        string literalParens = new string('(', SqlTokenizer.MaxNestingDepth);
        string sql = "SELECT a FROM t WHERE b = '" + literalParens + "' AND "
                   + new string('(', SqlTokenizer.MaxNestingDepth) + "1"
                   + new string(')', SqlTokenizer.MaxNestingDepth);

        Assert.DoesNotContain(SqlTokenizer.Tokenize(sql), t => t.Type == TokenType.TooDeep);

        string oneDeeper = "SELECT a FROM t WHERE b = '" + literalParens + "' AND "
                         + new string('(', SqlTokenizer.MaxNestingDepth + 1) + "1"
                         + new string(')', SqlTokenizer.MaxNestingDepth + 1);

        Assert.Contains(SqlTokenizer.Tokenize(oneDeeper), t => t.Type == TokenType.TooDeep);
    }
}
