using System;
using System.Collections.Generic;
using System.Reflection;
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

    // ── Member parity, the standing check ────────────────────────────────
    //
    // CopyFinalStatement is a hand-maintained memberwise copy with no analyzer
    // enforcement, which is the same bug class the audit criteria §2.3
    // calls "the single most frequently recurring in this project's history"
    // for SchemaSimulator.Clone(). It expired exactly that way: HasRowLimit and
    // HasGroupBy were added to QueryModel and the copy was never updated, so
    // JNT8009/JNT8010 went silent on the final statement of a WITH until
    // 2026-08-03.
    //
    // Blanket-copying is NOT the fix, which is why this is a classification
    // test rather than a "copies everything" assertion: WithRecursive is set on
    // the OUTER model by ParseWith and is always false on the inner one, so
    // copying it would clobber true with false.

    /// <summary>Members CopyFinalStatement must carry from the final statement.</summary>
    private static readonly HashSet<string> Copied = new()
    {
        nameof(QueryModel.StatementType),
        nameof(QueryModel.TargetTable),
        nameof(QueryModel.Tables),
        nameof(QueryModel.Columns),
        nameof(QueryModel.Joins),
        nameof(QueryModel.Parameters),
        nameof(QueryModel.Literals),
        nameof(QueryModel.PerfHints),
        nameof(QueryModel.OrderBy),
        nameof(QueryModel.PredicateAtoms),
        nameof(QueryModel.UnsupportedConstructs),
        nameof(QueryModel.ExpressionsMissingAlias),
        nameof(QueryModel.Returning),
        nameof(QueryModel.HasReturning),
        nameof(QueryModel.HasRowLimit),
        nameof(QueryModel.HasGroupBy),
        nameof(QueryModel.Subqueries),
    };

    /// <summary>
    /// Members deliberately NOT carried, each for a stated reason:
    /// Name — the outer model already holds the query's own name, taken from the file;
    /// Ctes — attached to the outer model by ParseWith and merged, not replaced;
    /// WithRecursive — set on the OUTER model, always false on the inner one.
    /// </summary>
    private static readonly HashSet<string> DeliberatelyNotCopied = new()
    {
        nameof(QueryModel.Name),
        nameof(QueryModel.Ctes),
        nameof(QueryModel.WithRecursive),
    };

    [Fact]
    public void EveryQueryModelMember_IsClassifiedCopiedOrDeliberatelyNot()
    {
        var unclassified = new List<string>();
        foreach (var p in typeof(QueryModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (!Copied.Contains(p.Name) && !DeliberatelyNotCopied.Contains(p.Name))
                unclassified.Add(p.Name);

        Assert.True(unclassified.Count == 0,
            "QueryModel members with no CopyFinalStatement decision recorded: "
            + string.Join(", ", unclassified)
            + ". Add each to Copied (and to CopyFinalStatement) or to DeliberatelyNotCopied "
            + "with the reason. A member left out of both is the silent-drop bug this test exists for.");
    }

    [Fact]
    public void TheTwoClassificationsAreDisjointAndCoverTheWholeType()
    {
        var all = new HashSet<string>();
        foreach (var p in typeof(QueryModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            all.Add(p.Name);

        foreach (var name in Copied)
            Assert.Contains(name, all);
        foreach (var name in DeliberatelyNotCopied)
        {
            Assert.Contains(name, all);
            Assert.DoesNotContain(name, Copied);
        }

        // A stale entry left behind by a rename would otherwise sit here
        // forever, quietly shrinking what the first test checks.
        Assert.Equal(all.Count, Copied.Count + DeliberatelyNotCopied.Count);
    }
}
