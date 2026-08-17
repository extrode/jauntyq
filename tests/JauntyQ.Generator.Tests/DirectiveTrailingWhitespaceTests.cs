using JauntyQ.Generator.Directives;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R79-01/-02/-03: a directive line must be honoured, or reported — never
/// both ignored and silent, and never reported with advice the author already
/// followed. These cover the three ways that promise was broken: trailing
/// whitespace on the three no-value directives, a bare <c>-- @call</c> with no
/// procedure name to call, and a tab between <c>@type</c>'s alias and dbtype.
/// </summary>
[Trait("Category", "AuditRegression")]
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

    // AUD-R79-03: alias/dbtype separator

    [Theory]
    [InlineData("-- @type total\tdecimal(10,2)\nSELECT 1 AS total")]
    [InlineData("-- @type total \t decimal(10,2)\nSELECT 1 AS total")]
    public void Type_TabSeparated_StillParses(string sql)
    {
        // A tab between alias and dbtype used to drop the directive entirely,
        // and the build then failed with JNT3005 (an Error) telling the author
        // to declare the alias with the very line they had written.
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.NotNull(directives.TypeDirectives);
        var td = Assert.Single(directives.TypeDirectives!);
        Assert.Equal("total", td.Alias);
        Assert.Equal("decimal(10,2)", td.DbType);
    }

    [Fact]
    public void Type_SpaceSeparatedMultiWord_Unchanged()
    {
        // False-positive guard: the multi-word dbtype case AUD-R9 closed must
        // survive the separator change with its interior space intact.
        var (directives, _) = DirectiveParser.Parse("-- @type avg_len double precision\nSELECT 1 AS avg_len");

        var td = Assert.Single(directives.TypeDirectives!);
        Assert.Equal("avg_len", td.Alias);
        Assert.Equal("double precision", td.DbType);
    }

    // Round 79 residual: the separator between the directive NAME and its
    // value. AUD-R79-03 fixed the tab INSIDE @type's value; the six
    // value-taking directives were still matched with a literal "@name "
    // prefix, so a tab straight after the name matched nothing and the line
    // fell through to JNT3008 "requires a value" -- accurate that the
    // directive was not applied, inaccurate that no value was present. Round
    // 79 recorded it rather than widening its own fix.

    [Fact]
    public void Type_TabAfterDirectiveName_StillParses()
    {
        var (directives, cleaned) = DirectiveParser.Parse("-- @type\ttotal decimal(10,2)\nSELECT 1 AS total");

        var td = Assert.Single(directives.TypeDirectives!);
        Assert.Equal("total", td.Alias);
        Assert.Equal("decimal(10,2)", td.DbType);
        Assert.Null(directives.SuspiciousDirectives);
        Assert.DoesNotContain("@type", cleaned);
    }

    [Fact]
    public void Result_TabAfterDirectiveName_StillParses()
    {
        var (directives, _) = DirectiveParser.Parse("-- @result\tProductSummary\nSELECT 1");

        Assert.Equal("ProductSummary", directives.ResultTypeName);
        Assert.Null(directives.SuspiciousDirectives);
    }

    [Fact]
    public void Params_TabAfterDirectiveName_StillParses()
    {
        var (directives, _) = DirectiveParser.Parse("-- @params\tid:int, name:string\nSELECT 1");

        Assert.NotNull(directives.ExplicitParams);
        Assert.Equal(2, directives.ExplicitParams!.Count);
        Assert.Null(directives.SuspiciousDirectives);
    }

    [Fact]
    public void Each_TabAfterDirectiveName_StillParses()
    {
        var (directives, _) = DirectiveParser.Parse("-- @each\tIds\nSELECT 1");

        Assert.NotNull(directives.EachParams);
        Assert.Equal("Ids", Assert.Single(directives.EachParams!));
        Assert.Null(directives.SuspiciousDirectives);
    }

    [Fact]
    public void Call_TabAfterDirectiveName_StillParses()
    {
        var (directives, _) = DirectiveParser.Parse("-- @call\tGetProductById\nSELECT 1");

        Assert.Equal("GetProductById", directives.CallProcName);
        Assert.Null(directives.SuspiciousDirectives);
    }

    [Fact]
    public void Proc_TabAfterDirectiveName_StillParses()
    {
        var (directives, _) = DirectiveParser.Parse("-- @proc\tsp_GetProduct\nSELECT 1");

        Assert.True(directives.IsProc);
        Assert.Equal("sp_GetProduct", directives.ProcName);
        Assert.Null(directives.SuspiciousDirectives);
    }

    // ── Boundaries the name/value split must not move ──

    [Fact]
    public void ProceedComment_StillNotADirective()
    {
        // The word-boundary property AUD-R8-01 established: "@proceed" is not
        // "@proc" plus a name. Splitting on the name token makes this
        // structural rather than a consequence of prefix ordering.
        var (directives, cleaned) = DirectiveParser.Parse("-- @proceed with caution\nSELECT 1");

        Assert.False(directives.IsProc);
        Assert.Null(directives.ProcName);
        Assert.Null(directives.SuspiciousDirectives);
        Assert.Contains("@proceed with caution", cleaned);
    }

    [Theory]
    [InlineData("first")]
    [InlineData("identity")]
    [InlineData("stream")]
    public void NoValueDirective_WithTrailingWords_IsNotApplied(string name)
    {
        // The three no-value directives match the whole comment body today
        // (string.Equals). "-- @first extra" is an ordinary comment, and must
        // stay one -- the split must not turn "extra" into an accepted value.
        var (directives, cleaned) = DirectiveParser.Parse($"-- @{name} extra\nSELECT 1");

        Assert.False(directives.IsFirst);
        Assert.False(directives.ReturnsIdentity);
        Assert.False(directives.IsStream);
        Assert.Contains($"@{name} extra", cleaned);
    }

    [Theory]
    [InlineData("result")]
    [InlineData("type")]
    [InlineData("each")]
    public void ValueTakingDirective_NameNotWhitespaceTerminated_ReportsSuspicious(string name)
    {
        // "-- @type(x)" matched nothing before the split and must match
        // nothing after it: the character following the name has to be
        // whitespace for the rest to be that directive's value.
        var (directives, _) = DirectiveParser.Parse($"-- @{name}(x)\nSELECT 1");

        Assert.NotNull(directives.SuspiciousDirectives);
        Assert.Contains(directives.SuspiciousDirectives!, m => m.Contains($"@{name} requires a value"));
    }
}
