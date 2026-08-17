using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

/// <summary>
/// Pins the parser's answer for the query shapes named in the justsmtp.com
/// consumer report (2026-08-17), so the supported-surface reference has a
/// measured source rather than a remembered one and a later change that widens
/// or narrows the surface has to say so here.
///
/// Aggregation shapes parse clean; the three rejected shapes all reduce to the
/// single "SUBQUERY" unsupported construct behind JNT1007.
/// </summary>
public class SupportedSurfaceProbeTests
{
    private static QueryModel Parse(string sql) =>
        SqlParser.Parse(SqlTokenizer.Tokenize(sql), "Probe");

    // ── Aggregation: supported, and the report assumed otherwise ───────────

    [Theory]
    [InlineData("group by, single table",
        "select m.user_id, count(*) as total from inbound_messages m " +
        "where m.user_id = @userId group by m.user_id")]
    [InlineData("group by over a join",
        "select m.id, m.subject, count(d.id) as delivery_count " +
        "from inbound_messages m left join inbound_deliveries d on d.inbound_message_id = m.id " +
        "where m.user_id = @userId group by m.id, m.subject")]
    [InlineData("bool_or over a join",
        "select m.id, bool_or(d.status = 'delivered') as delivered " +
        "from inbound_messages m left join inbound_deliveries d on d.inbound_message_id = m.id " +
        "group by m.id")]
    [InlineData("count(*) filter (where ...) over a join",
        "select m.id, count(*) filter (where d.status = 'failed') as failed_count " +
        "from inbound_messages m left join inbound_deliveries d on d.inbound_message_id = m.id " +
        "group by m.id")]
    [InlineData("group by ... having",
        "select m.user_id, count(*) as total from inbound_messages m " +
        "group by m.user_id having count(*) > 5")]
    public void AggregationShapes_Parse_WithNoUnsupportedConstruct(string label, string sql)
    {
        var model = Parse(sql);

        Assert.True(model.HasGroupBy, $"{label}: GROUP BY was not recognized");
        Assert.Empty(model.UnsupportedConstructs);
    }

    // ── Subqueries outside WHERE: all three collapse to one refusal ────────

    [Theory]
    [InlineData("scalar subquery in the projection",
        "select m.id, (select max(d.created_at) from inbound_deliveries d " +
        "where d.inbound_message_id = m.id) as last_delivery from inbound_messages m")]
    [InlineData("derived table in FROM",
        "select x.id from (select m.id from inbound_messages m) x")]
    public void SubqueryOutsideWhere_IsRefused_AsUnsupportedConstruct(string label, string sql)
    {
        var model = Parse(sql);

        Assert.Contains("SUBQUERY", model.UnsupportedConstructs);
        Assert.Empty(model.Subqueries);
        _ = label;
    }

    /// <summary>
    /// LATERAL is still unsupported, but it is now recorded as MISSING GRAMMAR
    /// rather than as a table.
    ///
    /// This test previously asserted the opposite — that the parser produced a
    /// relation literally named "lateral" — which is what sent the consumer to
    /// JNT2001 and their own schema for a table the parser had invented. Spec
    /// 015 T6 turned it red on purpose; this is the replacement.
    /// </summary>
    [Fact]
    public void LateralJoin_IsRecordedAsMissingGrammar_NotAsATable()
    {
        var model = Parse(
            "select m.id, d.status from inbound_messages m " +
            "left join lateral (select d.status from inbound_deliveries d " +
            "where d.inbound_message_id = m.id limit 1) d on true");

        Assert.Contains("LATERAL", model.UnsupportedConstructs);
        Assert.DoesNotContain(model.Tables, t =>
            string.Equals(t.TableName, "lateral", System.StringComparison.OrdinalIgnoreCase));
    }

    // ── The rest of the surface named by docs/06-reference/supported-sql.md ─

    /// <summary>
    /// Spec 015 T8: the reference page claims a specific accepted surface, so
    /// every claim on it is measured here and the page cannot drift silently
    /// away from the parser.
    /// </summary>
    [Theory]
    [InlineData("distinct", "select distinct m.user_id from inbound_messages m")]
    [InlineData("case expression with an alias",
        "select m.id, case when m.subject is null then 'x' else 'y' end as kind from inbound_messages m")]
    [InlineData("window function with an alias",
        "select m.id, row_number() over (partition by m.user_id order by m.id) as rn from inbound_messages m")]
    [InlineData("between", "select m.id from inbound_messages m where m.id between @a and @b")]
    [InlineData("order by with limit and offset",
        "select m.id from inbound_messages m order by m.id limit @n offset @o")]
    [InlineData("where-clause IN (SELECT ...)",
        "select m.id from inbound_messages m where m.id in (select d.inbound_message_id from inbound_deliveries d)")]
    [InlineData("insert ... select",
        "insert into archive (id) select m.id from inbound_messages m")]
    [InlineData("insert ... returning",
        "insert into inbound_messages (subject) values (@s) returning id")]
    public void AcceptedShapes_RecordNoUnsupportedConstruct(string label, string sql)
    {
        var model = Parse(sql);

        Assert.Empty(model.UnsupportedConstructs);
        _ = label;
    }

    [Theory]
    [InlineData("union", "UNION",
        "select m.id from inbound_messages m union select d.id from inbound_deliveries d")]
    [InlineData("with recursive", "WITH RECURSIVE",
        "with recursive t as (select 1 as n) select t.n from t")]
    [InlineData("two statements in one file", "MULTI_STATEMENT",
        "select m.id from inbound_messages m; select d.id from inbound_deliveries d")]
    public void RefusedShapes_RecordTheirOwnConstruct(string label, string construct, string sql)
    {
        var model = Parse(sql);

        Assert.Contains(construct, model.UnsupportedConstructs);
        _ = label;
    }

    /// <summary>
    /// A measured gap rather than a supported shape: <c>UPDATE ... FROM other</c>
    /// parses without complaint but models only the target table, so the second
    /// relation is invisible to validation. Noted in <c>the todo list</c>; pinned
    /// here so that whoever fixes it has to come back and change this test
    /// rather than discovering the claim on the reference page was stale.
    /// </summary>
    [Fact]
    public void UpdateFrom_ParsesButDoesNotModelTheSecondRelation()
    {
        var model = Parse(
            "update inbound_messages set subject = d.status from inbound_deliveries d " +
            "where d.inbound_message_id = inbound_messages.id");

        Assert.Empty(model.UnsupportedConstructs);
        Assert.Equal(StatementType.Update, model.StatementType);
        var only = Assert.Single(model.Tables);
        Assert.Equal("inbound_messages", only.TableName);
    }

    // ── The shapes the report confirmed as working ─────────────────────────

    [Fact]
    public void CorrelatedExistsInWhere_IsLifted_AndSupported()
    {
        var model = Parse(
            "select m.id from inbound_messages m where exists " +
            "(select 1 from inbound_deliveries d where d.inbound_message_id = m.id)");

        Assert.Empty(model.UnsupportedConstructs);
        var sub = Assert.Single(model.Subqueries);
        Assert.Equal(SubqueryKind.Exists, sub.Kind);
    }

    [Fact]
    public void NonRecursiveCte_Parses_WithNoUnsupportedConstruct()
    {
        var model = Parse(
            "with recent as (select m.id from inbound_messages m where m.user_id = @userId) " +
            "select r.id from recent r");

        Assert.Empty(model.UnsupportedConstructs);
    }
}
