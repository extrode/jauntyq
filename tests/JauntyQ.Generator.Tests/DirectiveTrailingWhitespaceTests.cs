using JauntyQ.Generator.Directives;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R79-01/-02: a directive line must be honoured, or reported — never
/// both ignored and silent. These cover the two ways that promise was broken:
/// trailing whitespace on the three no-value directives, and a bare
/// <c>-- @call</c> with no procedure name to call.
/// </summary>
public class DirectiveTrailingWhitespaceTests
{
    // ── AUD-R79-01: trailing whitespace on no-value directives ──

    [Theory]
    [InlineData("-- @first \nSELECT ProductId FROM Products")]
    [InlineData("-- @first\t\nSELECT ProductId FROM Products")]
    [InlineData("-- @first   \nSELECT ProductId FROM Products")]
    public void First_TrailingWhitespace_StillSetsIsFirst(string sql)
    {
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.True(directives.IsFirst);
        Assert.DoesNotContain("@first", cleaned);
    }

    [Theory]
    [InlineData("-- @identity \nINSERT INTO Products (Name) VALUES (@Name)")]
    [InlineData("-- @identity\t\nINSERT INTO Products (Name) VALUES (@Name)")]
    public void Identity_TrailingWhitespace_StillSetsReturnsIdentity(string sql)
    {
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.True(directives.ReturnsIdentity);
        Assert.DoesNotContain("@identity", cleaned);
    }

    [Theory]
    [InlineData("-- @stream \nSELECT ProductId FROM Products")]
    [InlineData("-- @stream\t\nSELECT ProductId FROM Products")]
    public void Stream_TrailingWhitespace_StillSetsIsStream(string sql)
    {
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.True(directives.IsStream);
        Assert.DoesNotContain("@stream", cleaned);
    }

    [Fact]
    public void First_TrailingSpace_IsNotSilent()
    {
        // The invariant behind the fix, stated independently of which way it
        // is satisfied: a directive is applied or it is reported.
        var (directives, _) = DirectiveParser.Parse("-- @first \nSELECT ProductId FROM Products");

        Assert.True(directives.IsFirst || directives.SuspiciousDirectives is { Count: > 0 });
    }

    [Fact]
    public void Proc_TrailingSpace_StillSetsIsProcWithoutName()
    {
        // Bare @proc is meaningful (the name is synthesized), and a padded
        // bare @proc must stay bare rather than acquiring a whitespace name.
        var (directives, _) = DirectiveParser.Parse("-- @proc  \nSELECT ProductId FROM Products");

        Assert.True(directives.IsProc);
        Assert.Null(directives.ProcName);
    }

    // ── AUD-R79-01 second half: padded bare value-taking directives ──

    [Theory]
    [InlineData("result")]
    [InlineData("params")]
    [InlineData("type")]
    [InlineData("each")]
    public void ValueTakingDirective_BareButPadded_ReportsSuspicious(string name)
    {
        // "-- @result   " used to match the "@result " prefix and hand the
        // parser an empty value, setting an empty ResultTypeName in silence.
        var (directives, _) = DirectiveParser.Parse($"-- @{name}   \nSELECT ProductId FROM Products");

        Assert.NotNull(directives.SuspiciousDirectives);
        Assert.Contains(directives.SuspiciousDirectives!, m => m.Contains($"@{name} requires a value"));
    }

    [Fact]
    public void ResultBareButPadded_DoesNotSetEmptyResultTypeName()
    {
        var (directives, _) = DirectiveParser.Parse("-- @result   \nSELECT ProductId FROM Products");

        Assert.Null(directives.ResultTypeName);
    }

    // ── AUD-R79-02: bare @call ──

    [Theory]
    [InlineData("-- @call\nSELECT 1")]
    [InlineData("-- @call \nSELECT 1")]
    [InlineData("-- @call\t\nSELECT 1")]
    public void Call_WithoutProcName_ReportsSuspicious(string sql)
    {
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.Null(directives.CallProcName);
        Assert.NotNull(directives.SuspiciousDirectives);
        Assert.Contains(directives.SuspiciousDirectives!, m => m.Contains("@call requires a value"));
    }

    [Fact]
    public void Call_WithProcName_StillParsesAndIsStripped()
    {
        // The false-positive boundary for AUD-R79-02: the ordinary form must
        // be untouched by the removal of the bare-@call acceptance.
        var (directives, cleaned) = DirectiveParser.Parse("-- @call GetProductById\nSELECT 1");

        Assert.Equal("GetProductById", directives.CallProcName);
        Assert.Null(directives.SuspiciousDirectives);
        Assert.DoesNotContain("@call", cleaned);
    }

    [Fact]
    public void Call_WithProcNameAndTrailingSpace_StillParses()
    {
        var (directives, _) = DirectiveParser.Parse("-- @call GetProductById \nSELECT 1");

        Assert.Equal("GetProductById", directives.CallProcName);
    }

    [Fact]
    public void CallerComment_StillNotADirective()
    {
        // Word-boundary rule preserved: "@caller" is neither a @call nor,
        // being more than one edit from any directive name, a near miss.
        var (directives, cleaned) = DirectiveParser.Parse("-- @caller must hold a lock\nSELECT 1");

        Assert.Null(directives.CallProcName);
        Assert.Null(directives.SuspiciousDirectives);
        Assert.Contains("@caller must hold a lock", cleaned);
    }
}
