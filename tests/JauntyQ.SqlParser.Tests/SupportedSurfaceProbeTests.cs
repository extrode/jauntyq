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

    /// <summary>
    /// PostgreSQL's <c>DISTINCT ON (...)</c> is refused as missing grammar.
    ///
    /// Measured before the fix (2026-08-18), which is why it is refused rather
    /// than skipped: the ON, its parenthesized list and the first real column
    /// glued into ONE expression item whose SQL was
    /// <c>ON ( d.inbound_message_id ) d.inbound_message_id</c>, leaving the
    /// query modeling one fewer real column than it returns. The aliased form
    /// is the reason this is a correctness fix and not a message fix: an alias
    /// satisfies the JNT3004 check that caught the other two shapes, so it
    /// passed validation outright and the generated mapper typed its first
    /// property from expression-shape inference over that text.
    /// </summary>
    [Theory]
    [InlineData("bare", "select distinct on (d.inbound_message_id) d.inbound_message_id, d.status " +
        "from inbound_deliveries d order by d.inbound_message_id, d.created_at desc")]
    [InlineData("aliased — passed validation entirely before the fix",
        "select distinct on (d.inbound_message_id) d.inbound_message_id as message_id, d.status " +
        "from inbound_deliveries d order by d.inbound_message_id, d.created_at desc")]
    [InlineData("unqualified", "select distinct on (status) status, id from inbound_deliveries order by status")]
    [InlineData("multi-column key",
        "select distinct on (a.x, a.y) a.x, a.y, a.z from things a order by a.x, a.y, a.z desc")]
    [InlineData("function call in the key list",
        "select distinct on (coalesce(a.x, a.y)) a.x, a.z from things a order by coalesce(a.x, a.y)")]
    public void DistinctOn_IsRefusedAsMissingGrammar(string label, string sql)
    {
        var model = Parse(sql);

        Assert.Contains("DISTINCT ON", model.UnsupportedConstructs);
        _ = label;
    }

    /// <summary>
    /// The other half of the DISTINCT ON fix, and the load-bearing half: the
    /// modifier is stepped over completely, so the projection that follows it
    /// parses as itself. Without this the refusal would arrive beside a
    /// spurious JNT3004 blaming the consumer's first column for needing an
    /// alias it already has no need of.
    /// </summary>
    [Fact]
    public void DistinctOn_LeavesTheRestOfTheProjectionIntact()
    {
        var model = Parse(
            "select distinct on (d.inbound_message_id) d.inbound_message_id, d.status " +
            "from inbound_deliveries d order by d.inbound_message_id, d.created_at desc");

        Assert.Equal(2, model.Columns.Count);
        Assert.All(model.Columns, c => Assert.False(c.IsExpression,
            $"'{c.ColumnName}{c.ExpressionSql}' was modeled as an expression; the DISTINCT ON " +
            "modifier is still gluing itself onto the projection."));
        Assert.Equal("inbound_message_id", model.Columns[0].ColumnName);
        Assert.Equal("d", model.Columns[0].TableAlias);
        Assert.Equal("status", model.Columns[1].ColumnName);
    }

    /// <summary>
    /// Plain DISTINCT keeps its existing free pass — it does not change the
    /// result shape, so it is skipped, not refused. A DISTINCT ON check that
    /// fired here would refuse most of the accepted surface.
    /// </summary>
    [Theory]
    [InlineData("plain distinct", "select distinct m.user_id from inbound_messages m")]
    [InlineData("distinct over a join with an ON clause",
        "select distinct m.id from inbound_messages m join inbound_deliveries d on d.inbound_message_id = m.id")]
    [InlineData("distinct then a function call",
        "select distinct coalesce(m.subject, 'x') as s from inbound_messages m")]
    [InlineData("count(distinct col)", "select count(distinct m.user_id) as c from inbound_messages m")]
    public void PlainDistinct_IsStillSkipped_NotRefused(string label, string sql)
    {
        var model = Parse(sql);

        Assert.DoesNotContain("DISTINCT ON", model.UnsupportedConstructs);
        _ = label;
    }

    /// <summary>
    /// T-SQL's two trailing TOP modifiers, found 2026-08-18 by asking whether
    /// the <c>DISTINCT ON</c> defect — a modifier skipped without checking what
    /// follows it — had siblings in the same function. Both did.
    ///
    /// Measured before the fix, <c>select top 10 percent a, b from things</c>
    /// modeled <c>percent AS a</c> and <c>b</c>: PERCENT is an Identifier, so
    /// unlike DISTINCT ON's leftovers it did not become an expression that
    /// JNT3004 would catch — it became a plain column reference, stealing the
    /// real first column's alias. <c>with ties</c> produced the expression item
    /// <c>WITH ties a</c> instead.
    ///
    /// Neither modifier changes the column shape, so both are now skipped like
    /// the row count itself rather than refused.
    /// </summary>
    [Theory]
    [InlineData("percent", "select top 10 percent a, b from things")]
    [InlineData("percent, parenthesized count", "select top (10) percent a, b from things")]
    [InlineData("with ties", "select top 10 with ties a, b from things order by a")]
    [InlineData("percent and with ties", "select top 10 percent with ties a, b from things order by a")]
    [InlineData("uppercase", "SELECT TOP 10 PERCENT a, b FROM things")]
    public void TopModifiers_AreSkipped_LeavingTheRealProjection(string label, string sql)
    {
        var model = Parse(sql);

        Assert.Equal(2, model.Columns.Count);
        Assert.All(model.Columns, c => Assert.False(c.IsExpression,
            $"{label}: '{c.ColumnName}{c.ExpressionSql}' was modeled as an expression"));
        Assert.Equal("a", model.Columns[0].ColumnName);
        Assert.Equal("b", model.Columns[1].ColumnName);

        // The specific silent corruption: PERCENT must not survive as a column
        // wearing the real first column's name as its alias.
        Assert.Empty(model.Columns[0].OutputAlias);
        Assert.DoesNotContain(model.Columns, c =>
            string.Equals(c.ColumnName, "percent", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The guard that shaped the fix. Quoting does NOT protect a column named
    /// <c>percent</c>: the tokenizer strips the delimiters, so <c>[percent]</c>
    /// and a bare <c>PERCENT</c> arrive as the same Identifier token — an
    /// assumption that consuming PERCENT unconditionally would have been safe
    /// was measured and found false, and it would have deleted a real column.
    ///
    /// What separates them is the following token: a modifier always has
    /// another projection item after it, a column has a comma, an AS, or FROM.
    /// </summary>
    [Theory]
    [InlineData("followed by a comma", "select top 10 [percent], b from things", 2)]
    [InlineData("the only column", "select top 10 [percent] from things", 1)]
    [InlineData("aliased", "select top 10 [percent] as p from things", 1)]
    public void PercentAsAColumnName_SurvivesTheTopModifierSkip(string label, string sql, int expectedColumns)
    {
        var model = Parse(sql);

        Assert.Equal(expectedColumns, model.Columns.Count);
        Assert.Contains(model.Columns, c =>
            string.Equals(c.ColumnName, "percent", StringComparison.OrdinalIgnoreCase));
        _ = label;
    }

    [Fact]
    public void PlainTop_IsUnaffected()
    {
        var model = Parse("select top 10 a, b from things");

        Assert.Equal(2, model.Columns.Count);
        Assert.Equal("a", model.Columns[0].ColumnName);
        Assert.Equal("b", model.Columns[1].ColumnName);
    }

    /// <summary>
    /// <c>SELECT ALL</c> is DISTINCT's ANSI complement — "do not deduplicate",
    /// already the default — so it cannot change the result shape and is
    /// skipped like DISTINCT. Measured 2026-08-18: before the fix it glued onto
    /// the first column as the expression <c>ALL a.x</c> and was refused by
    /// JNT3004 for wanting an alias.
    /// </summary>
    [Theory]
    [InlineData("qualified", "select all a.x from things a")]
    [InlineData("unqualified", "select all x, y from things")]
    [InlineData("star", "select all * from things")]
    public void SelectAll_IsSkipped_LikeDistinct(string label, string sql)
    {
        var model = Parse(sql);

        Assert.All(model.Columns, c => Assert.False(c.IsExpression,
            $"{label}: '{c.ExpressionSql}' was modeled as an expression"));
        Assert.DoesNotContain(model.Columns, c =>
            string.Equals(c.ColumnName, "all", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// T-SQL table hints were already handled correctly — pinned here because
    /// they sit in the same "skipped modifier" position that produced three
    /// defects in the SELECT list, and a regression would be silent.
    /// </summary>
    [Theory]
    [InlineData("aliased", "select a.x from things a with (nolock) where a.x = @p", "a")]
    [InlineData("unaliased", "select x from things with (nolock)", "")]
    public void TableHints_DoNotBecomeTablesOrAliases(string label, string sql, string expectedAlias)
    {
        var model = Parse(sql);

        var table = Assert.Single(model.Tables);
        Assert.Equal("things", table.TableName);
        Assert.Equal(expectedAlias, table.Alias);
        _ = label;
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
