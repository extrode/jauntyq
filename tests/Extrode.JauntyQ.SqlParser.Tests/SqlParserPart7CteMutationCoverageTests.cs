using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>
/// Direct coverage for SqlParser.Part7.cs's ParseWith: the CTE body's
/// parameter binding merge onto the outer model (new vs. already-bound vs.
/// unbound-existing), malformed CTE definitions breaking out cleanly, and the
/// final statement's trailing-';' handling.
/// </summary>
[Trait("Category", "AuditRegression")]
public class SqlParserPart7CteMutationCoverageTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    // ── Parameter merge onto the outer model ────────────────────────────

    [Fact]
    public void CteOnlyParameter_IsAddedFreshToOuterModel()
    {
        // The "existing == null" branch this test's name refers to
        // (SqlParser.Part7.cs:78) is unreachable through Parse: the top-level
        // token scan (SqlParser.cs:14-22) registers every @parameter it sees
        // -- including ones inside CTE bodies, since it scans the whole raw
        // stream before ParseWith ever runs -- so by the time this merge loop
        // runs, `existing` is never null. This test still exercises real,
        // correct behavior (the merge's `else if` carry-binding branch), just
        // not the branch its name claims.
        var model = ParseSql(
            "with recent as (select id from orders where status = @status) select id from recent");

        var p = Assert.Single(model.Parameters);
        Assert.Equal("status", p.Name);
        Assert.Equal("=", p.ComparisonOp);
        Assert.Equal("status", p.BoundColumnName);
    }

    [Fact]
    public void OuterUnboundParameter_IsCarriedTheCteBodysBinding()
    {
        // The outer top-level token scan registers every @param it sees but
        // leaves it unbound; when the CTE body binds the SAME name to a
        // column, that binding must be carried onto the outer, not discarded.
        var model = ParseSql(
            "with recent as (select id from orders where status = @status) " +
            "select id from recent where @status = @status");

        var p = model.Parameters.Find(x => x.Name == "status");
        Assert.NotNull(p);
        Assert.Equal("status", p!.BoundColumnName);
    }

    [Fact]
    public void OuterAlreadyBoundParameter_IsNotOverwrittenByTheCteBody()
    {
        // The outer statement already binds @id to orders.id; a same-named
        // parameter used unbound-ly inside an unrelated CTE body must not
        // clobber that existing binding.
        var model = ParseSql(
            "with lookup as (select @id as echoed) " +
            "select id from orders where id = @id");

        var p = model.Parameters.Find(x => x.Name == "id");
        Assert.NotNull(p);
        Assert.Equal("id", p!.BoundColumnName);
    }

    [Fact]
    public void FirstCteBindingWins_SecondCteBodyDoesNotOverwriteIt()
    {
        // The test above binds @id unbound in one CTE and bound in the outer
        // statement -- both halves of the merge guard (Part7.cs:89) still
        // evaluate false-vs-false or true-vs-true the same way for that shape.
        // The guard's actual job only shows up with two CTEs binding the SAME
        // parameter to DIFFERENT columns: the first CTE body to run binds
        // @id, so by the time the second CTE's body merges, `existing` is
        // already non-empty and its binding must not be replaced.
        var model = ParseSql(
            "with a as (select order_id from orders where order_id = @id), " +
            "b as (select cust_id from customers where cust_id = @id) " +
            "select a.order_id from a, b");

        var p = model.Parameters.Find(x => x.Name == "id");
        Assert.NotNull(p);
        Assert.Equal("order_id", p!.BoundColumnName);
    }

    // ── Malformed CTE definitions: break, don't throw or loop ───────────

    [Fact]
    public void CteMissingAsKeyword_BreaksOutCleanly()
    {
        // A malformed CTE never reaches `model.Ctes.Add`, so break-out leaves
        // Ctes empty; the remaining tokens ("select 1") are still parsed as
        // the final statement. Asserting only "no throw" would pass even if
        // the break landed at the wrong token position.
        var model = ParseSql("with c (select 1) select 1");

        Assert.Empty(model.Ctes);
        Assert.Equal(StatementType.Select, model.StatementType);
    }

    [Fact]
    public void CteAsWithoutOpeningParen_BreaksOutCleanly()
    {
        var model = ParseSql("with c as select 1");

        Assert.Empty(model.Ctes);
        Assert.Equal(StatementType.Select, model.StatementType);
    }

    [Fact]
    public void CteDeclaredColumnListUnclosed_DoesNotThrow()
    {
        // The declared-column-list scan doesn't track paren depth, so it stops
        // at the CTE body's OWN closing paren (from "(select 1, 2)"), then
        // fails the AS-keyword check on the second "select" and breaks. The
        // remaining tokens ("select a from c") still parse as a real final
        // statement referencing column "a" and table "c" -- not just "didn't
        // throw".
        var model = ParseSql("with c (a, b as (select 1, 2) select a from c");

        Assert.Empty(model.Ctes);
        Assert.Equal(StatementType.Select, model.StatementType);
        Assert.Contains(model.Tables, t => t.TableName == "c");
        Assert.Contains(model.Columns, c => c.ColumnName == "a");
    }

    // ── WITH RECURSIVE: exact position, not merely present anywhere ─────

    // RecursiveImmediatelyAfterWith_IsFlagged duplicated ParserTests.cs's
    // WithRecursive_Rejected exactly (same input, same assertions, no added
    // kill power) -- removed.

    [Fact]
    public void RecursiveKeyword_AppearingLaterInTheStream_DoesNotFlagWithRecursive()
    {
        // "recursive_flag" (a prior version of this test) is a single
        // Identifier token, not an occurrence of the RECURSIVE keyword at all
        // -- SqlTokenizer.cs registers "RECURSIVE" as a whole-word keyword, so
        // it never matches as a substring, and that version added no real
        // coverage of "position matters, not mere presence". A genuine
        // Keyword-typed RECURSIVE token elsewhere in the stream needs a
        // context where the tokenizer still classifies it that way but the
        // parser reads it somewhere other than position 1: here, as a would-be
        // second CTE's name. Since RECURSIVE tokenizes as Keyword, not
        // Identifier, the CTE-list while-loop's Identifier check ends the loop
        // there instead of treating it as a new CTE.
        var model = ParseSql("with a as (select 1), recursive as (select 1) select 1");

        Assert.False(model.WithRecursive);
    }

    // ── Final statement: trailing ';' is dropped, not carried into it ──

    [Fact]
    public void FinalStatementTrailingSemicolon_IsDropped()
    {
        // Traced this looking for a shape where the strip is actually
        // observable and could not find one: the OUTER top-level
        // DetectMultipleStatements pass runs on the ORIGINAL tokens (WITH
        // clause included) before ParseWith is ever called, so any ';' with
        // real content after it -- e.g. "with c as (...); select ..." -- is
        // already classified MULTI_STATEMENT by that outer pass regardless of
        // what ParseWith's own final-statement re-parse does. And for a
        // solitary trailing ';' with nothing after it (this shape), both the
        // outer pass and the nested re-parse stop cleanly at ';' exactly as
        // they would at the End sentinel either way. This mutant looks like a
        // genuinely equivalent one for any input reachable through the public
        // Parse entry point; kept as a plain regression check, not a claimed
        // kill.
        var model = ParseSql("with c as (select id from orders) select id from c;");

        Assert.Equal(StatementType.Select, model.StatementType);
        Assert.Single(model.Ctes);
    }

    [Fact]
    public void FinalStatementWithoutTrailingSemicolon_StillParsesCorrectly()
    {
        var model = ParseSql("with c as (select id from orders) select id from c");

        Assert.Equal(StatementType.Select, model.StatementType);
    }
}
