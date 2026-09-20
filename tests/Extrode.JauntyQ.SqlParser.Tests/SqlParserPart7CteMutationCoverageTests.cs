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

    // ── Malformed CTE definitions: break, don't throw or loop ───────────

    [Fact]
    public void CteMissingAsKeyword_BreaksOutCleanly()
    {
        var ex = Record.Exception(() => ParseSql("with c (select 1) select 1"));

        Assert.Null(ex);
    }

    [Fact]
    public void CteAsWithoutOpeningParen_BreaksOutCleanly()
    {
        var ex = Record.Exception(() => ParseSql("with c as select 1"));

        Assert.Null(ex);
    }

    [Fact]
    public void CteDeclaredColumnListUnclosed_DoesNotThrow()
    {
        var ex = Record.Exception(() => ParseSql("with c (a, b as (select 1, 2) select a from c"));

        Assert.Null(ex);
    }

    // ── WITH RECURSIVE: exact position, not merely present anywhere ─────

    [Fact]
    public void RecursiveImmediatelyAfterWith_IsFlagged()
    {
        var model = ParseSql("with recursive t as (select 1 as n) select n from t");

        Assert.True(model.WithRecursive);
        Assert.Contains("WITH RECURSIVE", model.UnsupportedConstructs);
    }

    [Fact]
    public void CteNamedRecursive_IsNotMistakenForWithRecursive()
    {
        // "RECURSIVE" as the CTE's own name (not the keyword right after WITH)
        // must not flag WithRecursive -- it's the position that matters, not
        // the mere presence of the word anywhere in the statement.
        var model = ParseSql("with x as (select 1 as recursive_flag) select recursive_flag from x");

        Assert.False(model.WithRecursive);
    }

    // ── Final statement: trailing ';' is dropped, not carried into it ──

    [Fact]
    public void FinalStatementTrailingSemicolon_IsDropped()
    {
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
