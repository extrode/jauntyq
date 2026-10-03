using Extrode.JauntyQ.Generator.Directives;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class DirectiveParserMutationCoverageTests
{
    private static string DidYouMean(string word, string known) =>
        $"unrecognized directive '-- @{word}'; did you mean '-- @{known}'? The line was kept as a plain comment and had no effect.";

    [Fact]
    public void CrLfInput_CleanedSqlIsJoinedWithLf()
    {
        var (directives, cleaned) = DirectiveParser.Parse("-- @first\r\nselect 1\r\nfrom t");

        Assert.True(directives.IsFirst);
        Assert.Equal("select 1\nfrom t", cleaned);
    }

    [Fact]
    public void CommentNotStartingWithAt_IsNeverReadAsADirective()
    {
        var (directives, cleaned) = DirectiveParser.Parse("-- xfirst\nselect 1");

        Assert.False(directives.IsFirst);
        Assert.Null(directives.SuspiciousDirectives);
        Assert.Equal("-- xfirst\nselect 1", cleaned);
    }

    [Fact]
    public void RepeatedFlag_RecordsTheExactSameValueMessage()
    {
        var (directives, _) = DirectiveParser.Parse("-- @first\n-- @first\nselect 1");

        Assert.Equal(
            "-- @first appears more than once in this file with the same value; the repeat has no effect. Keep one.",
            Assert.Single(directives.DuplicateDirectives!));
    }

    [Fact]
    public void RepeatedResultWithDifferentValues_RecordsTheExactMessage()
    {
        var (directives, _) = DirectiveParser.Parse("-- @result A\n-- @result B\nselect 1");

        Assert.Equal(
            "-- @result appears more than once in this file with different values. Later lines overwrite earlier ones, " +
            "so '-- @result A' was discarded and '-- @result B' is the one in force. Keep one.",
            Assert.Single(directives.DuplicateDirectives!));
    }

    [Theory]
    [InlineData("(int Id")]
    [InlineData("Foo)")]
    public void ResultValueParenthesizedOnOneSideOnly_IsATypeName(string value)
    {
        var (directives, _) = DirectiveParser.Parse($"-- @result {value}\nselect 1");

        Assert.Equal(value, directives.ResultTypeName);
        Assert.Null(directives.InlineColumns);
    }

    [Fact]
    public void ResultInlineColumns_SetNoTypeName()
    {
        var (directives, _) = DirectiveParser.Parse("-- @result (T x, Count, int Id)\nselect 1");

        Assert.Null(directives.ResultTypeName);
        Assert.Collection(directives.InlineColumns!,
            c => { Assert.Equal("T", c.Type); Assert.Equal("x", c.Name); },
            c => { Assert.Equal("int", c.Type); Assert.Equal("Id", c.Name); });
    }

    [Fact]
    public void TypeWithoutDbType_RecordsTheExactMessage()
    {
        var (directives, _) = DirectiveParser.Parse("-- @type total\nselect 1");

        Assert.Null(directives.TypeDirectives);
        Assert.Equal(
            "-- @type total names an alias but no db type; write '-- @type total <dbtype>' (e.g. '-- @type total int'). The directive had no effect.",
            Assert.Single(directives.SuspiciousDirectives!));
    }

    [Fact]
    public void TypeDbTypeAfterSeveralSpaces_KeepsItsInnerSpace()
    {
        var (directives, _) = DirectiveParser.Parse("-- @type t  double precision\nselect 1");

        var type = Assert.Single(directives.TypeDirectives!);
        Assert.Equal("t", type.Alias);
        Assert.Equal("double precision", type.DbType);
    }

    [Fact]
    public void SuspiciousLinesFromBothChecks_AllAccumulate()
    {
        var (directives, _) = DirectiveParser.Parse("-- @frist\n-- @type total\n-- @tyep\nselect 1");

        Assert.Equal(3, directives.SuspiciousDirectives!.Count);
        Assert.Equal(DidYouMean("frist", "first"), directives.SuspiciousDirectives[0]);
        Assert.Equal(DidYouMean("tyep", "type"), directives.SuspiciousDirectives[2]);
    }

    [Fact]
    public void ParamsEntryWithEmptyName_IsDropped()
    {
        var (directives, _) = DirectiveParser.Parse("-- @params :int, id:int\nselect 1");

        var param = Assert.Single(directives.ExplicitParams!);
        Assert.Equal("id", param.Name);
        Assert.Equal("int", param.CSharpType);
    }

    [Theory]
    [InlineData("typ", "type")]
    [InlineData("firsts", "first")]
    [InlineData("indentity", "identity")]
    public void InsertionOrDeletionTypo_SuggestsTheKnownName(string word, string known)
    {
        var (directives, _) = DirectiveParser.Parse($"-- @{word}\nselect 1");

        Assert.Equal(DidYouMean(word, known), Assert.Single(directives.SuspiciousDirectives!));
    }

    [Theory]
    [InlineData("-- @first thing")]
    [InlineData("-- @-first")]
    [InlineData("-- @frxst")]
    [InlineData("-- @fxist")]
    [InlineData("-- @firstxx")]
    [InlineData("-- @")]
    [InlineData("-- @123")]
    public void NotOneEditFromAnyName_StaysAQuietComment(string line)
    {
        var (directives, cleaned) = DirectiveParser.Parse(line + "\nselect 1");

        Assert.Null(directives.SuspiciousDirectives);
        Assert.False(directives.IsFirst);
        Assert.Equal(line + "\nselect 1", cleaned);
    }
}
