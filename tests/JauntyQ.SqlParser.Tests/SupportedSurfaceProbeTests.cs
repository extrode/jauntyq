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
    /// LATERAL is not in the grammar, and the parser reads the keyword as a
    /// table name -- which is why the consumer met JNT2001 ("table does not
    /// exist in schema") rather than a grammar diagnostic. Asserted so the
    /// misdiagnosis is recorded at its source; fixing it should turn this red.
    /// </summary>
    [Fact]
    public void LateralJoin_IsParsedAsATableNamed_Lateral()
    {
        var model = Parse(
            "select m.id, d.status from inbound_messages m " +
            "left join lateral (select d.status from inbound_deliveries d " +
            "where d.inbound_message_id = m.id limit 1) d on true");

        Assert.Contains(model.Tables, t =>
            string.Equals(t.TableName, "lateral", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains("SUBQUERY", model.UnsupportedConstructs);
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
