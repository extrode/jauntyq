using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>
/// Targeted coverage for SqlTokenizer.cs mutation survivors not already
/// exercised by TokenizerTests.cs: exhaustive keyword/symbol enumeration,
/// end-of-input boundary conditions, and MergeQualifiedIdentifiers' internal
/// loop-position bookkeeping.
/// </summary>
public class SqlTokenizerMutationCoverageTests
{
    // ── Every reserved keyword must round-trip through the Keywords set ──
    // A String mutation on any one keyword literal removes it from the set
    // (typically to ""), which this exhaustive sweep catches regardless of
    // which literal was hit.

    [Theory]
    [InlineData("SELECT")] [InlineData("FROM")] [InlineData("WHERE")] [InlineData("JOIN")]
    [InlineData("ON")] [InlineData("AND")] [InlineData("OR")]
    [InlineData("LEFT")] [InlineData("RIGHT")] [InlineData("INNER")] [InlineData("OUTER")]
    [InlineData("CROSS")] [InlineData("FULL")]
    [InlineData("GROUP")] [InlineData("BY")] [InlineData("ORDER")] [InlineData("LIMIT")] [InlineData("OFFSET")]
    [InlineData("AS")] [InlineData("IN")] [InlineData("NOT")] [InlineData("NULL")] [InlineData("IS")]
    [InlineData("LIKE")] [InlineData("BETWEEN")]
    [InlineData("INSERT")] [InlineData("UPDATE")] [InlineData("DELETE")] [InlineData("SET")]
    [InlineData("INTO")] [InlineData("VALUES")]
    [InlineData("HAVING")] [InlineData("DISTINCT")] [InlineData("TOP")] [InlineData("UNION")]
    [InlineData("INTERSECT")] [InlineData("EXCEPT")] [InlineData("ALL")] [InlineData("EXISTS")]
    [InlineData("CASE")] [InlineData("WHEN")] [InlineData("THEN")] [InlineData("ELSE")] [InlineData("END")]
    [InlineData("ASC")] [InlineData("DESC")] [InlineData("COUNT")] [InlineData("SUM")] [InlineData("AVG")]
    [InlineData("MIN")] [InlineData("MAX")]
    [InlineData("CAST")] [InlineData("COALESCE")] [InlineData("NULLIF")] [InlineData("WITH")]
    [InlineData("RECURSIVE")] [InlineData("RETURNING")]
    public void EveryReservedKeyword_IsRecognizedByIsReservedKeyword_AndTokenizesAsKeyword(string keyword)
    {
        Assert.True(SqlTokenizer.IsReservedKeyword(keyword));

        var tokens = SqlTokenizer.Tokenize($"select 1 from t where x = 1 {keyword.ToLowerInvariant()} y");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == keyword);
    }

    // ── Block comment '*/' detection: both halves of the AND, not an OR ──

    [Fact]
    public void BlockComment_ContentContainingLoneAsteriskOrSlash_ClosesOnlyAtRealTerminator()
    {
        // "x*y" contains a lone '*' not followed by '/'; if the closing check
        // were `sql[pos]=='*' || sql[pos+1]=='/'` (Logical mutant) or either
        // comparison flipped (Equality mutant), the comment would close early
        // at that lone '*', corrupting everything downstream.
        var tokens = SqlTokenizer.Tokenize("select/*x*y*/1 from t");

        Assert.Equal(
            new[] { TokenType.Keyword, TokenType.Number, TokenType.Keyword, TokenType.Identifier, TokenType.End },
            tokens.Select(t => t.Type).ToArray());
        Assert.Equal("SELECT", tokens[0].Value);
        Assert.Equal("1", tokens[1].Value);
        Assert.Equal("FROM", tokens[2].Value);
        Assert.Equal("t", tokens[3].Value);
    }

    [Theory]
    [InlineData("select/**/1 from t")]
    [InlineData("select/* a/b */1 from t")]
    [InlineData("select/* a**/1 from t")]
    public void BlockComment_VariousContentShapes_CloseExactlyAtRealTerminator(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Unterminated);
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "1");
    }

    // ── "@@..." unsupported-construct handling ──

    [Theory]
    [InlineData("select @@ROWCOUNT+1 from t")]
    [InlineData("select @@ROWCOUNT")]
    public void DoubleAtVariable_IdentifierScan_CapturesExactNameNoMoreNoLess(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        var unknown = Assert.Single(tokens, t => t.Type == TokenType.Unknown);
        Assert.Equal(
            "'@@ROWCOUNT' (not supported: either a T-SQL server variable "
            + "such as @@IDENTITY/@@ROWCOUNT, or PostgreSQL's full-text-search @@ match operator)",
            unknown.Value);
    }

    // ── Unterminated quoted/backtick identifiers: exact sentinel per quote char ──

    [Theory]
    [InlineData('"', "\" ... \"")]
    [InlineData('`', "` ... `")]
    public void UnterminatedQuotedOrBacktickIdentifier_EmitsExactSentinel(char quoteChar, string expectedValue)
    {
        var tokens = SqlTokenizer.Tokenize($"select {quoteChar}abc from t");
        var unterminated = Assert.Single(tokens, t => t.Type == TokenType.Unterminated);
        Assert.Equal(expectedValue, unterminated.Value);
        Assert.Equal(TokenType.End, tokens[^1].Type);
        Assert.Equal(string.Empty, tokens[^1].Value);
    }

    // ── National string literal prefix N'...' / n'...' ──

    [Theory]
    [InlineData("select N'Active' as s from t")]
    [InlineData("select n'active' as s from t")]
    public void NationalStringPrefix_IsConsumed_LiteralHasNoLeadingPrefixChar(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        var lit = Assert.Single(tokens, t => t.Type == TokenType.Literal);
        Assert.DoesNotContain("N", lit.Value, System.StringComparison.Ordinal);
        Assert.DoesNotContain("n", lit.Value, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BareN_AtEndOfInput_NotFollowedByQuote_TokenizesAsPlainIdentifier()
    {
        // Exercises the pos+1<len boundary guard on the N'...' prefix check
        // with nothing after the 'N' at all.
        var tokens = SqlTokenizer.Tokenize("select N");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "N");
    }

    // ── Unterminated string literal's trailing End token ──

    [Fact]
    public void UnterminatedStringLiteral_TrailingEndToken_HasEmptyValue()
    {
        var tokens = SqlTokenizer.Tokenize("select id from users where name = 'unclosed");
        int idx = tokens.FindIndex(t => t.Type == TokenType.Unterminated);
        Assert.Equal(TokenType.End, tokens[idx + 1].Type);
        Assert.Equal(string.Empty, tokens[idx + 1].Value);
    }

    // ── Hex literal detection boundaries (IsHexDigit range checks) ──

    [Theory]
    [InlineData("0")] [InlineData("9")] [InlineData("a")] [InlineData("f")] [InlineData("A")] [InlineData("F")]
    public void HexDigitBoundary_ValidChars_FormHexLiteral(string ch)
    {
        var tokens = SqlTokenizer.Tokenize($"select 0x{ch}1 from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == $"0x{ch}1");
    }

    [Theory]
    [InlineData("/")] [InlineData(":")] [InlineData("`")] [InlineData("g")] [InlineData("@")] [InlineData("G")]
    public void HexDigitBoundary_InvalidChars_DoNotFormHexLiteral(string ch)
    {
        var tokens = SqlTokenizer.Tokenize($"select 0x{ch}1 from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "0");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Number && t.Value.StartsWith("0x"));
    }

    [Fact]
    public void HexLiteral_ExactlyAtInputEnd_TokenizesAsHexNumber_NoOverrun()
    {
        var tokens = SqlTokenizer.Tokenize("select 0x1F");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "0x1F");
    }

    [Fact]
    public void HexPrefixAtInputEnd_NoRoomForDigit_DoesNotOverrun()
    {
        // pos+2 would be exactly len here -- the `pos + 2 < len` guard must
        // reject this before ever indexing sql[pos + 2].
        var tokens = SqlTokenizer.Tokenize("select 0x");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "0");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "x");
    }

    [Fact]
    public void HexLiteral_FollowedByWhitespace_DoesNotEmitSpuriousUnknownToken()
    {
        // The digit-scan loop's own "continue" after emitting the hex Number
        // token matters specifically when the next character is whitespace:
        // without it, execution falls through the rest of the iteration's
        // checks (none of which re-skip whitespace, since that only happens
        // at the top of the outer while loop) and reaches the catch-all
        // "Unknown character" branch, wrongly emitting an Unknown token for
        // the space instead of silently skipping it.
        var tokens = SqlTokenizer.Tokenize("select 0x1F from t");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Unknown);
        Assert.Equal(
            new[] { TokenType.Keyword, TokenType.Number, TokenType.Keyword, TokenType.Identifier, TokenType.End },
            tokens.Select(t => t.Type).ToArray());
    }

    // ── Exponent numeric literal end-of-input boundaries ──

    [Theory]
    [InlineData("select 1e5", "1e5")]
    [InlineData("select 7e+2", "7e+2")]
    [InlineData("select 2.5E-3", "2.5E-3")]
    public void ExponentNumericLiteral_AtEndOfInput_DoesNotOverrun(string sql, string expected)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == expected);
    }

    [Fact]
    public void ExponentSignAtEndOfInput_WithNoFollowingDigit_DoesNotConsumeSign()
    {
        var tokens = SqlTokenizer.Tokenize("select 1e+");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "1");
    }

    [Fact]
    public void ExponentMarkerAtInputEnd_NoRoomForSignOrDigit_DoesNotOverrun()
    {
        // expPos would be exactly len here -- the `expPos < len` guard on the
        // sign check must reject this before indexing sql[expPos].
        var tokens = SqlTokenizer.Tokenize("select 1e");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "1");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "e");
    }

    // ── "::" cast operator vs. a lone ":" ──

    [Fact]
    public void DoubleColon_TokenizesAsSingleCastOperator()
    {
        var tokens = SqlTokenizer.Tokenize("select x::int from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Symbol && t.Value == "::");
    }

    [Fact]
    public void SingleColon_DoesNotMergeIntoDoubleColon()
    {
        var tokens = SqlTokenizer.Tokenize("select :name from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Symbol && t.Value == ":");
        Assert.DoesNotContain(tokens, t => t.Value == "::");
    }

    // ── Every single-character symbol tokenizes with its exact value ──

    [Theory]
    [InlineData(',')] [InlineData('=')] [InlineData('(')] [InlineData(')')] [InlineData('*')]
    [InlineData('<')] [InlineData('>')] [InlineData('+')] [InlineData('-')] [InlineData('/')]
    [InlineData(';')] [InlineData('!')] [InlineData('%')] [InlineData('&')]
    [InlineData('|')] [InlineData('^')] [InlineData('~')] [InlineData('?')] [InlineData(':')]
    [InlineData('#')]
    public void EachSingleCharSymbol_TokenizesWithExactValue(char c)
    {
        var tokens = SqlTokenizer.Tokenize($"select a {c} b from t");
        Assert.Contains(tokens, t => t.Type == TokenType.Symbol && t.Value == c.ToString());
    }

    // ── Paren-depth tracking: guard against underflow, wrong-type decrement,
    //    and increment/decrement being swapped ──

    [Fact]
    public void ParenDepthTracking_OpenCloseThenReopen_TracksRealDepthNotCumulativeOpens()
    {
        // 600 well-nested (1) pairs (never deep) followed by a real run of
        // MaxNestingDepth-1 opens (still under the cap). If real closes fail
        // to decrement depth back to 0 between pairs -- guard underflow,
        // wrong comparison, ')' literal mutated, or -- turned into ++ -- the
        // cumulative count would blow past the cap well before this file's
        // real maximum nesting (0) does.
        var sb = new System.Text.StringBuilder("select ");
        for (int i = 0; i < 600; i++)
            sb.Append("(1)");
        sb.Append('(', SqlTokenizer.MaxNestingDepth - 1);
        sb.Append('1');
        sb.Append(')', SqlTokenizer.MaxNestingDepth - 1);
        sb.Append(" from t");

        var tokens = SqlTokenizer.Tokenize(sb.ToString());
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.TooDeep);
    }

    [Fact]
    public void ParenDepthTracking_UnrelatedSymbolsInsideDeepNesting_DoNotDecrementDepth()
    {
        // Each open paren is immediately followed by a comma -- a Symbol
        // that is neither "(" nor ")". If the decrement guard's && were a ||
        // (Logical mutant), `Value == ")" || depth > 0` is satisfied by
        // depth > 0 alone, so every comma would wrongly decrement depth,
        // masking the real over-cap nesting below.
        var sb = new System.Text.StringBuilder("select ");
        int count = SqlTokenizer.MaxNestingDepth + 5;
        for (int i = 0; i < count; i++)
            sb.Append("(a,");
        sb.Append('1');
        sb.Append(')', count);
        sb.Append(" from t");

        var tokens = SqlTokenizer.Tokenize(sb.ToString());
        Assert.Contains(tokens, t => t.Type == TokenType.TooDeep);
    }

    [Fact]
    public void SingleStrayClosingParenAtZeroDepth_DoesNotDelayTooDeepThreshold()
    {
        // A lone ")" before any "(" hits depth == 0. The guard `depth > 0`
        // must block its decrement there; weakening it to `depth >= 0`
        // (Equality mutant) lets it decrement to -1, which then requires one
        // MORE real "(" than MaxNestingDepth+1 to trip TooDeep -- exactly
        // the margin this test allows none of.
        var tokens = SqlTokenizer.Tokenize(
            "select )" + new string('(', SqlTokenizer.MaxNestingDepth + 1)
            + "1" + new string(')', SqlTokenizer.MaxNestingDepth + 1) + " from t");

        Assert.Contains(tokens, t => t.Type == TokenType.TooDeep);
    }

    [Fact]
    public void NonSymbolTokenWithParenLikeValue_NeverCountsTowardDepth()
    {
        // A string literal whose entire content is exactly "(" produces a
        // Literal token with Value == "(". The depth-counting loop's own
        // `if (t.Type != TokenType.Symbol) continue;` guard exists
        // specifically so such tokens are skipped before the Value=="("
        // comparison ever runs. Removing that guard (Statement mutant) lets
        // MaxNestingDepth+ separate '(' -valued Literal tokens -- with zero
        // real Symbol parens anywhere -- wrongly trip the TooDeep sentinel.
        var sb = new System.Text.StringBuilder("select ");
        for (int i = 0; i < SqlTokenizer.MaxNestingDepth + 10; i++)
            sb.Append("'(' ");
        sb.Append("from t");

        var tokens = SqlTokenizer.Tokenize(sb.ToString());
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.TooDeep);
    }

    [Fact]
    public void SuccessfulTokenize_TrailingEndToken_HasEmptyValue()
    {
        var tokens = SqlTokenizer.Tokenize("select 1 from t");
        Assert.Equal(TokenType.End, tokens[^1].Type);
        Assert.Equal(string.Empty, tokens[^1].Value);
    }

    // ── MergeQualifiedIdentifiers: loop-position and chain-continuation state ──

    [Fact]
    public void QuotedIdentifierWithEmbeddedDot_AsVeryFirstToken_IsNotTreatedAsChainContinuation()
    {
        // At i=0 there is no prior iteration, so leftIsChainContinuation must
        // start false. If the initial `continuingChain` were seeded true
        // (Boolean mutant), a quoted identifier containing a literal dot as
        // the very first token would be wrongly folded into the qualifier
        // that follows it.
        var tokens = SqlTokenizer.Tokenize("\"weird.name\".col");

        Assert.Equal(4, tokens.Count); // 3 real tokens + trailing End sentinel
        Assert.Equal(TokenType.Identifier, tokens[0].Type);
        Assert.Equal("weird.name", tokens[0].Value);
        Assert.Equal(TokenType.Symbol, tokens[1].Type);
        Assert.Equal(".", tokens[1].Value);
        Assert.Equal(TokenType.Identifier, tokens[2].Type);
        Assert.Equal("col", tokens[2].Value);
        Assert.Equal(TokenType.End, tokens[3].Type);
    }

    [Fact]
    public void TrailingBareDotIdentifier_AsVeryLastToken_DoesNotThrow()
    {
        // MergeQualifiedIdentifiers runs BEFORE the End sentinel is appended,
        // so a bare-scanned identifier ending in "." (e.g. from "select
        // dbo.") can legitimately be the last token in the raw list. The
        // loop bound `i < tokens.Count - 1` exists specifically so this
        // position is never visited by branch 2, which indexes tokens[i+1]
        // with no bounds check of its own. Widening that bound by one
        // (Equality mutant) reaches this position and throws
        // IndexOutOfRangeException.
        var tokens = SqlTokenizer.Tokenize("select dbo.");
        Assert.NotEmpty(tokens);
        Assert.Equal(TokenType.End, tokens[^1].Type);
    }

    [Fact]
    public void BareChainEndingInDot_FollowedByQuotedTail_MergesEachQualifiedColumnIndependently()
    {
        var tokens = SqlTokenizer.Tokenize("select dbo.users.\"a\", schema.tbl.\"b\" from t");

        var identifiers = tokens.Where(t => t.Type == TokenType.Identifier).Select(t => t.Value).ToArray();
        Assert.Contains("dbo.users.a", identifiers);
        Assert.Contains("schema.tbl.b", identifiers);
    }

    [Fact]
    public void BareChainEndingInDot_ChainedWithFurtherQuotedSegments_FullyMergesAllParts()
    {
        // dbo.users."a"."b": branch 2 first merges the bare "dbo.users."
        // prefix with the quoted "a" into "dbo.users.a". Extending that
        // further with the trailing ."b" requires branch 1's
        // chain-continuation path, which only fires because the loop
        // revisits the SAME position (via `i--`) with `continuingChain` set
        // true. Corrupting that revisit -- i++ instead of i--, dropping it,
        // removing the tokens.RemoveAt(i + 1) that keeps indices aligned, or
        // never setting continuingChain -- leaves the chain half-merged as
        // "dbo.users.a" + "." + "b" instead of one identifier.
        var tokens = SqlTokenizer.Tokenize("select dbo.users.\"a\".\"b\" from t");

        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "dbo.users.a.b");
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Identifier && t.Value == "dbo.users.a");
    }

    [Fact]
    public void BareChainEndingInDot_QuotedTailContainingLiteralDot_IsNotMerged()
    {
        // The quoted tail's OWN value contains a literal dot, so branch 2's
        // right-operand guard must block the merge (folding would silently
        // corrupt which part was the real trailing qualifier vs. a literal
        // dot inside a name). This specifically pins the guard to
        // tokens[i + 1] (the actual right operand) rather than some other
        // position, such as tokens[i - 1] (e.g. the preceding SELECT
        // keyword, which never contains a dot and would wrongly pass the
        // guard if substituted in).
        var tokens = SqlTokenizer.Tokenize("select dbo.users.\"first.weird\" from t");

        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Identifier && t.Value == "dbo.users.first.weird");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "dbo.users.");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "first.weird");
    }
}
