using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

/// <summary>
/// WHERE-clause capture into <see cref="QueryModel.PredicateAtoms"/>: the
/// clause split at top-level AND into classified token runs, which is the only
/// place an unparameterized predicate such as <c>deleted_at IS NULL</c> reaches
/// the IR at all. Consumed by the <c>-- @mirrors</c> comparison (JNT8011).
/// </summary>
public class PredicateAtomTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    /// <summary>Renders an atom the way the comparison reads it, minus schema resolution.</summary>
    private static string Render(PredicateAtom atom)
    {
        var parts = new List<string>();
        foreach (var term in atom.Terms)
        {
            parts.Add(term.Kind == AtomTermKind.Column && !string.IsNullOrEmpty(term.TableAlias)
                ? term.TableAlias + "." + term.Text
                : term.Text);
        }
        return string.Join(" ", parts);
    }

    private static List<string> Rendered(QueryModel model)
    {
        var result = new List<string>();
        foreach (var atom in model.PredicateAtoms)
            result.Add(Render(atom));
        return result;
    }

    [Fact]
    public void NoWhereClause_NoAtoms()
    {
        var model = ParseSql("select id from bookmarks");
        Assert.Empty(model.PredicateAtoms);
    }

    [Fact]
    public void TopLevelAnd_SplitsIntoConjuncts()
    {
        var model = ParseSql(
            "select id from bookmarks where bookmarks.user_id = @userId and bookmarks.deleted_at is null");

        Assert.Equal(
            new[] { "bookmarks.user_id = @userId", "bookmarks.deleted_at IS NULL" },
            Rendered(model));
    }

    [Fact]
    public void UnparameterizedPredicate_IsCaptured()
    {
        // The whole reason atoms exist: nothing else in the IR sees this.
        var model = ParseSql("select id from bookmarks where deleted_at is null");

        var atom = Assert.Single(model.PredicateAtoms);
        Assert.Equal("deleted_at IS NULL", Render(atom));
        Assert.Empty(model.Parameters);
        Assert.Empty(model.Literals);
    }

    [Fact]
    public void TopLevelOr_CollapsesWholeRegionToOneAtom()
    {
        // A disjunction does not decompose into conjuncts, so the region is
        // kept whole rather than split into pieces that mean nothing.
        var model = ParseSql(
            "select id from bookmarks where a = 1 or b = 2 and c = 3");

        var atom = Assert.Single(model.PredicateAtoms);
        Assert.Equal("a = 1 OR b = 2 AND c = 3", Render(atom));
    }

    [Fact]
    public void BetweenAnd_IsNotASplitter()
    {
        var model = ParseSql(
            "select id from bookmarks where views between @lo and @hi and status = 1");

        Assert.Equal(
            new[] { "views BETWEEN @lo AND @hi", "status = 1" },
            Rendered(model));
    }

    [Fact]
    public void ParenthesizedAnd_IsNotASplitter()
    {
        var model = ParseSql(
            "select id from bookmarks where (a = 1 and b = 2) and c = 3");

        Assert.Equal(
            new[] { "( a = 1 AND b = 2 )", "c = 3" },
            Rendered(model));
    }

    [Fact]
    public void NotExists_KeepsItsNot()
    {
        // Atoms are taken BEFORE ExtractPredicateSubqueries, which removes the
        // subquery span together with its leading NOT -- after that pass runs,
        // EXISTS and NOT EXISTS are indistinguishable in the IR.
        var model = ParseSql(
            "select id from bookmarks where not exists (select 1 from tags where tags.id = bookmarks.id)");

        var atom = Assert.Single(model.PredicateAtoms);
        Assert.StartsWith("NOT EXISTS (", Render(atom));
        Assert.NotEqual(Render(atom), Render(ParseSql(
            "select id from bookmarks where exists (select 1 from tags where tags.id = bookmarks.id)")
            .PredicateAtoms[0]));
    }

    [Fact]
    public void GroupByEndsTheRegion()
    {
        var model = ParseSql(
            "select user_id, count(*) as n from bookmarks where status = 1 group by user_id");

        var atom = Assert.Single(model.PredicateAtoms);
        Assert.Equal("status = 1", Render(atom));
    }

    [Fact]
    public void CteBodyAndFinalStatement_BothCaptured()
    {
        var model = ParseSql(
            "with page as (select id, created_at from bookmarks where bookmarks.user_id = @userId) " +
            "select id from page where page.created_at > @since");

        // The final statement's atoms are copied onto the outer model; the CTE
        // body keeps its own. Both matter -- a paginating list puts its filter
        // in the CTE while the count that mirrors it is flat.
        Assert.Equal(new[] { "page.created_at > @since" }, Rendered(model));
        Assert.Equal(new[] { "bookmarks.user_id = @userId" }, Rendered(Assert.Single(model.Ctes).Body));
    }

    [Fact]
    public void DeleteStatement_CapturesItsWhere()
    {
        var model = ParseSql("delete from bookmarks where bookmarks.id = @id");

        Assert.Equal(new[] { "bookmarks.id = @id" }, Rendered(model));
    }

    [Fact]
    public void EndSentinel_IsNotCapturedAsATerm()
    {
        // A blank trailing term would make two identical clauses differ by
        // whichever one had the predicate in final position.
        var model = ParseSql("select id from bookmarks where status = 1");

        var atom = Assert.Single(model.PredicateAtoms);
        Assert.All(atom.Terms, t => Assert.NotEqual(string.Empty, t.Text));
    }

    [Fact]
    public void SubqueryLift_DoesNotAlterTheAtomsAlreadyTaken()
    {
        // ExtractPredicateSubqueries removes the IN (SELECT ...) span from the
        // token stream after atoms are taken. The atom keeps the whole
        // predicate; the model's own Subqueries list confirms the lift ran.
        var model = ParseSql(
            "select id from bookmarks where bookmarks.id in (select tags.bookmark_id from tags) and status = 1");

        Assert.Single(model.Subqueries);
        Assert.Equal(
            new[] { "bookmarks.id IN ( SELECT tags.bookmark_id FROM tags )", "status = 1" },
            Rendered(model));
    }
}
