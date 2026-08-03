using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

/// <summary>
/// A WITH file must behave exactly like its final statement. The outer model is
/// built by <c>CopyFinalStatement</c>, so every shape flag the final statement
/// sets has to survive the copy — a flag left behind is not a wrong answer but
/// a silent one, which is worse: the consuming check reads the default and
/// concludes the query does not page, does not group, and needs no scrutiny.
/// </summary>
public class FinalStatementShapeTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    [Fact]
    public void FinalStatementLimit_SetsHasRowLimitOnTheOuterModel()
    {
        var model = ParseSql(
            "with active as (select event_id from events where correlation_id = @corr) "
            + "select id, title from bookmarks where user_id = @userId limit 20 offset 40");

        Assert.True(model.HasRowLimit);
    }

    [Fact]
    public void FinalStatementGroupBy_SetsHasGroupByOnTheOuterModel()
    {
        var model = ParseSql(
            "with active as (select event_id from events where correlation_id = @corr) "
            + "select user_id, count(*) from bookmarks group by user_id");

        Assert.True(model.HasGroupBy);
    }

    [Fact]
    public void WithWhoseFinalStatementNeitherPagesNorGroups_LeavesBothFlagsClear()
    {
        // The copy must carry the flag, not set it: a copy that assigned true
        // unconditionally would pass both tests above and be useless.
        var model = ParseSql(
            "with active as (select event_id from events where correlation_id = @corr limit 5) "
            + "select id, title from bookmarks where user_id = @userId");

        Assert.False(model.HasRowLimit);
        Assert.False(model.HasGroupBy);
    }

    [Fact]
    public void CteBodyKeepsItsOwnFlags()
    {
        // The CTE that pages is the common shape, and its own model is where
        // the flag has always been recorded correctly. Pinned so the fix to
        // the outer copy cannot be mistaken for what already worked.
        var model = ParseSql(
            "with page as (select id from bookmarks where user_id = @userId order by id limit 20) "
            + "select id from page");

        var cte = Assert.Single(model.Ctes);
        Assert.True(cte.Body.HasRowLimit);
    }
}
