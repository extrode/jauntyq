using System.Linq;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>
/// Direct coverage for SqlParser.Part3.cs mutation survivors:
/// ExtractParameterBindings' three scan passes (col &lt;op&gt; @param,
/// identifier IN (@param), identifier LIKE @param, identifier BETWEEN
/// @lower AND @upper), its "first binding wins" overwrite guard, and
/// ExtractLiteralBindings' three scan passes (col &lt;op&gt; literal
/// including the unary-minus special case, identifier IN (literal, ...)
/// including its own unary-minus special case and closing-paren-vs-literal
/// disambiguation).
/// </summary>
[Trait("Category", "AuditRegression")]
public class SqlParserPart3MutationCoverageTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    // ── ExtractParameterBindings: col <op> @param / @param <op> col ──

    [Fact]
    public void ParamBoundTwice_FirstOccurrenceWins()
    {
        var model = ParseSql("SELECT * FROM t WHERE a = @p AND b = @p");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal("a", p.BoundColumnName);
    }

    [Fact]
    public void IdentifierEqualsIdentifier_DoesNotBindUnrelatedParamByCoincidentName()
    {
        // "a = b" must not be mistaken for "identifier <op> @param" just
        // because a real parameter named @b happens to exist elsewhere.
        var model = ParseSql("SELECT * FROM t WHERE a = b AND x = @b");

        var p = model.Parameters.Single(x => x.Name == "b");
        Assert.Equal("x", p.BoundColumnName);
    }

    [Fact]
    public void ParameterComparedToNumberLiteral_DoesNotBindViaIdentifierPath()
    {
        // "@p = 5": right side is a Number, not an Identifier, so the
        // Parameter/Identifier branch must not fire.
        var model = ParseSql("SELECT * FROM t WHERE @p = 5");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.True(string.IsNullOrEmpty(p.BoundColumnName));
    }

    [Fact]
    public void ParameterCompared_RightSideIdentifier_Binds()
    {
        var model = ParseSql("SELECT * FROM t WHERE @p = a");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal("a", p.BoundColumnName);
        Assert.Equal("=", p.ComparisonOp);
    }

    // ── ExtractParameterBindings: identifier IN (@param) ─────────────

    [Fact]
    public void IdentifierInParam_BindsColumn()
    {
        var model = ParseSql("SELECT * FROM t WHERE a IN (@p)");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal("a", p.BoundColumnName);
        Assert.Equal("IN", p.ComparisonOp);
    }

    [Fact]
    public void NumberLiteral_InParam_DoesNotBind()
    {
        var model = ParseSql("SELECT * FROM t WHERE 5 IN (@p)");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.True(string.IsNullOrEmpty(p.BoundColumnName));
    }

    [Fact]
    public void IdentifierNotFollowedByInKeyword_DoesNotBindParam()
    {
        var model = ParseSql("SELECT * FROM t WHERE a = (@p)");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.True(string.IsNullOrEmpty(p.BoundColumnName));
    }

    [Fact]
    public void IdentifierInWithoutOpenParen_DoesNotBindParam()
    {
        var model = ParseSql("SELECT * FROM t WHERE a IN @p");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.True(string.IsNullOrEmpty(p.BoundColumnName));
    }

    [Fact]
    public void IdentifierInParenNumber_DoesNotBindAsParam()
    {
        var model = ParseSql("SELECT * FROM t WHERE a IN (5, @p)");

        // "a IN (5, @p)" -- the fourth token after "a IN (" is a Number,
        // not the Parameter itself, so the single-param IN pattern must not
        // match and bind @p to "a" from this scan (BETWEEN/IN-list literal
        // handling is a separate concern; only ExtractParameterBindings'
        // single-param IN pattern is under test here).
        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.True(string.IsNullOrEmpty(p.BoundColumnName));
    }

    // ── ExtractParameterBindings: identifier LIKE @param ─────────────

    [Fact]
    public void IdentifierLikeParam_BindsColumn()
    {
        var model = ParseSql("SELECT * FROM t WHERE name LIKE @pattern");

        var p = model.Parameters.Single(x => x.Name == "pattern");
        Assert.Equal("name", p.BoundColumnName);
        Assert.Equal("LIKE", p.ComparisonOp);
    }

    [Fact]
    public void NumberLiteral_LikeParam_DoesNotBind()
    {
        var model = ParseSql("SELECT * FROM t WHERE 5 LIKE @pattern");

        var p = model.Parameters.Single(x => x.Name == "pattern");
        Assert.True(string.IsNullOrEmpty(p.BoundColumnName));
    }

    // ── ExtractParameterBindings: identifier BETWEEN @lower AND @upper ─

    [Fact]
    public void IdentifierBetweenTwoParams_BindsBothToSameColumn()
    {
        var model = ParseSql("SELECT * FROM t WHERE age BETWEEN @lo AND @hi");

        var lo = model.Parameters.Single(x => x.Name == "lo");
        var hi = model.Parameters.Single(x => x.Name == "hi");
        Assert.Equal("age", lo.BoundColumnName);
        Assert.Equal("BETWEEN", lo.ComparisonOp);
        Assert.Equal("age", hi.BoundColumnName);
        Assert.Equal("BETWEEN", hi.ComparisonOp);
    }

    [Fact]
    public void IdentifierBetweenParam_MissingAndUpper_OnlyLowerBinds()
    {
        // "age BETWEEN @lo" with nothing after it: the AND/@upper
        // continuation must not run off the end of the token stream or
        // bind a phantom upper parameter.
        var model = ParseSql("SELECT * FROM t WHERE age BETWEEN @lo");

        var lo = model.Parameters.Single(x => x.Name == "lo");
        Assert.Equal("age", lo.BoundColumnName);
    }

    [Fact]
    public void IdentifierBetweenParam_FollowedByOrInsteadOfAnd_OnlyLowerBinds()
    {
        var model = ParseSql("SELECT * FROM t WHERE age BETWEEN @lo OR x = 1");

        var lo = model.Parameters.Single(x => x.Name == "lo");
        Assert.Equal("age", lo.BoundColumnName);
    }

    [Fact]
    public void NumberLiteral_BetweenParams_DoesNotBind()
    {
        var model = ParseSql("SELECT * FROM t WHERE 5 BETWEEN @lo AND @hi");

        var lo = model.Parameters.Single(x => x.Name == "lo");
        var hi = model.Parameters.Single(x => x.Name == "hi");
        Assert.True(string.IsNullOrEmpty(lo.BoundColumnName));
        Assert.True(string.IsNullOrEmpty(hi.BoundColumnName));
    }

    // ── ExtractLiteralBindings: identifier <op> literal / literal <op> identifier ─

    [Fact]
    public void IdentifierEqualsStringLiteral_BindsLiteral()
    {
        var model = ParseSql("SELECT * FROM t WHERE name = 'bob'");

        var lit = Assert.Single(model.Literals);
        Assert.Equal(LiteralKind.String, lit.Kind);
        Assert.Equal("bob", lit.Value);
        Assert.Equal("name", lit.BoundColumnName);
    }

    [Fact]
    public void IdentifierEqualsNegativeNumberLiteral_BindsWithMinusSign()
    {
        var model = ParseSql("SELECT * FROM t WHERE balance = -5");

        var lit = Assert.Single(model.Literals);
        Assert.Equal(LiteralKind.Number, lit.Kind);
        Assert.Equal("-5", lit.Value);
        Assert.Equal("balance", lit.BoundColumnName);
    }

    [Fact]
    public void NumberEqualsNegativeNumber_UnaryMinusBranchRequiresLeftIdentifier()
    {
        // "5 = -3": left is a Number, not an Identifier, so the unary-minus
        // special case must not fire (and no other branch matches either).
        var model = ParseSql("SELECT * FROM t WHERE 5 = -3");

        Assert.Empty(model.Literals);
    }

    [Fact]
    public void IdentifierEqualsPlusFive_UnaryMinusBranchRequiresMinusSign()
    {
        var model = ParseSql("SELECT * FROM t WHERE a = +5");

        Assert.Empty(model.Literals);
    }

    [Fact]
    public void IdentifierEqualsMinusIdentifier_UnaryMinusBranchRequiresNumberAfterMinus()
    {
        var model = ParseSql("SELECT * FROM t WHERE a = -b");

        Assert.Empty(model.Literals);
    }

    [Fact]
    public void NumberLiteralEqualsIdentifier_BindsLiteral()
    {
        var model = ParseSql("SELECT * FROM t WHERE 5 = age");

        var lit = Assert.Single(model.Literals);
        Assert.Equal(LiteralKind.Number, lit.Kind);
        Assert.Equal("5", lit.Value);
        Assert.Equal("age", lit.BoundColumnName);
    }

    [Fact]
    public void StringLiteralEqualsIdentifier_BindsLiteral()
    {
        var model = ParseSql("SELECT * FROM t WHERE 'bob' = name");

        var lit = Assert.Single(model.Literals);
        Assert.Equal(LiteralKind.String, lit.Kind);
    }

    [Fact]
    public void ColumnComparedToColumn_NoLiteralBindingRecorded()
    {
        var model = ParseSql("SELECT * FROM t WHERE a = b");

        Assert.Empty(model.Literals);
    }

    [Fact]
    public void NumberComparedToNumber_NoLiteralBindingRecorded()
    {
        var model = ParseSql("SELECT * FROM t WHERE 5 = 10");

        Assert.Empty(model.Literals);
    }

    // ── ExtractLiteralBindings: identifier IN (literal, literal, ...) ─

    [Fact]
    public void IdentifierInLiteralList_BindsEveryItemToSameColumn()
    {
        var model = ParseSql("SELECT * FROM t WHERE status IN ('a', 'b', 5)");

        Assert.Equal(3, model.Literals.Count);
        Assert.All(model.Literals, l => Assert.Equal("status", l.BoundColumnName));
        Assert.Equal(2, model.Literals.Count(l => l.Kind == LiteralKind.String));
        Assert.Single(model.Literals, l => l.Kind == LiteralKind.Number && l.Value == "5");
    }

    [Fact]
    public void IdentifierInList_NegativeNumber_BindsWithMinusSignAsSingleItem()
    {
        var model = ParseSql("SELECT * FROM t WHERE balance IN (-5, 3)");

        Assert.Equal(2, model.Literals.Count);
        Assert.Single(model.Literals, l => l.Kind == LiteralKind.Number && l.Value == "-5");
        Assert.Single(model.Literals, l => l.Kind == LiteralKind.Number && l.Value == "3");
        // A naive off-by-one on the unary-minus lookahead would emit an
        // extra spurious "5" binding alongside the correct "-5".
        Assert.DoesNotContain(model.Literals, l => l.Value == "5");
    }

    [Fact]
    public void NumberLiteral_InLiteralList_DoesNotBind()
    {
        var model = ParseSql("SELECT * FROM t WHERE 5 IN ('a', 'b')");

        Assert.Empty(model.Literals);
    }

    [Fact]
    public void IdentifierInWithoutOpenParen_NoLiteralBindings()
    {
        var model = ParseSql("SELECT * FROM t WHERE a IN @p");

        Assert.Empty(model.Literals);
    }

    [Fact]
    public void InListSingleNegativeNumber_TerminatesCleanlyAtClosingParen_DoesNotAbsorbTrailingTokens()
    {
        // After handling the "-N" pair, the scan must land exactly on the
        // real closing ")" -- an extra un-guarded advance here would skip
        // past it undetected and keep scanning into unrelated trailing
        // tokens, sweeping up "99" as a spurious extra list item.
        var model = ParseSql("SELECT * FROM t WHERE balance IN (-5) 99");

        var lit = Assert.Single(model.Literals);
        Assert.Equal("-5", lit.Value);
    }

    [Fact]
    public void InListLiteral_ClosingParenLookingLiteral_StillCounted()
    {
        // The literal value ")" must not be confused with the actual
        // closing-paren Symbol token that terminates the scan.
        var model = ParseSql("SELECT * FROM t WHERE a IN (')', 'y')");

        Assert.Equal(2, model.Literals.Count);
        Assert.All(model.Literals, l => Assert.Equal(LiteralKind.String, l.Kind));
    }

    [Fact]
    public void InListLiteral_TerminatesAtRealClosingParen_NotBeyond()
    {
        var model = ParseSql("SELECT * FROM t WHERE status IN ('a') AND other = 5");

        var statusLits = model.Literals.Where(l => l.BoundColumnName == "status").ToList();
        var single = Assert.Single(statusLits);
        Assert.Equal("a", single.Value);
    }

    [Fact]
    public void InListLiteral_StartsScanningExactlyAtFirstListItem_NotEarlier()
    {
        // Placing a Number token exactly 3 tokens before "status" probes
        // that the list scan starts at the "(" position (i + 3), not
        // anywhere else -- an off-by-index start would pick up this
        // unrelated "5" as a spurious extra list item.
        var model = ParseSql("SELECT * FROM t WHERE 5 a b status IN ('x')");

        var lit = Assert.Single(model.Literals);
        Assert.Equal("x", lit.Value);
    }

    // ── ExtractParameterBindings: "first binding wins" across every scan pass ─

    [Fact]
    public void ParamBoundTwice_ViaInPattern_FirstOccurrenceWins()
    {
        var model = ParseSql("SELECT * FROM t WHERE a = @p AND b IN (@p)");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal("a", p.BoundColumnName);
    }

    [Fact]
    public void ParamBoundTwice_ViaLikePattern_FirstOccurrenceWins()
    {
        var model = ParseSql("SELECT * FROM t WHERE a = @p AND b LIKE @p");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal("a", p.BoundColumnName);
    }

    [Fact]
    public void ParamBoundTwice_ViaBetweenLowerPattern_FirstOccurrenceWins()
    {
        var model = ParseSql("SELECT * FROM t WHERE a = @p AND b BETWEEN @p AND @q");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal("a", p.BoundColumnName);
    }

    [Fact]
    public void ParamBoundTwice_ViaBetweenUpperPattern_FirstOccurrenceWins()
    {
        var model = ParseSql("SELECT * FROM t WHERE a = @p AND b BETWEEN @q AND @p");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal("a", p.BoundColumnName);
    }

    // ── ExtractLiteralBindings: top-of-loop comparison-operator guard ──

    [Fact]
    public void CommaBetweenIdentifierAndLiteral_NotTreatedAsComparison()
    {
        // The "," separating a projected identifier from a projected
        // literal must never be mistaken for a comparison operator -- it
        // isn't in ComparisonOperators, so the guard must reject it before
        // the identifier/literal pair either side of it gets inspected.
        var model = ParseSql("SELECT a, 'x' FROM t");

        Assert.Empty(model.Literals);
    }

    // ── FunctionKeywords: keyword-classified function-call heads ─────
    //
    // ExtractPerfHints (SqlParser.Part4.cs) treats a keyword-typed token as
    // a function-call head only when it's a member of this file's
    // FunctionKeywords set; each entry needs its own scenario since the set
    // membership check can't be exercised via any other member.

    [Theory]
    [InlineData("CAST")]
    [InlineData("COALESCE")]
    [InlineData("NULLIF")]
    [InlineData("COUNT")]
    [InlineData("SUM")]
    [InlineData("AVG")]
    [InlineData("MIN")]
    [InlineData("MAX")]
    public void KeywordFunctionHead_WrappingColumn_ComparedAfter_RecordsFunctionOnColumn(string fn)
    {
        var model = ParseSql($"SELECT * FROM t WHERE {fn}(amount) = 5");

        Assert.Contains(model.PerfHints, h =>
            h.Kind == PerfHintKind.FunctionOnColumn &&
            h.FunctionName == fn &&
            h.BoundColumnName == "amount");
    }
}
