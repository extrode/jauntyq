using JauntyQ.SqlParser;
using JauntyQ.SqlParser.Tokens;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
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
        Assert.Equal(TokenType.Number, tokens[3].Type);
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

    // ── Unterminated constructs: must stop, not silently consume to EOF ──

    [Fact]
    public void UnterminatedBlockComment_EmitsUnterminatedToken()
    {
        var tokens = SqlTokenizer.Tokenize("select id from users /* where id = @id");

        Assert.Contains(tokens, t => t.Type == TokenType.Unterminated);
        // Tokenizing stops immediately: nothing after the unterminated marker
        // except the End sentinel.
        int idx = tokens.FindIndex(t => t.Type == TokenType.Unterminated);
        Assert.Equal(TokenType.End, tokens[idx + 1].Type);
        Assert.Equal(idx + 2, tokens.Count);
    }

    [Fact]
    public void TerminatedBlockComment_NoUnterminatedToken()
    {
        var tokens = SqlTokenizer.Tokenize("select id /* a comment */ from users");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Unterminated);
    }

    [Fact]
    public void UnterminatedBracketIdentifier_EmitsUnterminatedToken()
    {
        var tokens = SqlTokenizer.Tokenize("select [Name From Products");

        Assert.Contains(tokens, t => t.Type == TokenType.Unterminated);
        int idx = tokens.FindIndex(t => t.Type == TokenType.Unterminated);
        Assert.Equal(TokenType.End, tokens[idx + 1].Type);
        Assert.Equal(idx + 2, tokens.Count);
    }

    [Fact]
    public void TerminatedBracketIdentifier_NoUnterminatedToken()
    {
        var tokens = SqlTokenizer.Tokenize("select [Product Name] from products");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Unterminated);
    }

    [Fact]
    public void UnterminatedStringLiteral_EmitsUnterminatedToken()
    {
        var tokens = SqlTokenizer.Tokenize("select id from users where name = 'unclosed");

        Assert.Contains(tokens, t => t.Type == TokenType.Unterminated);
        int idx = tokens.FindIndex(t => t.Type == TokenType.Unterminated);
        Assert.Equal(TokenType.End, tokens[idx + 1].Type);
        Assert.Equal(idx + 2, tokens.Count);
    }

    [Fact]
    public void UnterminatedStringLiteral_EndingInEscapedQuote_EmitsUnterminatedToken()
    {
        // The trailing '' is an ESCAPED quote inside a still-open literal.
        var tokens = SqlTokenizer.Tokenize("select 'abc''");

        Assert.Contains(tokens, t => t.Type == TokenType.Unterminated);
    }

    [Fact]
    public void TerminatedStringLiteral_NoUnterminatedToken()
    {
        var tokens = SqlTokenizer.Tokenize("select id from users where name = 'o''brien'");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Unterminated);
        Assert.Contains(tokens, t => t.Type == TokenType.Literal && t.Value == "o''brien");
    }

    // ── Double-quoted / backtick-quoted identifiers ──

    [Fact]
    public void DoubleQuotedIdentifier_TokenizesAsIdentifier()
    {
        var tokens = SqlTokenizer.Tokenize("select \"Product Name\" from products");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "Product Name");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Unterminated);
    }

    [Fact]
    public void BacktickQuotedIdentifier_TokenizesAsIdentifier()
    {
        var tokens = SqlTokenizer.Tokenize("select `Product Name` from products");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "Product Name");
    }

    [Fact]
    public void DoubleQuotedIdentifier_DoubledQuoteEscapes()
    {
        var tokens = SqlTokenizer.Tokenize("select \"a\"\"b\" from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "a\"b");
    }

    [Fact]
    public void QuotedIdentifier_NeverMatchesKeywords()
    {
        // A quoted "select" is an identifier, not the SELECT keyword.
        var tokens = SqlTokenizer.Tokenize("select \"select\" from t");
        Assert.Equal(1, tokens.Count(t => t.Type == TokenType.Keyword && t.Value == "SELECT"));
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "select");
    }

    [Fact]
    public void UnterminatedDoubleQuotedIdentifier_EmitsUnterminatedToken()
    {
        var tokens = SqlTokenizer.Tokenize("select \"Name from products");

        Assert.Contains(tokens, t => t.Type == TokenType.Unterminated);
        int idx = tokens.FindIndex(t => t.Type == TokenType.Unterminated);
        Assert.Equal(TokenType.End, tokens[idx + 1].Type);
        Assert.Equal(idx + 2, tokens.Count);
    }

    [Fact]
    public void UnterminatedBacktickIdentifier_EmitsUnterminatedToken()
    {
        var tokens = SqlTokenizer.Tokenize("select `Name from products");
        Assert.Contains(tokens, t => t.Type == TokenType.Unterminated);
    }

    // ── Input size cap: refuse oversized input, don't crash or silently pass ──

    [Fact]
    public void OversizedInput_EmitsTooLargeToken()
    {
        var oversized = new string('a', SqlTokenizer.MaxInputLength + 1);
        var tokens = SqlTokenizer.Tokenize(oversized);

        Assert.Contains(tokens, t => t.Type == TokenType.TooLarge);
        int idx = tokens.FindIndex(t => t.Type == TokenType.TooLarge);
        Assert.Equal(TokenType.End, tokens[idx + 1].Type);
        Assert.Equal(idx + 2, tokens.Count);
    }

    [Fact]
    public void InputAtCap_NoTooLargeToken()
    {
        var atCap = new string('a', SqlTokenizer.MaxInputLength);
        var tokens = SqlTokenizer.Tokenize(atCap);
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.TooLarge);
    }

    // ── Operator characters: must be tokens, never silently skipped ──
    // Skipping a character corrupts the stream: 'a || b' once tokenized as
    // two adjacent identifiers and parsed as two plain columns.

    [Fact]
    public void ConcatOperator_TokenizesAsSingleSymbol()
    {
        var tokens = SqlTokenizer.Tokenize("select first_name || last_name from users");

        int idx = tokens.FindIndex(t => t.Type == TokenType.Symbol && t.Value == "||");
        Assert.True(idx > 0);
        Assert.Equal("first_name", tokens[idx - 1].Value);
        Assert.Equal("last_name", tokens[idx + 1].Value);
    }

    [Fact]
    public void PostgresCastOperator_TokenizesAsSingleSymbol()
    {
        var tokens = SqlTokenizer.Tokenize("select id::text from users");
        Assert.Contains(tokens, t => t.Type == TokenType.Symbol && t.Value == "::");
    }

    [Theory]
    [InlineData("%")]
    [InlineData("&")]
    [InlineData("|")]
    [InlineData("^")]
    [InlineData("~")]
    [InlineData("?")]
    [InlineData("#")]
    public void OperatorCharacters_TokenizeAsSymbols(string op)
    {
        var tokens = SqlTokenizer.Tokenize($"select a {op} b from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Symbol && t.Value == op);
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Unknown);
    }

    // ── Exponent numeric literals ──

    [Theory]
    [InlineData("1e5")]
    [InlineData("2.5E-3")]
    [InlineData("7e+2")]
    public void ExponentNumericLiteral_TokenizesAsOneNumber(string literal)
    {
        var tokens = SqlTokenizer.Tokenize($"select {literal} as x from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == literal);
    }

    [Fact]
    public void DigitFollowedByBareE_DoesNotConsumeTheIdentifier()
    {
        // '1e' with no exponent digits: Number(1) then Identifier(e).
        var tokens = SqlTokenizer.Tokenize("select 1e from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "1");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "e");
    }

    // ── Hex literals ──

    [Theory]
    [InlineData("0x1F")]
    [InlineData("0XFF")]
    [InlineData("0x0")]
    public void HexLiteral_TokenizesAsOneNumber(string literal)
    {
        // Without a dedicated hex path, the digit-scan loop stops at '0'
        // ('x' is neither a digit nor '.'), splitting this into Number("0")
        // + Identifier("x1F") -- two unrelated tokens instead of the one
        // literal a query or migration DEFAULT actually wrote.
        var tokens = SqlTokenizer.Tokenize($"select {literal} as val from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == literal);
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Identifier && t.Value.StartsWith("x", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BareZeroXWithNoHexDigits_DoesNotConsumeTheIdentifier()
    {
        // "0x" with no hex digit following isn't a hex literal at all --
        // stays Number(0) then Identifier(x), same contract as
        // DigitFollowedByBareE_DoesNotConsumeTheIdentifier above.
        var tokens = SqlTokenizer.Tokenize("select 0x from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "0");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "x");
    }

    // ── National string literals ──

    [Theory]
    [InlineData("N")]
    [InlineData("n")]
    public void NationalStringLiteral_TokenizesAsSingleLiteral_NotIdentifierPlusLiteral(string prefix)
    {
        // Without a dedicated N-prefix check, "N" falls through to the
        // identifier branch as Identifier("N") and the following '...'
        // becomes a wholly separate Literal token -- two unrelated tokens
        // instead of the one Unicode string constant this ordinary,
        // extremely common T-SQL syntax actually writes.
        var tokens = SqlTokenizer.Tokenize($"select {prefix}'Active' as val from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Literal && t.Value == "Active");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Identifier &&
            string.Equals(t.Value, prefix, System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NationalStringLiteral_EscapedQuote_StillTerminatesCorrectly()
    {
        var tokens = SqlTokenizer.Tokenize("select N'o''brien' as val from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Literal && t.Value == "o''brien");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Unterminated);
    }

    [Fact]
    public void BareUppercaseN_NotFollowedByQuote_StaysAnIdentifier()
    {
        // Guards against the N-prefix check swallowing a genuine identifier
        // named "N" (or starting with it) that merely happens to be
        // followed by something other than a string literal.
        var tokens = SqlTokenizer.Tokenize("select N from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "N");
    }

    // ── Quoted qualified names merge like bare ones ──

    [Theory]
    [InlineData("select \"u\".\"col\" from users \"u\"")]
    [InlineData("select [u].[col] from users [u]")]
    [InlineData("select \"u\".col from users \"u\"")]
    [InlineData("select u.\"col\" from users u")]
    public void QuotedQualifiedName_MergesToSingleIdentifier(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "u.col");
    }

    [Fact]
    public void QuotedPartContainingDot_IsNotMerged()
    {
        // A quoted identifier with a literal dot would split wrongly if
        // merged; it must stay separate (and surface as an expression).
        var tokens = SqlTokenizer.Tokenize("select \"u\".\"a.b\" from t");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Identifier && t.Value == "u.a.b");
    }

    // ── Unknown characters: emitted in place, never silently skipped ──

    [Fact]
    public void UnknownCharacter_EmitsUnknownToken()
    {
        var tokens = SqlTokenizer.Tokenize("select id from users where id = $1");

        Assert.Contains(tokens, t => t.Type == TokenType.Unknown && t.Value == "$");
    }

    [Fact]
    public void UnknownCharacter_TokenizingContinuesPastIt()
    {
        // Lenient consumers (migration classification, usage scanning) must
        // still see everything after the unknown character.
        var tokens = SqlTokenizer.Tokenize("select $ id from users");

        Assert.Contains(tokens, t => t.Type == TokenType.Unknown);
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "FROM");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "users");
    }

    [Fact]
    public void ServerVariable_EmitsUnknownToken_NotGarbageParameters()
    {
        // Regression: "@@" used to fall through to the '@name' parameter
        // handling, where the second '@' isn't an identifier char -- yielding
        // a garbage empty-named Parameter token immediately followed by an
        // unrelated Parameter("IDENTITY") token, both of which look like real
        // bindable parameters to everything downstream.
        var tokens = SqlTokenizer.Tokenize("select seq_no from t where seq_no > @@IDENTITY");

        Assert.Contains(tokens, t => t.Type == TokenType.Unknown && t.Value.Contains("@@IDENTITY"));
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Parameter && t.Value == "");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Parameter && t.Value == "IDENTITY");
    }

    [Fact]
    public void ServerVariable_TokenizingContinuesPastIt()
    {
        var tokens = SqlTokenizer.Tokenize("select @@ROWCOUNT, id from users");

        Assert.Contains(tokens, t => t.Type == TokenType.Unknown && t.Value.Contains("@@ROWCOUNT"));
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "id");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "users");
    }

    [Fact]
    public void BareDoubleAt_MessageDoesNotAssertTSql()
    {
        // "@@" is also PostgreSQL's full-text-search match operator
        // (tsvector @@ tsquery), so a bare "@@" with no dialect context
        // must not claim it's specifically a T-SQL server variable -- that
        // was simply wrong for a Postgres file.
        var tokens = SqlTokenizer.Tokenize("select id from docs where body @@ query");

        var unknown = Assert.Single(tokens, t => t.Type == TokenType.Unknown);
        Assert.DoesNotContain("T-SQL server variables are not supported", unknown.Value);
        Assert.Contains("full-text-search", unknown.Value);
    }

    [Fact]
    public void ParenNestingBeyondCap_EmitsTooDeepSentinel()
    {
        int d = SqlTokenizer.MaxNestingDepth + 5;
        var tokens = SqlTokenizer.Tokenize("select " + new string('(', d) + "1" + new string(')', d) + " from t");

        // Two-token sentinel, same shape as TooLarge: refuse rather than let the
        // O(n²) parser run on pathologically deep input.
        Assert.Contains(tokens, t => t.Type == TokenType.TooDeep);
        Assert.Equal(TokenType.End, tokens[^1].Type);
    }

    [Fact]
    public void ParenNestingAtCap_NoTooDeepSentinel()
    {
        int d = SqlTokenizer.MaxNestingDepth; // exactly at the cap is allowed
        var tokens = SqlTokenizer.Tokenize("select " + new string('(', d) + "1" + new string(')', d) + " from t");

        Assert.DoesNotContain(tokens, t => t.Type == TokenType.TooDeep);
    }

    [Fact]
    public void ParensInStringLiteral_DoNotCountTowardDepth()
    {
        // A string full of '(' must not trip the structural-nesting cap.
        var tokens = SqlTokenizer.Tokenize("select '" + new string('(', SqlTokenizer.MaxNestingDepth + 50) + "' as v from t");

        Assert.DoesNotContain(tokens, t => t.Type == TokenType.TooDeep);
        Assert.Contains(tokens, t => t.Type == TokenType.Literal);
    }

    // AUD-R40-01: SqlTokenizer.IsEffectivelyEmpty is the shared check both
    // UsageManifestBuilder and ImpactCommand now rely on to detect a
    // directive-only/comment-only/whitespace-only file (most notably a
    // "-- @call ProcName" stored-procedure invocation) before ever handing
    // it to SqlParser.Parse.
    [Fact]
    public void IsEffectivelyEmpty_EmptyString_True()
    {
        Assert.True(SqlTokenizer.IsEffectivelyEmpty(SqlTokenizer.Tokenize("")));
    }

    [Fact]
    public void IsEffectivelyEmpty_WhitespaceOnly_True()
    {
        Assert.True(SqlTokenizer.IsEffectivelyEmpty(SqlTokenizer.Tokenize("   \n\t  \n")));
    }

    [Fact]
    public void IsEffectivelyEmpty_CommentOnly_True()
    {
        Assert.True(SqlTokenizer.IsEffectivelyEmpty(SqlTokenizer.Tokenize("-- just a comment\n-- another one")));
    }

    [Fact]
    public void IsEffectivelyEmpty_CallDirectiveOnly_True()
    {
        Assert.True(SqlTokenizer.IsEffectivelyEmpty(SqlTokenizer.Tokenize("-- @call GetProductsByCategory")));
    }

    [Fact]
    public void IsEffectivelyEmpty_BlockCommentOnly_True()
    {
        Assert.True(SqlTokenizer.IsEffectivelyEmpty(SqlTokenizer.Tokenize("/* just a block comment */")));
    }

    [Fact]
    public void IsEffectivelyEmpty_RealQuery_False()
    {
        Assert.False(SqlTokenizer.IsEffectivelyEmpty(SqlTokenizer.Tokenize("select id from t")));
    }

    [Fact]
    public void IsEffectivelyEmpty_GarbageNonSqlText_False() // still real (non-End) tokens; must go through the parser's own error path, not be silently skipped
    {
        Assert.False(SqlTokenizer.IsEffectivelyEmpty(SqlTokenizer.Tokenize("not really sql at all")));
    }

    [Fact]
    public void IsEffectivelyEmpty_UnterminatedBlockComment_False() // Unterminated sentinel is a real, non-End token: must still surface as unparseable, not be silently skipped
    {
        Assert.False(SqlTokenizer.IsEffectivelyEmpty(SqlTokenizer.Tokenize("/* never closed")));
    }

    // ── MySQL backslash escapes: measured, refused, and NOT silent ─────────
    //
    // The string-literal branch recognizes only the ANSI '' escape, so MySQL's
    // \' is not understood. the todo list carried this as an unverified
    // hypothesis that the stream is "silently corrupted"; measured 2026-08-17,
    // it is not — every divergence walks the quote parity off by one and ends
    // in the Unterminated sentinel, which JNT1002 refuses before the parser
    // ever runs. The cost is a confusing message, not a wrong build, and that
    // is why these pin the behavior rather than change it: an actual fix
    // needs dialect at the tokenizer, which it does not have (see the @@
    // branch's comment) and which is a decision, not a patch.

    [Fact]
    public void MySqlBackslashEscape_EndsInTheUnterminatedSentinel_NotACorruptStream()
    {
        var tokens = SqlTokenizer.Tokenize(@"select t.id from t where t.name = 'it\'s'");

        // The literal stops at the backslash-escaped quote, the tail
        // mis-tokenizes, and the run ends refused rather than parsed.
        Assert.Contains(tokens, t => t.Type == TokenType.Literal && t.Value == @"it\");
        Assert.Contains(tokens, t => t.Type == TokenType.Unterminated && t.Value == "' ... '");
    }

    /// <summary>
    /// The worst shape found: the mis-parse swallows a whole conjunct into the
    /// literal (<c>x\'' and t.b = </c>) before quote parity runs out. Even here
    /// the file still ends in Unterminated, so nothing reaches the generator —
    /// but this is the case to re-measure first if the refusal is ever relaxed.
    /// </summary>
    [Fact]
    public void MySqlBackslashEscape_CanSwallowAConjunct_ButStillEndsRefused()
    {
        var tokens = SqlTokenizer.Tokenize(@"select t.id from t where t.a = 'x\'' and t.b = 'y'");

        // The exact text, not just "contains the conjunct": the ANSI '' form at
        // the same position swallows an identical span, so a looser assertion
        // survives swapping the backslash out and pins nothing.
        Assert.Contains(tokens, t => t.Type == TokenType.Literal && t.Value == @"x\'' and t.b = ");
        Assert.Contains(tokens, t => t.Type == TokenType.Unterminated);
    }

    /// <summary>
    /// The other side of the same coin, and the reason the tokenizer cannot
    /// simply honor backslash escapes everywhere: <c>'C:\'</c> is a valid,
    /// complete literal under ANSI/T-SQL/Postgres rules and tokenizes as one
    /// today. Honoring MySQL's escape unconditionally would turn this correct
    /// SQL Server file into an unterminated-literal refusal.
    /// </summary>
    [Fact]
    public void TrailingBackslashLiteral_IsCompleteUnderAnsiRules()
    {
        var tokens = SqlTokenizer.Tokenize(@"select t.id from t where t.p = 'C:\'");

        Assert.Contains(tokens, t => t.Type == TokenType.Literal && t.Value == @"C:\");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Unterminated);
    }
}
