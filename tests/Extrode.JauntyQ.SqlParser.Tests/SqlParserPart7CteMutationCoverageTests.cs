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
    public void CteBoundParameter_IsNotOverwrittenByTheFinalStatementsOwnBinding()
    {
        // This exercises CopyFinalStatement's OWN parameter merge (not
        // ParseWith's CTE-body merge above): the CTE body binds @id to
        // order_id first; the final statement then also uses @id, bound to
        // a DIFFERENT column (cust_id). "First binding wins" must hold here
        // too -- the final statement's own parse must not clobber a binding
        // ParseWith already merged in from the CTE body.
        var model = ParseSql(
            "with sub as (select order_id from orders where order_id = @id) " +
            "select cust_id from customers where cust_id = @id");

        var p = model.Parameters.Find(x => x.Name == "id");
        Assert.NotNull(p);
        Assert.Equal("order_id", p!.BoundColumnName);
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

    [Fact]
    public void CteBodyEndingInAParameterComparison_ParsesWithoutIndexingPastItsSlicedTokens()
    {
        // The sliced body tokens don't inherently end in the tokenizer's
        // usual End sentinel -- ParseWith must append one explicitly before
        // handing them to the sub-parser, since downstream extractors (e.g.
        // ExtractParameterBindings' lookahead scans) assume that sentinel is
        // always there. A body whose last real token is a bound parameter
        // exercises exactly that lookahead at the tail end of the list.
        var model = ParseSql("with c as (select id from t where x = @p) select 1");

        var cte = Assert.Single(model.Ctes);
        var p = Assert.Single(cte.Body.Parameters);
        Assert.Equal("p", p.Name);
        Assert.Equal("x", p.BoundColumnName);
    }

    [Fact]
    public void CteBodyEndingInABetweenClause_ParsesWithoutIndexingPastItsSlicedTokens()
    {
        // A BETWEEN clause's binding scan looks ahead several tokens past
        // its "identifier" anchor (see Part3.cs's ExtractParameterBindings),
        // so a body ending in one right at the sliced token list's tail
        // exercises the deepest lookahead this sentinel protects against.
        var model = ParseSql("with c as (select id from t where x between @lo and @hi) select 1");

        var cte = Assert.Single(model.Ctes);
        Assert.Equal(2, cte.Body.Parameters.Count);
    }

    [Fact]
    public void SecondCteWithoutALeadingComma_IsNotTreatedAsPartOfTheList()
    {
        // A completed CTE body not followed by "," must stop the CTE list
        // outright, not merely skip the "continue" and fall through to
        // re-check the loop condition -- if it did, a same-shaped
        // Identifier-then-AS-then-paren token run right after (like a
        // missing-comma second CTE) would be wrongly consumed as another
        // CTE definition instead of becoming part of the final statement.
        var model = ParseSql("with c as (select 1) d as (select 2) select 1");

        Assert.Single(model.Ctes);
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

    // ── Final statement's unsupported constructs are carried onto model ─

    [Fact]
    public void FinalStatementUnsupportedConstruct_IsCarriedOntoTheOuterModel()
    {
        var model = ParseSql(
            "with active as (select id from t) " +
            "select id into archived from t");

        Assert.Contains("SELECT INTO", model.UnsupportedConstructs);
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

    // ── Non-identifier, non-RECURSIVE token immediately after WITH ─────

    [Fact]
    public void KeywordImmediatelyAfterWith_ThatIsNeitherRecursiveNorAName_StartsNoCteAndParsesRestAsFinalStatement()
    {
        // tokens[1] is a real Keyword ("SELECT"), so the RECURSIVE check's
        // three-way AND (pos<Count && Type==Keyword && Value=="RECURSIVE")
        // must actually evaluate Value=="RECURSIVE" (false here) rather than
        // short-circuiting via an OR'd Type check -- and the CTE-name while
        // loop's Type==Identifier check must actually gate entry, not be
        // forced true by an OR, since a Keyword token would otherwise be
        // wrongly consumed as a CTE name and pos would be dragged one token
        // past where the final statement should start.
        var model = ParseSql("with select 1");

        Assert.False(model.WithRecursive);
        Assert.Empty(model.Ctes);
        // StatementType defaults to Select regardless (QueryModel's ctor),
        // so it can't distinguish this on its own: if the CTE-name loop were
        // wrongly entered (forcing "SELECT" itself to be consumed as a bogus
        // CTE name), the final statement would be re-parsed starting from
        // "1" alone, and "SELECT" -- the only token that would ever reach
        // ParseSelect and populate a projected column -- would already be
        // gone. Only Columns actually being populated proves "select 1" was
        // parsed as a real SELECT and not skipped past.
        Assert.NotEmpty(model.Columns);
    }

    // ── Boundary tokens that are the right token-type but wrong value ───

    [Fact]
    public void SymbolAfterAs_ThatIsNotAnOpeningParen_BreaksOutInsteadOfMisreadingAsBodyStart()
    {
        // tokens[pos] right after AS is a real Symbol (")"), so the body-open
        // check's Value=="(" comparison must actually run rather than being
        // OR'd away by the preceding Type==Symbol check -- otherwise a stray
        // ")" gets misread as the CTE body's opening paren and an empty/
        // garbage CTE is recorded instead of the definition breaking clean.
        var model = ParseSql("with c as ) select 1");

        Assert.Empty(model.Ctes);
    }

    // ── WITH RECURSIVE short-circuit returns immediately ────────────────

    [Fact]
    public void WithRecursive_ReturnsImmediately_NeverTreatingTheRemainingTokensAsAStatement()
    {
        // WithRecursive_Rejected (ParserTests.cs) already asserts the flag
        // and UnsupportedConstructs entry -- both are set on the lines
        // BEFORE the return, so they hold whether or not the return itself
        // executes. Only a check on what happens AFTER the return (should
        // never happen) actually distinguishes it: without it, the
        // remaining "t as (select ...) select ... from t" tokens get fed to
        // Parse() as an ordinary statement, populating Tables/Ctes from
        // fragments that were never meant to be parsed at all.
        var model = ParseSql(
            "with recursive t as (select product_id from products) select product_id from t");

        Assert.True(model.WithRecursive);
        Assert.Empty(model.Tables);
        Assert.Empty(model.Ctes);
    }

    // ── Virtual columns: alias precedence and wildcard/empty exclusion ──

    [Fact]
    public void CteVirtualColumns_UseOutputAliasWhenPresent_NotTheRawColumnName()
    {
        var model = ParseSql("with c as (select product_id as pid from products) select pid from c");

        var cte = Assert.Single(model.Ctes);
        Assert.Contains("pid", cte.VirtualColumns);
        Assert.DoesNotContain("product_id", cte.VirtualColumns);
    }

    [Fact]
    public void CteVirtualColumns_ExcludeWildcardStar()
    {
        var model = ParseSql("with c as (select * from products) select x from c");

        var cte = Assert.Single(model.Ctes);
        Assert.DoesNotContain("*", cte.VirtualColumns);
    }

    [Fact]
    public void CommaAfterCteName_LeavesNoCteRegressionCheck()
    {
        // Regression check for a stray Symbol ("," ) right after a CTE
        // name (not "("). Traced but not confirmed as a mutant kill: the
        // top-level Parse() dispatcher's tolerant one-token skip made the
        // observable result identical whether the declared-column-list
        // open check wrongly consumed "AS (select 1)" as a bogus column
        // list or correctly broke out immediately -- both leave Ctes empty
        // and exactly one projected column in the final statement.
        var model = ParseSql("with c , as (select 1) select 1");

        Assert.Empty(model.Ctes);
        Assert.Single(model.Columns);
    }

    [Fact]
    public void CommaAfterAs_WithARealParenPairLaterInTheStream_DoesNotGetMisreadAsBodyStart()
    {
        // tokens[pos] right after AS is a Symbol ("," ) that is not "(", but
        // there IS a real "(...)" pair later in the stream (inside the final
        // "select (a) from t"). If the body-open check's Value=="(" were
        // OR'd away by the Type==Symbol check alone, FindMatchingParen would
        // be wrongly invoked starting at the comma and would "accidentally"
        // match that unrelated later paren pair, fabricating a bogus CTE
        // body out of "select (" instead of breaking out cleanly.
        var model = ParseSql("with c as , select (a) from t");

        Assert.Empty(model.Ctes);
    }

    [Fact]
    public void UnclosedDeclaredColumnList_RunsToEndSentinelWithoutIndexingPastIt()
    {
        // No closing ")" ever appears, so the declared-column-list scan (and
        // the AS-keyword check right after it) must stop exactly at the
        // tokenizer's trailing End sentinel rather than reading one token
        // past it -- an off-by-one on any of these "pos < tokens.Count"
        // guards throws IndexOutOfRangeException here.
        var model = ParseSql("with c (a, b");

        Assert.Empty(model.Ctes);
    }

    [Fact]
    public void StraySymbolAfterCompleteCteBody_LeavesExactlyOneCteAndAValidFinalStatement()
    {
        // Regression check for a stray Symbol (not ",") right after a
        // completed CTE body. Traced but NOT claimed as a kill: the
        // top-level Parse() dispatch loop skips any single non-Keyword
        // token one position at a time regardless of type/value (see
        // SqlParser.cs's `else { pos++; }` fallback), so whether this ")"
        // is left in front of the final statement's tokens (break, the
        // real behavior) or silently consumed as if it were a
        // list-continuing comma (the mutated behavior) produces the same
        // observable final statement here. Kept as a plain regression
        // check, not a claimed mutant kill.
        var model = ParseSql("with c as (select 1) ) select 1");

        Assert.Single(model.Ctes);
        Assert.Equal(StatementType.Select, model.StatementType);
    }
}
