using System.Linq;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class SqlParserPart2MutationCoverageTests
{
    private static QueryModel ParseSql(string sql)
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), "q");

    private static QueryModel ParseTokens(params (TokenType Type, string Value)[] tokens)
        => SqlParser.Parse(tokens.Select(t => new Token(t.Type, t.Value)).ToList(), "q");

    private static (TokenType, string) K(string v) => (TokenType.Keyword, v);
    private static (TokenType, string) I(string v) => (TokenType.Identifier, v);
    private static (TokenType, string) S(string v) => (TokenType.Symbol, v);
    private static (TokenType, string) L(string v) => (TokenType.Literal, v);
    private static readonly (TokenType, string) End = (TokenType.End, string.Empty);

    private static string[] TableNames(QueryModel m) => m.Tables.Select(t => t.TableName).ToArray();

    private static string[] JoinPairs(QueryModel m)
        => m.Joins.Select(j => $"{j.LeftTable}.{j.LeftColumn}={j.RightTable}.{j.RightColumn}").ToArray();

    [Fact]
    public void FromAsAlias_FollowedByCommaTable_RegistersBothTables()
    {
        var model = ParseSql("SELECT a FROM t AS x, u");

        Assert.Equal(2, model.Tables.Count);
        Assert.Equal("t", model.Tables[0].TableName);
        Assert.Equal("x", model.Tables[0].Alias);
        Assert.Equal("u", model.Tables[1].TableName);
        Assert.Empty(model.Tables[1].Alias);
    }

    [Fact]
    public void FromAsFollowedByKeyword_RecordsNoAlias()
    {
        var model = ParseSql("SELECT a FROM t AS WHERE x = 1");

        var table = Assert.Single(model.Tables);
        Assert.Equal("t", table.TableName);
        Assert.Empty(table.Alias);
    }

    [Fact]
    public void FromTableFollowedByWhere_RecordsNoAlias()
    {
        var model = ParseSql("SELECT a FROM t WHERE x = 1");

        var table = Assert.Single(model.Tables);
        Assert.Equal("t", table.TableName);
        Assert.Empty(table.Alias);
    }

    [Fact]
    public void FromWithNothingAfterIt_RecordsNoTable()
    {
        Assert.Empty(ParseSql("SELECT 1 FROM").Tables);
    }

    [Fact]
    public void FromTableFunctionCall_DoesNotRegisterItsArgumentAsATable()
    {
        Assert.Equal(new[] { "f" }, TableNames(ParseSql("SELECT a FROM f(x)")));
    }

    [Fact]
    public void FromAliasFollowedByNonCommaTokenSpelledComma_StopsTheTableList()
    {
        var model = ParseTokens(K("SELECT"), I("a"), K("FROM"), I("t"), I("x"), L(","), I("u"), End);

        Assert.Equal(new[] { "t" }, TableNames(model));
    }

    [Fact]
    public void FromAliasFollowedByAnotherIdentifier_DoesNotRegisterItAsATable()
    {
        var model = ParseSql("SELECT a FROM t x NATURAL JOIN u");

        Assert.Equal(new[] { "t", "u" }, TableNames(model));
        Assert.Equal("x", model.Tables[0].Alias);
    }

    [Fact]
    public void JoinKeywordsFollowedByNonJoinKeyword_StopAndRecordNoJoinedTable()
    {
        var model = ParseSql("SELECT a FROM t LEFT JOIN WHERE x = 1");

        Assert.Equal(new[] { "t" }, TableNames(model));
    }

    [Fact]
    public void JoinWithNothingAfterIt_RecordsNoJoinedTable()
    {
        Assert.Equal(new[] { "t" }, TableNames(ParseSql("SELECT a FROM t JOIN")));
    }

    [Fact]
    public void JoinFollowedByIdentifierSpelledLikeAJoinKeyword_TreatsItAsTheTableName()
    {
        var model = ParseTokens(K("SELECT"), I("a"), K("FROM"), I("t"), K("JOIN"), I("LEFT"), End);

        Assert.Equal(new[] { "t", "LEFT" }, TableNames(model));
        Assert.Equal(JoinKind.None, model.Tables[1].Join);
    }

    [Fact]
    public void JoinedTableFollowedByNonKeywordSpelledAs_RecordsNoAlias()
    {
        var model = ParseTokens(K("SELECT"), I("a"), K("FROM"), I("t"), K("JOIN"), I("u"), L("AS"), I("x"), End);

        Assert.Equal(new[] { "t", "u" }, TableNames(model));
        Assert.Empty(model.Tables[1].Alias);
    }

    [Fact]
    public void JoinAsFollowedByOn_RecordsNoAliasAndStillParsesTheCondition()
    {
        var model = ParseSql("SELECT a FROM t JOIN u AS ON t.id = u.id");

        Assert.Empty(model.Tables[1].Alias);
        Assert.Equal(new[] { "t.id=u.id" }, JoinPairs(model));
    }

    [Fact]
    public void JoinAsAlias_ThenOnCondition_CapturesBoth()
    {
        var model = ParseSql("SELECT a FROM t JOIN u AS v ON t.id = v.id");

        Assert.Equal("v", model.Tables[1].Alias);
        Assert.Equal(new[] { "t.id=v.id" }, JoinPairs(model));
    }

    [Fact]
    public void JoinedTableFollowedByWhere_ParsesNoJoinCondition()
    {
        Assert.Empty(ParseSql("SELECT a FROM t JOIN u WHERE a = b").Joins);
    }

    [Fact]
    public void JoinedTableFollowedByNonKeywordSpelledOn_ParsesNoJoinCondition()
    {
        var model = ParseTokens(K("SELECT"), I("a"), K("FROM"), I("t"), K("JOIN"), I("u"),
            L("ON"), I("a"), S("="), I("b"), End);

        Assert.Empty(model.Joins);
    }

    [Fact]
    public void JoinedTableFollowedByNonIdentifierSpelledUsing_ParsesNoUsingClause()
    {
        var model = ParseTokens(K("SELECT"), I("a"), K("FROM"), I("t"), K("JOIN"), I("u"),
            L("USING"), S("("), I("id"), S(")"), End);

        Assert.Empty(model.Joins);
    }

    [Fact]
    public void UsingWithOnlyOneTableInScope_RecordsNoJoin()
    {
        var model = ParseSql("SELECT a JOIN u USING (id)");

        Assert.Equal(new[] { "u" }, TableNames(model));
        Assert.Empty(model.Joins);
    }

    [Fact]
    public void UsingWithoutOpeningParen_RecordsNoJoin()
    {
        Assert.Empty(ParseSql("SELECT a FROM t JOIN u USING , id").Joins);
    }

    [Fact]
    public void UsingFollowedByNonSymbolSpelledOpenParen_RecordsNoJoin()
    {
        var model = ParseTokens(K("SELECT"), I("a"), K("FROM"), I("t"), K("JOIN"), I("u"),
            I("USING"), L("("), I("id"), S(")"), End);

        Assert.Empty(model.Joins);
    }

    [Fact]
    public void UsingListWithoutClosingParen_StopsAtTheEndOfInput()
    {
        Assert.Equal(new[] { "t.id=u.id" }, JoinPairs(ParseSql("SELECT a FROM t JOIN u USING (id")));
    }

    [Fact]
    public void UsingList_StopsAtItsClosingParen()
    {
        var model = ParseSql("SELECT a FROM t JOIN u USING (id) WHERE x = 1");

        Assert.Equal(new[] { "t.id=u.id" }, JoinPairs(model));
    }

    [Fact]
    public void UsingList_ResumesTheOuterParseAfterItsClosingParen()
    {
        var model = ParseSql("SELECT a FROM t JOIN u USING (id, LIMIT)");

        Assert.Equal(new[] { "t.id=u.id" }, JoinPairs(model));
        Assert.False(model.HasRowLimit);
    }

    [Fact]
    public void OnConditionAgainstALiteral_RecordsNoJoin()
    {
        Assert.Empty(ParseSql("SELECT a FROM t JOIN u ON t.id = 1").Joins);
    }

    [Theory]
    [InlineData("SELECT a FROM t JOIN u ON t.a = u.a OR t.b = u.b")]
    [InlineData("SELECT a FROM t JOIN u ON t.a = u.a AND 1 = u.b")]
    [InlineData("SELECT a FROM t JOIN u ON t.a = u.a AND t.b > u.b")]
    [InlineData("SELECT a FROM t JOIN u ON t.a = u.a AND t.b = 1")]
    public void OnConditionTail_ThatIsNotAnAndedColumnEquality_IsNotRecorded(string sql)
    {
        Assert.Equal(new[] { "t.a=u.a" }, JoinPairs(ParseSql(sql)));
    }

    [Fact]
    public void OnConditionTail_WithNonSymbolSpelledEquals_IsNotRecorded()
    {
        var model = ParseTokens(K("SELECT"), I("a"), K("FROM"), I("t"), K("JOIN"), I("u"), K("ON"),
            I("t.a"), S("="), I("u.a"), K("AND"), I("t.b"), I("="), I("u.b"), End);

        Assert.Equal(new[] { "t.a=u.a" }, JoinPairs(model));
    }

    public static TheoryData<string[], string[], string[]> ListsWithoutEndMarker => new()
    {
        { new[] { "K:SELECT", "I:a", "K:FROM" }, new string[0], new string[0] },
        { new[] { "K:SELECT", "I:a", "K:FROM", "I:t" }, new[] { "t" }, new string[0] },
        { new[] { "K:SELECT", "I:a", "K:FROM", "I:t", "K:AS" }, new[] { "t" }, new string[0] },
        { new[] { "K:SELECT", "I:a", "K:FROM", "I:t", "K:JOIN" }, new[] { "t" }, new string[0] },
        { new[] { "K:SELECT", "I:a", "K:FROM", "I:t", "K:CROSS", "K:JOIN" }, new[] { "t" }, new string[0] },
        { new[] { "K:SELECT", "I:a", "K:FROM", "I:t", "K:JOIN", "I:u" }, new[] { "t", "u" }, new string[0] },
        { new[] { "K:SELECT", "I:a", "K:FROM", "I:t", "K:JOIN", "I:u", "K:AS" }, new[] { "t", "u" }, new string[0] },
        { new[] { "K:SELECT", "I:a", "K:FROM", "I:t", "K:JOIN", "I:u", "I:USING" }, new[] { "t", "u" }, new string[0] },
        { new[] { "K:SELECT", "I:a", "K:FROM", "I:t", "K:JOIN", "I:u", "I:USING", "S:(", "I:id" }, new[] { "t", "u" }, new[] { "t.id=u.id" } },
        { new[] { "K:SELECT", "I:a", "K:FROM", "I:t", "K:JOIN", "I:u", "K:ON", "I:t.a", "S:=" }, new[] { "t", "u" }, new string[0] },
        { new[] { "K:SELECT", "I:a", "K:FROM", "I:t", "K:JOIN", "I:u", "K:ON", "I:t.a", "S:=", "I:u.a", "K:AND", "I:t.b", "S:=" }, new[] { "t", "u" }, new[] { "t.a=u.a" } },
    };

    [Theory]
    [MemberData(nameof(ListsWithoutEndMarker))]
    public void TokenListWithoutEndMarker_ParsesUpToItsLastTokenWithoutReadingPastIt(
        string[] tokens, string[] expectedTables, string[] expectedJoins)
    {
        var list = tokens.Select(t => new Token(
            t[0] switch { 'K' => TokenType.Keyword, 'I' => TokenType.Identifier, _ => TokenType.Symbol },
            t.Substring(2))).ToList();

        var model = SqlParser.Parse(list, "q");

        Assert.Equal(expectedTables, TableNames(model));
        Assert.Equal(expectedJoins, JoinPairs(model));
    }
}
