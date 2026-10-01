using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class CodeEmitterSqlScanTests
{
    public static TheoryData<string, string[]> EachSplits() => new()
    {
        { "[x@Ids] in (@Ids)", new[] { "[x@Ids] in (", "@Ids", ")" } },
        { "@Ids [x", new[] { "@Ids", " [x" } },
        { "a [b@Ids] -1 or c in (@Ids)", new[] { "a [b@Ids] -1 or c in (", "@Ids", ")" } },
        { "-- @Ids\nin (@Ids)-", new[] { "-- @Ids\nin (", "@Ids", ")-" } },
        { "/* a*b @Ids */ x = a*b and c in (@Ids@Ids)", new[] { "/* a*b @Ids */ x = a*b and c in (", "@Ids", "@Ids", ")" } },
        { "/**/(@Ids) /* x", new[] { "/**/(", "@Ids", ") /* x" } },
        { "'' in (@Ids) 'x", new[] { "'' in (", "@Ids", ") 'x" } },
        { "in ('a''@Ids', \"b\"\"@Ids\", `c@Ids`, @Ids)", new[] { "in ('a''@Ids', \"b\"\"@Ids\", `c@Ids`, ", "@Ids", ")" } },
        { "in (@ids", new[] { "in (", "@Ids" } },
        { "@IdsX @Id @ @_Ids", new[] { "@IdsX @Id @ @_Ids" } },
    };

    [Theory]
    [MemberData(nameof(EachSplits))]
    public void SplitSqlForEach_ExpandsOnlyWholeEachReferencesOutsideQuotesAndComments(string sql, string[] expected)
    {
        var segments = CodeEmitter.SplitSqlForEach(sql, new() { "Ids" });

        Assert.Equal(expected, segments.ConvertAll(s => s.IsEachRef ? "@" + s.Value : s.Value));
    }

    [Theory]
    [InlineData("select 1", "select 1")]
    [InlineData("\nselect", "\n  select")]
    [InlineData("-- it's\nx", "-- it's\n  x")]
    [InlineData("-- c\n'a\nb'\nx", "-- c\n  'a\nb'\n  x")]
    [InlineData("/* a/b it's */\nx", "/* a/b it's */\n  x")]
    [InlineData("/* a * b\n */ 'x\ny'\nz", "/* a * b\n   */ 'x\ny'\n  z")]
    [InlineData("\n/* x *", "\n  /* x *")]
    [InlineData("\nx -", "\n  x -")]
    [InlineData("x - 'a\nb'", "x - 'a\nb'")]
    [InlineData("x / 'a\nb'", "x / 'a\nb'")]
    [InlineData("[a]]\nb]\nc", "[a]]\nb]\n  c")]
    [InlineData("[a]\nb", "[a]\n  b")]
    [InlineData("\"a\"\"\nb\"\n`c\nd`\ne", "\"a\"\"\nb\"\n  `c\nd`\n  e")]
    public void IndentSqlContinuationLines_PadsOnlyNewlinesOutsideLiteralsAndIdentifiers(string sql, string expected)
    {
        Assert.Equal(expected, CodeEmitter.IndentSqlContinuationLines(sql, 2));
    }
}
