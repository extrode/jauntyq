using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// JNT8009 (unstable pagination) and JNT8010 (unordered pagination): a query
/// that takes a page with LIMIT/OFFSET but whose ORDER BY is not total — not
/// tiebroken down to something unique — has no defined order between equal
/// keys, so a row can appear on two pages or on none. JNT8010 is the worse
/// case: no ORDER BY at all, so the page is an arbitrary subset.
///
/// Every silence condition is pinned here, because the value of these two
/// codes is entirely in what they DON'T say: a diagnostic that fires on the
/// correct paginating queries in the sample corpus would be turned off on day
/// one and take the real check with it.
/// </summary>
public class PaginationStabilityTests
{
    /// <summary>
    /// bookmarks carries a PK (id), a non-unique index (user_id) and a unique
    /// index (slug), so all three totality outcomes are reachable. events has
    /// no index metadata at all — the snapshot-shaped stand-in for a
    /// DDL-sourced table, whose secondary unique indexes the MigrationParser
    /// drops (JauntyQGenerator.Part7.cs:97), making totality unprovable there.
    /// </summary>
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""bookmarks"": {
      ""name"": ""bookmarks"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""slug"": { ""name"": ""slug"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 60 },
        ""title"": { ""name"": ""title"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 200 },
        ""created_at"": { ""name"": ""created_at"", ""dbType"": ""timestamp"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""ix_bookmarks_user"", ""columns"": [""user_id""], ""isUnique"": false },
        { ""name"": ""ux_bookmarks_slug"", ""columns"": [""slug""], ""isUnique"": true }
      ]
    },
    ""bookmark_tags"": {
      ""name"": ""bookmark_tags"",
      ""columns"": {
        ""bookmark_id"": { ""name"": ""bookmark_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""tag_name"": { ""name"": ""tag_name"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 40 }
      },
      ""indexes"": [
        { ""name"": ""ix_bookmark_tags_bookmark"", ""columns"": [""bookmark_id""], ""isUnique"": false }
      ]
    },
    ""events"": {
      ""name"": ""events"",
      ""columns"": {
        ""event_id"": { ""name"": ""event_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""correlation_id"": { ""name"": ""correlation_id"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 40 },
        ""occurred_at"": { ""name"": ""occurred_at"", ""dbType"": ""timestamp"", ""isNullable"": false }
      },
      ""indexes"": []
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql, string schema = "")
    {
        var compilation = CSharpCompilation.Create("PaginationTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Bookmarks/TestQuery.sql", sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json",
                    string.IsNullOrEmpty(schema) ? SchemaJson : schema)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    // ── JNT8009: the ORDER BY is not total ───────────────────────────────

    [Fact]
    public void Limit_OrderByNonUniqueColumn_JNT8009()
    {
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "order by bookmarks.created_at desc\n" +
            "limit 20 offset 40");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8009");
    }

    [Fact]
    public void OffsetAlone_OrderByNonUniqueColumn_JNT8009()
    {
        // T-SQL paging: OFFSET n ROWS FETCH NEXT m ROWS ONLY. Only OFFSET is a
        // tokenizer keyword — FETCH/NEXT/ROWS/ONLY are not — so OFFSET alone
        // has to be what marks the statement as taking a page.
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "order by bookmarks.created_at desc\n" +
            "offset 40 rows fetch next 20 rows only");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8009");
    }

    [Fact]
    public void CteWithoutTiebreak_JNT8009()
    {
        // The pagination is INSIDE the CTE, which is the shape the consumer
        // actually writes. A top-level-only check would see page.created_at —
        // a CTE virtual column that resolves to nothing — and stay silent on
        // the one case this diagnostic exists for.
        var result = Run(
            "with page as (\n" +
            "    select bookmarks.id, bookmarks.title, bookmarks.created_at\n" +
            "    from bookmarks\n" +
            "    where bookmarks.user_id = @userId\n" +
            "    order by bookmarks.created_at desc\n" +
            "    limit 20 offset 40\n" +
            ")\n" +
            "select page.id, page.title, page.created_at\n" +
            "from page");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8009");
    }

    // ── JNT8010: no ORDER BY at all ──────────────────────────────────────

    [Fact]
    public void Limit_NoOrderBy_JNT8010()
    {
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "limit 20 offset 40");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8010");
    }

    [Fact]
    public void Limit_NoOrderBy_DoesNotAlsoReportJNT8009()
    {
        // The two codes are mutually exclusive by construction: 8009 is the
        // ORDER-BY-present arm. Both firing would double-report one defect.
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "limit 20 offset 40");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009");
    }

    [Fact]
    public void NoIndexMetadata_NoOrderBy_StillJNT8010()
    {
        // The unordered arm needs no uniqueness reasoning, so unlike JNT8009 it
        // still works for a table whose secondary indexes were never captured.
        var result = Run(
            "select events.event_id, events.correlation_id\n" +
            "from events\n" +
            "limit 20");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8010");
    }

    // ── Silence: the sort IS total ───────────────────────────────────────

    [Fact]
    public void Limit_OrderByWithPrimaryKeyTiebreak_Silent()
    {
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "order by bookmarks.created_at desc, bookmarks.id desc\n" +
            "limit 20 offset 40");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009" || d.Id == "JNT8010");
    }

    [Fact]
    public void CteWithPrimaryKeyTiebreak_Silent()
    {
        // The consumer's Public/ListPage.sql, reduced to the CTE that matters.
        var result = Run(
            "with page as (\n" +
            "    select bookmarks.id, bookmarks.title, bookmarks.created_at\n" +
            "    from bookmarks\n" +
            "    where bookmarks.user_id = @userId\n" +
            "    order by bookmarks.created_at desc, bookmarks.id desc\n" +
            "    limit 20 offset 40\n" +
            ")\n" +
            "select page.id, page.title, page.created_at\n" +
            "from page");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009" || d.Id == "JNT8010");
    }

    [Fact]
    public void Limit_OrderByCompleteUniqueIndex_Silent()
    {
        // slug carries a complete, non-prefix, non-expression unique index, so
        // ordering by it alone is already total — no PK tiebreak needed.
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "order by bookmarks.slug\n" +
            "limit 20 offset 40");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009");
    }

    [Fact]
    public void Limit_ProjectedAliasBesidePrimaryKeyTiebreak_Silent()
    {
        // SearchPage.sql's shape: a projected alias leads the sort, plain
        // columns follow. The PK is present among the plain items, so the
        // covers-a-unique-key test has to run BEFORE the "an item isn't a
        // plain column, so don't reason" bail — otherwise this reports.
        var result = Run(
            "select bookmarks.id, bookmarks.title, length(bookmarks.title) as weight\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "order by weight desc, bookmarks.created_at desc, bookmarks.id desc\n" +
            "limit 20 offset 40");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009");
    }

    // ── Silence: we cannot prove instability ─────────────────────────────

    [Fact]
    public void NoIndexMetadata_NonPrimaryKeyTiebreak_Silent()
    {
        // events has no captured indexes, so a unique index on correlation_id
        // would be invisible: totality is UNPROVABLE, not absent. Firing here
        // would be a false positive on every DDL-sourced project.
        var result = Run(
            "select events.event_id, events.correlation_id\n" +
            "from events\n" +
            "order by events.occurred_at desc, events.correlation_id\n" +
            "limit 20");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009");
    }

    [Fact]
    public void Limit_OrderByExpressionOnly_Silent()
    {
        // An expression item may carry uniqueness we cannot see. Don't guess.
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "order by length(bookmarks.title) desc\n" +
            "limit 20 offset 40");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009");
    }

    // ── Silence: out of the v1 scope ─────────────────────────────────────

    [Fact]
    public void Limit_WithJoin_Silent()
    {
        // A unique key on the driving table does not make a fanned-out join's
        // result total, so the inference doesn't hold and v1 declines it.
        //
        // The join is on title deliberately, NOT on the primary key: an
        // equijoin against bookmarks.id would already pin the row through the
        // one-row-seek gate above, and this test would then pass no matter what
        // the join gate did.
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "join bookmark_tags on bookmark_tags.tag_name = bookmarks.title\n" +
            "where bookmarks.user_id = @userId\n" +
            "order by bookmarks.created_at desc\n" +
            "limit 20 offset 40");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009" || d.Id == "JNT8010");
    }

    [Fact]
    public void Limit_WithGroupBy_Silent()
    {
        // GROUP BY collapses rows, so per-row uniqueness says nothing about the
        // uniqueness of the grouped result.
        var result = Run(
            "select bookmarks.user_id, count(bookmarks.id) as total\n" +
            "from bookmarks\n" +
            "group by bookmarks.user_id\n" +
            "order by bookmarks.user_id\n" +
            "limit 20 offset 40");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009" || d.Id == "JNT8010");
    }

    [Fact]
    public void PredicateSubqueryLimitOne_Silent()
    {
        // LIMIT 1 inside EXISTS/IN is idiomatic and cannot be unstable — the
        // subquery's row identity never reaches the caller.
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.id in (select bookmark_tags.bookmark_id from bookmark_tags limit 1)\n" +
            "order by bookmarks.created_at desc, bookmarks.id desc");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009" || d.Id == "JNT8010");
    }

    [Fact]
    public void PrimaryKeyEqualityFilter_NoOrderBy_Silent()
    {
        // Already filtered to at most one row: however it is "ordered", the
        // result is the same row every time. This is the -- @first idiom.
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.id = @id\n" +
            "limit 1");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009" || d.Id == "JNT8010");
    }

    [Fact]
    public void SqlServerTopWithoutOffset_Silent()
    {
        // TOP n with no OFFSET is not pagination: it takes the head of the
        // result once, so there are no page boundaries for a row to fall
        // between. (It is also discarded by SkipProjectionModifiers, so the IR
        // never sees it — this pins that staying true.)
        var result = Run(
            "select top 10 bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "order by bookmarks.created_at desc",
            SchemaJson.Replace(@"""dialect"": ""postgres""", @"""dialect"": ""sqlserver"""));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009" || d.Id == "JNT8010");
    }

    [Fact]
    public void NoRowLimit_UnorderedQuery_Silent()
    {
        // No LIMIT/OFFSET: an unordered SELECT returns every row, so there is
        // no page for rows to fall between. Neither code applies.
        var result = Run(
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009" || d.Id == "JNT8010");
    }

    // ── The page taken by the FINAL statement of a WITH ───────────────────

    [Fact]
    public void FinalStatementLimit_NoOrderBy_JNT8010()
    {
        // The CTE computes something unrelated and the page is taken by the
        // final statement, straight off a real table. Every gate that guards
        // these codes is satisfied, so silence here can only mean the outer
        // model never learned the final statement pages at all.
        var result = Run(
            "with active as (\n" +
            "    select events.event_id from events where events.correlation_id = @corr\n" +
            ")\n" +
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "limit 20 offset 40");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8010");
    }

    [Fact]
    public void FinalStatementLimit_UntiebrokenOrderBy_JNT8009()
    {
        var result = Run(
            "with active as (\n" +
            "    select events.event_id from events where events.correlation_id = @corr\n" +
            ")\n" +
            "select bookmarks.id, bookmarks.title\n" +
            "from bookmarks\n" +
            "where bookmarks.user_id = @userId\n" +
            "order by bookmarks.created_at desc\n" +
            "limit 20 offset 40");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8009");
    }

    [Fact]
    public void FinalStatementLimit_GroupBy_Silent()
    {
        // GROUP BY collapses rows, so the unique key on the base table proves
        // nothing about the result's identity and both codes must stay quiet.
        // This pins the pair: carrying HasRowLimit across without HasGroupBy
        // turns every grouped page in a WITH into a false JNT8010.
        var result = Run(
            "with active as (\n" +
            "    select events.event_id from events where events.correlation_id = @corr\n" +
            ")\n" +
            "select bookmarks.user_id, count(*) as total\n" +
            "from bookmarks\n" +
            "group by bookmarks.user_id\n" +
            "limit 20");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8009" || d.Id == "JNT8010");
    }

}
