using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>
/// Direct coverage for SqlParser.Part4.cs's ExtractPerfHints: the WHERE-region
/// boundary (WHERE...GROUP/ORDER/HAVING), function-on-column detection in
/// both operand positions, the leading-wildcard LIKE/NOT LIKE shapes, and
/// column-compared-to-column back-references.
/// </summary>
[Trait("Category", "AuditRegression")]
public class SqlParserPart4PerfHintMutationCoverageTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    // ── WHERE-region boundary ────────────────────────────────────────

    [Fact]
    public void NoWhereClause_NoPerfHintsCaptured()
    {
        var model = ParseSql("SELECT id FROM products");

        Assert.Empty(model.PerfHints);
    }

    [Fact]
    public void FunctionOnColumn_AfterGroupBy_IsNotScanned()
    {
        // The WHERE region ends at GROUP; a function-wrapped column that only
        // appears after GROUP BY must not be picked up as a WHERE-clause hint.
        var model = ParseSql(
            "SELECT category_id, COUNT(*) FROM products WHERE active = 1 " +
            "GROUP BY category_id HAVING UPPER(category_id) = 'X'");

        Assert.DoesNotContain(model.PerfHints, h => h.Kind == PerfHintKind.FunctionOnColumn);
    }

    // ── FunctionOnColumn: compared after vs. before vs. not at all ──────

    [Fact]
    public void FunctionWrappingColumn_ComparedAfter_IsDetected()
    {
        var model = ParseSql("SELECT id FROM users WHERE UPPER(email) = 'X'");

        Assert.Contains(model.PerfHints, h =>
            h.Kind == PerfHintKind.FunctionOnColumn &&
            h.FunctionName == "UPPER" &&
            h.BoundColumnName == "email");
    }

    [Fact]
    public void FunctionWrappingColumn_ComparedBefore_IsDetected()
    {
        var model = ParseSql("SELECT id FROM users WHERE 'X' = UPPER(email)");

        Assert.Contains(model.PerfHints, h =>
            h.Kind == PerfHintKind.FunctionOnColumn &&
            h.FunctionName == "UPPER" &&
            h.BoundColumnName == "email");
    }

    [Fact]
    public void FunctionWrappingColumn_NotComparedEitherSide_IsNotDetected()
    {
        // The function call appears in WHERE but is not itself compared --
        // e.g. it's an argument to another call. Neither the "compared after"
        // nor "compared before" branch should fire.
        var model = ParseSql("SELECT id FROM users WHERE COALESCE(UPPER(email), 'x') = 'X'");

        Assert.DoesNotContain(model.PerfHints, h =>
            h.Kind == PerfHintKind.FunctionOnColumn && h.FunctionName == "UPPER");
    }

    [Fact]
    public void FunctionCallWithNoColumnArgument_RecordsNoHint()
    {
        // The inner scan for the first Identifier argument finds none (a
        // literal-only argument), so no PerfHint is added even though the
        // call itself is compared.
        var model = ParseSql("SELECT id FROM users WHERE UPPER('literal') = 'X'");

        Assert.DoesNotContain(model.PerfHints, h => h.Kind == PerfHintKind.FunctionOnColumn);
    }

    // ── Leading-wildcard LIKE / NOT LIKE ─────────────────────────────

    [Fact]
    public void LikeWithLeadingPercent_IsDetected()
    {
        var model = ParseSql("SELECT id FROM users WHERE email LIKE '%example.com'");

        Assert.Contains(model.PerfHints, h =>
            h.Kind == PerfHintKind.LeadingWildcardLike && h.BoundColumnName == "email");
    }

    [Fact]
    public void LikeWithLeadingUnderscore_IsDetected()
    {
        var model = ParseSql("SELECT id FROM users WHERE code LIKE '_ABC'");

        Assert.Contains(model.PerfHints, h => h.Kind == PerfHintKind.LeadingWildcardLike);
    }

    [Fact]
    public void NotLikeWithLeadingWildcard_IsAlsoDetected()
    {
        var model = ParseSql("SELECT id FROM users WHERE email NOT LIKE '%example.com'");

        Assert.Contains(model.PerfHints, h =>
            h.Kind == PerfHintKind.LeadingWildcardLike && h.BoundColumnName == "email");
    }

    [Fact]
    public void LikeWithoutLeadingWildcard_IsNotDetected()
    {
        var model = ParseSql("SELECT id FROM users WHERE email LIKE 'example%'");

        Assert.DoesNotContain(model.PerfHints, h => h.Kind == PerfHintKind.LeadingWildcardLike);
    }

    [Fact]
    public void LikeWithEmptyPattern_DoesNotThrow()
    {
        // The tokens[k+1].Value.Length > 0 guard prevents indexing [0] on an
        // empty literal.
        var model = ParseSql("SELECT id FROM users WHERE email LIKE ''");

        Assert.DoesNotContain(model.PerfHints, h => h.Kind == PerfHintKind.LeadingWildcardLike);
    }

    // ── Column compared to column ────────────────────────────────────

    [Fact]
    public void ColumnComparedToColumn_RecordsBothSides()
    {
        // WHERE-clause column=column shapes surface directly, without needing
        // a subquery (an EXISTS(...) subquery's WHERE is lifted out before
        // ExtractPerfHints ever runs -- that path is covered separately).
        var model = ParseSql("SELECT a.id FROM articles a, article_tags atg WHERE atg.article_id = a.id");

        Assert.Contains(model.PerfHints, h =>
            h.Kind == PerfHintKind.ColumnComparedToColumn &&
            h.BoundTableAlias == "atg" && h.BoundColumnName == "article_id");
        Assert.Contains(model.PerfHints, h =>
            h.Kind == PerfHintKind.ColumnComparedToColumn &&
            h.BoundTableAlias == "a" && h.BoundColumnName == "id");
    }

    [Fact]
    public void ColumnComparedToLiteral_IsNotColumnComparedToColumn()
    {
        var model = ParseSql("SELECT id FROM users WHERE status = 1");

        Assert.DoesNotContain(model.PerfHints, h => h.Kind == PerfHintKind.ColumnComparedToColumn);
    }
}
