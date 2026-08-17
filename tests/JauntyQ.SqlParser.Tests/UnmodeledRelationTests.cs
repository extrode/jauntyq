using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

/// <summary>
/// Spec 015 followup. 015 guarded LATERAL in the join position only, so the
/// fix held for the one shape the reporting consumer happened to write and
/// nowhere else: FROM LATERAL, the comma form, and T-SQL's CROSS/OUTER APPLY
/// all still recorded an invented relation and sent the consumer to JNT2001
/// looking through their schema for a table the parser had made up.
///
/// The property under test is the general one -- no unimplemented relation
/// syntax is ever reported as a missing schema object -- so each accepted
/// position gets its own case rather than trusting one to stand for the rest.
/// </summary>
public class UnmodeledRelationTests
{
    private static QueryModel Parse(string sql) =>
        SqlParser.Parse(SqlTokenizer.Tokenize(sql), "Probe");

    private static void AssertNoInventedRelation(QueryModel model, string invented)
    {
        Assert.DoesNotContain(model.Tables, t =>
            string.Equals(t.TableName, invented, System.StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("lateral in the FROM position",
        "select a.id from lateral (select 1 as n) a")]
    [InlineData("lateral after a comma join",
        "select x.id from t, lateral (select 1 as n) x")]
    [InlineData("lateral in the join position (the 015 case, still covered)",
        "select m.id from m left join lateral (select 1 as n) d on true")]
    public void Lateral_InAnyPosition_IsGrammarNotAMissingTable(string label, string sql)
    {
        var model = Parse(sql);

        Assert.Contains("LATERAL", model.UnsupportedConstructs);
        AssertNoInventedRelation(model, "lateral");
        _ = label;
    }

    [Theory]
    [InlineData("cross apply", "select a.id from t cross apply (select 1 as n) a")]
    [InlineData("outer apply", "select a.id from t outer apply (select 1 as n) a")]
    public void Apply_IsRecordedAsItsOwnConstruct_NotAsATable(string label, string sql)
    {
        var model = Parse(sql);

        Assert.Contains("APPLY", model.UnsupportedConstructs);
        AssertNoInventedRelation(model, "apply");
        _ = label;
    }

    /// <summary>
    /// The reason APPLY is gated on a preceding CROSS/OUTER rather than matched
    /// as a bare word: it is reserved in T-SQL but not in the other four
    /// dialects, so a table genuinely named "apply" must keep parsing. Without
    /// the gate this fix would break a legal Postgres/MySQL/SQLite query to fix
    /// a SQL Server one.
    /// </summary>
    [Fact]
    public void ATableActuallyNamedApply_StillParsesAsATable()
    {
        var model = Parse("select a.id from t join apply a on a.id = t.id");

        Assert.DoesNotContain("APPLY", model.UnsupportedConstructs);
        Assert.Contains(model.Tables, t =>
            string.Equals(t.TableName, "apply", System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// INTERSECT and EXCEPT were in the state the reference page calls the
    /// worst of the three: neither supported nor refused. Neither was a
    /// tokenizer keyword, so "FROM a INTERSECT SELECT ..." read INTERSECT as
    /// a's ALIAS and produced a silently mis-modeled query with no construct
    /// recorded at all -- strictly more dangerous than UNION, which at least
    /// said no.
    /// </summary>
    [Theory]
    [InlineData("intersect", "select a.id from a intersect select b.id from b")]
    [InlineData("except", "select a.id from a except select b.id from b")]
    [InlineData("union still refused", "select a.id from a union select b.id from b")]
    public void SetOperations_AreRefused(string label, string sql)
    {
        var model = Parse(sql);

        Assert.Contains("UNION", model.UnsupportedConstructs);
        _ = label;
    }

    /// <summary>
    /// The specific mis-modeling the keyword addition removes: before it, the
    /// set-operation keyword was consumed as an alias of the first branch's
    /// table.
    /// </summary>
    [Fact]
    public void Intersect_IsNoLongerReadAsAnAliasOfTheFirstTable()
    {
        var model = Parse("select a.id from a intersect select b.id from b");

        Assert.DoesNotContain(model.Tables, t =>
            string.Equals(t.Alias, "intersect", System.StringComparison.OrdinalIgnoreCase));
    }
}
