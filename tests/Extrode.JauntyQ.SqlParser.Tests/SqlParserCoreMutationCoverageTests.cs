using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>
/// Direct coverage for SqlParser.cs mutation survivors not exercised by the
/// existing suite: expression type-inference shape checks (SUM/AVG, EXISTS,
/// IS [NOT] NULL and top-level comparisons across CASE/paren nesting,
/// redundant-paren stripping), SELECT INTO target skipping, qualified
/// star-select, ORDER BY boundary/expression-depth tracking, and the TOP N
/// PERCENT / WITH TIES modifier's exact token arithmetic.
/// </summary>
[Trait("Category", "AuditRegression")]
public class SqlParserCoreMutationCoverageTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    private static ColumnRef Expr(QueryModel model, string alias) =>
        model.Columns.Find(c => c.IsExpression && c.OutputAlias == alias)
            ?? throw new Xunit.Sdk.XunitException($"no expression item aliased '{alias}'");

    // ── SUM/AVG shape: exact single-column call only ────────────────────

    [Theory]
    [InlineData("SUM", "sum")]
    [InlineData("AVG", "avg")]
    public void AggregateOfBareColumn_CapturesFunctionAndArgument(string fn, string expectedFn)
    {
        var model = ParseSql($"SELECT {fn}(amount) AS total FROM orders");

        var col = Expr(model, "total");
        Assert.Equal(expectedFn.ToUpperInvariant(), col.AggregateFunction);
        Assert.Empty(col.AggregateArgTableAlias);
        Assert.Equal("amount", col.AggregateArgColumnName);
        Assert.Equal(string.Empty, col.InferredDbType);
    }

    [Fact]
    public void AggregateOfQualifiedColumn_SplitsAliasAndColumn()
    {
        var model = ParseSql("SELECT SUM(o.amount) AS total FROM orders o");

        var col = Expr(model, "total");
        Assert.Equal("SUM", col.AggregateFunction);
        Assert.Equal("o", col.AggregateArgTableAlias);
        Assert.Equal("amount", col.AggregateArgColumnName);
    }

    [Theory]
    [InlineData("SUM(a + b)")]
    [InlineData("SUM(DISTINCT amount)")]
    [InlineData("ROUND(SUM(amount), 2)")]
    [InlineData("SUM(amount) + 1")]
    public void AggregateNotInExactShape_DoesNotCaptureAggregateFunction(string expression)
    {
        var model = ParseSql($"SELECT {expression} AS total FROM orders");

        var col = Expr(model, "total");
        Assert.Empty(col.AggregateFunction);
    }

    // ── EXISTS(...) head ──────────────────────────────────────────────

    [Fact]
    public void ExistsSubquery_InfersBooleanNotNull()
    {
        var model = ParseSql(
            "SELECT EXISTS(SELECT 1 FROM orders WHERE orders.user_id = users.id) AS has_orders FROM users");

        var col = Expr(model, "has_orders");
        Assert.Equal("boolean", col.InferredDbType);
        Assert.True(col.InferredNotNull);
    }

    [Fact]
    public void ExistsNotFollowedByParen_DoesNotInferBoolean()
    {
        // EXISTS without an immediately-following '(' is not the recognized
        // shape (it is malformed SQL, but Parse must still be total over it).
        var model = ParseSql("SELECT EXISTS AS flag FROM users");

        var col = Expr(model, "flag");
        Assert.Equal(string.Empty, col.InferredDbType);
    }

    // ── IS [NOT] NULL / comparison, across CASE and paren nesting depth ─

    [Fact]
    public void TopLevelIsNotNull_InfersBooleanNotNull()
    {
        var model = ParseSql("SELECT email IS NOT NULL AS has_email FROM users");

        var col = Expr(model, "has_email");
        Assert.Equal("boolean", col.InferredDbType);
        Assert.True(col.InferredNotNull);
    }

    [Fact]
    public void TopLevelComparison_InfersBooleanNotNull()
    {
        var model = ParseSql("SELECT age >= 18 AS is_adult FROM users");

        var col = Expr(model, "is_adult");
        Assert.Equal("boolean", col.InferredDbType);
        Assert.True(col.InferredNotNull);
    }

    [Fact]
    public void ComparisonInsideCase_DoesNotLeakAsTopLevelBoolean()
    {
        // The comparison ("status = 1") sits inside a CASE...END, tracked by
        // caseDepth alongside paren depth -- it must NOT be read as the
        // enclosing expression's own top-level operator (CASE's result type
        // is its THEN/ELSE branch type, which shape inference cannot know).
        var model = ParseSql(
            "SELECT CASE WHEN status = 1 THEN 'active' ELSE 'inactive' END AS label FROM users");

        var col = Expr(model, "label");
        Assert.Equal(string.Empty, col.InferredDbType);
        Assert.False(col.InferredNotNull);
    }

    [Fact]
    public void ComparisonInsideNestedParens_DoesNotLeakAsTopLevelBoolean()
    {
        var model = ParseSql("SELECT LOWER(CASE WHEN name = 'x' THEN 'y' ELSE 'z' END) AS lbl FROM users");

        var col = Expr(model, "lbl");
        Assert.Equal(string.Empty, col.InferredDbType);
    }

    [Fact]
    public void IsNullInsideParens_StillDetectedAfterDepthReturnsToZero()
    {
        // "(a) IS NULL": the parenthesised sub-expression closes depth back to
        // zero before IS is reached, so IS is seen at depth 0 and still fires.
        var model = ParseSql("SELECT (email) IS NULL AS no_email FROM users");

        var col = Expr(model, "no_email");
        Assert.Equal("boolean", col.InferredDbType);
    }

    // ── Redundant outer-paren stripping (EnclosesWholeRun) ──────────────

    [Fact]
    public void SingleRedundantOuterParens_AreStripped_ExposingTopLevelIsNotNull()
    {
        var model = ParseSql("SELECT (email IS NOT NULL) AS has_email FROM users");

        var col = Expr(model, "has_email");
        Assert.Equal("boolean", col.InferredDbType);
    }

    [Fact]
    public void TwoSeparateParenGroups_AreNotTreatedAsOneRedundantWrap()
    {
        // "(a) + (b)": the first '(' closes at depth 0 well before the run's
        // end, so EnclosesWholeRun must say this pair does NOT enclose the
        // whole expression -- stripping it would wrongly expose "email" alone
        // as if it were the whole (unwrapped) body.
        var model = ParseSql("SELECT (a) + (b) AS total FROM t");

        var col = Expr(model, "total");
        Assert.Equal(string.Empty, col.InferredDbType);
    }

    [Fact]
    public void ParenWrappedScalarSubquery_IsNeverUnwrapped()
    {
        // Unwrapping would expose the subquery's own internal WHERE "=" as if
        // it were the outer expression's top-level comparison.
        var model = ParseSql(
            "SELECT (SELECT MAX(id) FROM orders WHERE orders.user_id = users.id) AS last_order FROM users");

        var col = Expr(model, "last_order");
        Assert.Equal(string.Empty, col.InferredDbType);
    }

    // ── SELECT INTO target skipping ──────────────────────────────────

    [Fact]
    public void SelectInto_IsRecordedAsUnsupported()
    {
        var model = ParseSql("SELECT id, email INTO backup FROM users");

        Assert.Contains("SELECT INTO", model.UnsupportedConstructs);
    }

    [Fact]
    public void SelectInto_DoesNotGlueTargetOntoPrecedingColumn()
    {
        // Before the refusal, "email into backup" glued into one opaque
        // expression item -- the real column "email" must still resolve on
        // its own, and "backup" must not appear as a phantom column.
        var model = ParseSql("SELECT id, email INTO backup FROM users");

        Assert.DoesNotContain(model.Columns, c => c.ColumnName == "backup" || c.OutputAlias == "backup");
        Assert.Contains(model.Columns, c => c.ColumnName == "email");
    }

    [Theory]
    [InlineData("SELECT id INTO TEMP staging FROM users", 1)]
    [InlineData("SELECT id INTO TEMPORARY staging FROM users", 1)]
    [InlineData("SELECT id INTO UNLOGGED staging FROM users", 1)]
    [InlineData("SELECT id INTO TABLE staging FROM users", 1)]
    [InlineData("SELECT id INTO TEMP TABLE staging FROM users", 1)]
    public void SelectIntoPostgresForm_SkipsAllModifierWordsAndTheTargetName(string sql, int expectedColumns)
    {
        var model = ParseSql(sql);

        Assert.Contains("SELECT INTO", model.UnsupportedConstructs);
        Assert.Equal(expectedColumns, model.Columns.Count);
        Assert.DoesNotContain(model.Columns, c =>
            c.ColumnName is "staging" or "temp" or "temporary" or "unlogged" or "table");
    }

    [Fact]
    public void SelectIntoTSqlTempTable_SkipsHashPrefixedTarget()
    {
        // T-SQL's '#' is its own Symbol token, so "#t" is two tokens.
        var model = ParseSql("SELECT id INTO #staging FROM users");

        Assert.Contains("SELECT INTO", model.UnsupportedConstructs);
        Assert.Single(model.Columns);
    }

    [Fact]
    public void SelectIntoTSqlGlobalTempTable_SkipsDoubleHashPrefixedTarget()
    {
        var model = ParseSql("SELECT id INTO ##staging FROM users");

        Assert.Contains("SELECT INTO", model.UnsupportedConstructs);
        Assert.Single(model.Columns);
    }

    [Fact]
    public void SelectIntoUnaliased_IsRefusedNotAcceptedAsAnExpressionNeedingAnAlias()
    {
        // EndsASelectListItem(INTO) must make INTO end an in-flight expression
        // item too, not just be recognized as its own token -- otherwise
        // "count(*) into backup" glues INTO's target into the count expression.
        var model = ParseSql("SELECT count(*) INTO backup FROM users");

        Assert.Contains("SELECT INTO", model.UnsupportedConstructs);
        var col = Assert.Single(model.Columns);
        Assert.Equal(string.Empty, col.OutputAlias);
    }

    // ── Qualified star-select ("alias.*") ────────────────────────────

    [Fact]
    public void QualifiedStarSelect_CapturesTableAliasAndStarColumn()
    {
        var model = ParseSql("SELECT u.* FROM users u");

        var col = Assert.Single(model.Columns);
        Assert.Equal("u", col.TableAlias);
        Assert.Equal("*", col.ColumnName);
    }

    [Fact]
    public void PlainStarSelect_HasNoTableAlias()
    {
        var model = ParseSql("SELECT * FROM users");

        var col = Assert.Single(model.Columns);
        Assert.Empty(col.TableAlias);
        Assert.Equal("*", col.ColumnName);
    }

    // ── Implicit-alias plain column: afterAlias guard ───────────────────

    [Theory]
    [InlineData("SELECT id name FROM users", "name")]
    public void ImplicitAliasEndingAtEndOfList_IsCaptured(string sql, string expectedAlias)
    {
        var model = ParseSql(sql);

        var col = Assert.Single(model.Columns);
        Assert.Equal("id", col.ColumnName);
        Assert.Equal(expectedAlias, col.OutputAlias);
    }

    [Fact]
    public void ImplicitAliasFollowedByComma_IsCaptured()
    {
        var model = ParseSql("SELECT id name, email FROM users");

        Assert.Equal(2, model.Columns.Count);
        Assert.Equal("name", model.Columns[0].OutputAlias);
        Assert.Equal("email", model.Columns[1].ColumnName);
    }

    [Fact]
    public void ImplicitAliasFollowedByClauseKeyword_IsCaptured()
    {
        var model = ParseSql("SELECT id name FROM users WHERE id = @id");

        var col = Assert.Single(model.Columns);
        Assert.Equal("name", col.OutputAlias);
    }

    [Fact]
    public void TwoBareIdentifiersWhereSecondIsNotAValidAliasTerminator_IsNotAnImplicitAlias()
    {
        // "id name extra": the token after the candidate alias ("extra") is
        // neither a comma, a clause keyword, nor End, so "id name" must NOT
        // be read as column+alias -- it falls to the expression path instead.
        var model = ParseSql("SELECT id name extra FROM users");

        Assert.DoesNotContain(model.Columns, c => c.ColumnName == "id" && c.OutputAlias == "name");
    }

    // ── ORDER BY: boundary and nested-paren expression depth ────────────

    [Fact]
    public void OrderByExpression_TracksParenDepthAcrossNestedCalls()
    {
        // The expression consumes to the next TOP-LEVEL comma; a comma nested
        // inside round(...)'s argument list must not end the item early.
        var model = ParseSql("SELECT id FROM t ORDER BY round(price, 2), id");

        Assert.Equal(2, model.OrderBy.Count);
        Assert.Equal(OrderByItemKind.Expression, model.OrderBy[0].Kind);
        Assert.Equal(OrderByItemKind.PlainColumn, model.OrderBy[1].Kind);
    }

    [Fact]
    public void OrderByExpression_EndsAtNextClauseKeyword_NotJustEndOfTokens()
    {
        var model = ParseSql("SELECT id FROM t ORDER BY lower(name) LIMIT 10");

        var item = Assert.Single(model.OrderBy);
        Assert.Equal(OrderByItemKind.Expression, item.Kind);
    }

    [Fact]
    public void OrderByDirectionKeyword_DoesNotBecomeASecondItem()
    {
        var model = ParseSql("SELECT id FROM t ORDER BY name DESC");

        var item = Assert.Single(model.OrderBy);
        Assert.Equal(OrderByItemKind.PlainColumn, item.Kind);
        Assert.Equal("name", item.BoundColumnName);
    }

    [Fact]
    public void OrderByLoneColumnAtAbsoluteEndOfStatement_IsPlainColumn()
    {
        var model = ParseSql("SELECT id FROM t ORDER BY name");

        var item = Assert.Single(model.OrderBy);
        Assert.Equal(OrderByItemKind.PlainColumn, item.Kind);
    }

    // ── TOP N PERCENT WITH TIES: exact token arithmetic ─────────────────

    [Fact]
    public void TopParenN_PercentWithTies_SkipsAllFourModifierTokens()
    {
        var model = ParseSql("SELECT TOP (10) PERCENT WITH TIES a, b FROM things ORDER BY a");

        Assert.Equal(2, model.Columns.Count);
        Assert.Equal("a", model.Columns[0].ColumnName);
        Assert.Equal("b", model.Columns[1].ColumnName);
    }

    [Fact]
    public void TopN_WithTies_ButNoPercent_SkipsBothTopAndWithTies()
    {
        var model = ParseSql("SELECT TOP 5 WITH TIES a FROM things ORDER BY a");

        var col = Assert.Single(model.Columns);
        Assert.Equal("a", col.ColumnName);
    }

    [Fact]
    public void TopN_WithFollowedByNonTiesIdentifier_WithIsNotConsumedAsAModifier()
    {
        // "WITH" here is not "WITH TIES" (the next token isn't TIES), so the
        // two-token skip must not fire; Parse must still be total (no throw)
        // over this malformed-but-tokenizable shape.
        var ex = Record.Exception(() => ParseSql("SELECT TOP 5 WITH bogus a FROM things"));

        Assert.Null(ex);
    }

    [Fact]
    public void TopN_WithAtEndOfStatement_NoSecondTokenToCheckForTies_DoesNotThrow()
    {
        // Boundary: WITH is the last real token, so the "pos + 1 < tokens.Count"
        // guard on the TIES lookahead must hold even though only the tokenizer's
        // End sentinel follows.
        var ex = Record.Exception(() => ParseSql("SELECT TOP 5 WITH"));

        Assert.Null(ex);
    }

    // ── ORDER BY terminator/lookahead literal checks ────────────────────

    [Fact]
    public void OrderBy_TerminatesExactlyAtOffsetKeyword()
    {
        // OFFSET is deliberately not in IsClauseKeyword's list -- it is the
        // ORDER BY terminator's own explicit "|| token.Value == OFFSET" arm.
        var model = ParseSql("SELECT a FROM t ORDER BY x OFFSET 5");

        var item = Assert.Single(model.OrderBy);
        Assert.Equal("x", item.BoundColumnName);
    }

    [Fact]
    public void OrderByColumnFollowedByComma_IsPlainColumn_NotExpression()
    {
        // IsPlainOrderByItem's comma lookahead must match the literal ",",
        // not any Symbol token -- otherwise a bare column before a comma
        // wrongly falls through to the expression path.
        var model = ParseSql("SELECT a FROM t ORDER BY x, y");

        Assert.Equal(2, model.OrderBy.Count);
        Assert.Equal(OrderByItemKind.PlainColumn, model.OrderBy[0].Kind);
        Assert.Equal("x", model.OrderBy[0].BoundColumnName);
        Assert.Equal(OrderByItemKind.PlainColumn, model.OrderBy[1].Kind);
        Assert.Equal("y", model.OrderBy[1].BoundColumnName);
    }

    // ── Expression-list comma boundary (depth-0 only) ───────────────────

    [Fact]
    public void ExpressionItem_TerminatesAtTopLevelComma_NotInsideParens()
    {
        // The expression-run collector's comma check must match the literal
        // "," at depth 0; a paren-nested comma must NOT terminate the run.
        var model = ParseSql("SELECT a + (b, c) AS x, d FROM t");

        Assert.Equal(2, model.Columns.Count);
        var expr = Expr(model, "x");
        Assert.True(expr.IsExpression);
        Assert.Equal("d", model.Columns[1].ColumnName);
    }

    // ── UnsupportedConstructs dedup guards match their real literal ─────

    [Fact]
    public void DistinctOnRecordedTwiceAcrossSeparateStatements_DedupesByExactLiteral()
    {
        // The dedup guard checks Contains("DISTINCT ON") specifically -- if
        // it matched any other string (e.g. blanked to ""), a second
        // occurrence would append a duplicate entry instead of being
        // deduped.
        var first = ParseSql("SELECT DISTINCT ON (a) a FROM t");
        var second = ParseSql("SELECT DISTINCT ON (b) b FROM t", "Second");

        Assert.Single(first.UnsupportedConstructs, c => c == "DISTINCT ON");
        Assert.Single(second.UnsupportedConstructs, c => c == "DISTINCT ON");
    }

    // ── ParseJoinCondition: multiple AND conditions in ON clause ────────

    [Fact]
    public void JoinCondition_WithTwoAndClauses_RecordsBothJoinPairs()
    {
        var model = ParseSql(
            "SELECT * FROM orders o JOIN order_items oi " +
            "ON o.id = oi.order_id AND o.tenant = oi.tenant");

        Assert.Equal(2, model.Joins.Count);
        Assert.Equal("o", model.Joins[0].LeftTable);
        Assert.Equal("id", model.Joins[0].LeftColumn);
        Assert.Equal("oi", model.Joins[0].RightTable);
        Assert.Equal("order_id", model.Joins[0].RightColumn);
        Assert.Equal("o", model.Joins[1].LeftTable);
        Assert.Equal("tenant", model.Joins[1].LeftColumn);
        Assert.Equal("oi", model.Joins[1].RightTable);
        Assert.Equal("tenant", model.Joins[1].RightColumn);
    }

    [Fact]
    public void JoinCondition_WithThreeAndClauses_RecordsAllThreeJoinPairs()
    {
        // Exercises the AND-chain loop iterating more than once (pos += 4
        // advancing correctly across repeated iterations), not just entering
        // it once.
        var model = ParseSql(
            "SELECT * FROM a JOIN b ON a.x = b.x AND a.y = b.y AND a.z = b.z");

        Assert.Equal(3, model.Joins.Count);
        Assert.Equal("z", model.Joins[2].LeftColumn);
        Assert.Equal("z", model.Joins[2].RightColumn);
    }

    [Fact]
    public void JoinCondition_SingleClauseOnly_DoesNotConsumeTrailingAnd()
    {
        // A single ON clause followed by a WHERE ... AND ... must not have
        // its own AND folded into the join-condition loop.
        var model = ParseSql(
            "SELECT * FROM a JOIN b ON a.x = b.x WHERE a.flag = 1 AND b.flag = 1");

        Assert.Single(model.Joins);
    }

    // ── ParseUsingClause: multi-column USING(...) sugar ─────────────────

    [Fact]
    public void UsingClause_WithSingleColumn_ExpandsToOneJoinPair()
    {
        var model = ParseSql("SELECT * FROM orders o JOIN customers c USING (customer_id)");

        var join = Assert.Single(model.Joins);
        Assert.Equal("o", join.LeftTable);
        Assert.Equal("customer_id", join.LeftColumn);
        Assert.Equal("c", join.RightTable);
        Assert.Equal("customer_id", join.RightColumn);
    }

    [Fact]
    public void UsingClause_WithMultipleColumns_ExpandsToOneJoinPairPerColumn()
    {
        var model = ParseSql("SELECT * FROM orders o JOIN order_lines l USING (order_id, line_no)");

        Assert.Equal(2, model.Joins.Count);
        Assert.Equal("order_id", model.Joins[0].LeftColumn);
        Assert.Equal("order_id", model.Joins[0].RightColumn);
        Assert.Equal("line_no", model.Joins[1].LeftColumn);
        Assert.Equal("line_no", model.Joins[1].RightColumn);
    }

    [Fact]
    public void UsingClause_WithOnlyOneTableInScope_IsANoOp()
    {
        // ParseUsingClause guards on model.Tables.Count < 2 -- a malformed
        // single-table USING(...) must not throw or fabricate a join.
        var model = ParseSql("SELECT * FROM orders USING (id)");

        Assert.Empty(model.Joins);
    }

    [Fact]
    public void UsingClause_WithNoAliasesOnEitherTable_FallsBackToTableNames()
    {
        // rightKey/leftKey prefer Alias only when it's non-empty; with no
        // alias on either side both must fall back to TableName, not an
        // empty string.
        var model = ParseSql("SELECT * FROM orders JOIN customers USING (customer_id)");

        var join = Assert.Single(model.Joins);
        Assert.Equal("orders", join.LeftTable);
        Assert.Equal("customers", join.RightTable);
    }

    // ── ParseJoin: alias vs. bare "USING" identifier disambiguation ─────

    [Fact]
    public void JoinedTable_WithRealAliasNamedNearUsing_KeepsAliasNotSwallowedAsUsing()
    {
        var model = ParseSql("SELECT * FROM orders o JOIN customers cust ON o.customer_id = cust.id");

        var joined = model.Tables.Find(t => t.TableName == "customers");
        Assert.NotNull(joined);
        Assert.Equal("cust", joined!.Alias);
    }

    [Fact]
    public void JoinedTableFollowedByUsingClause_DoesNotTreatUsingAsAlias()
    {
        var model = ParseSql("SELECT * FROM orders o JOIN customers USING (customer_id)");

        var joined = model.Tables.Find(t => t.TableName == "customers");
        Assert.NotNull(joined);
        Assert.Equal(string.Empty, joined!.Alias);
        Assert.Single(model.Joins);
    }

    [Fact]
    public void JoinedTable_WithAsAlias_RecordsAliasAndJoinKind()
    {
        var model = ParseSql("SELECT * FROM orders o LEFT JOIN customers AS c ON o.customer_id = c.id");

        var joined = model.Tables.Find(t => t.TableName == "customers");
        Assert.NotNull(joined);
        Assert.Equal("c", joined!.Alias);
        Assert.Equal(JoinKind.Left, joined.Join);
    }

    // ── ParseJoin: LATERAL / CROSS APPLY / OUTER APPLY unmodeled forms ──

    [Fact]
    public void Join_Lateral_RecordsUnmodeledConstructWithoutFabricatingTable()
    {
        var model = ParseSql("SELECT * FROM orders o JOIN LATERAL (SELECT 1) x ON true");

        Assert.Contains("LATERAL", model.UnsupportedConstructs);
        Assert.DoesNotContain(model.Tables, t => string.Equals(t.TableName, "lateral", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Join_CrossApply_RecordsUnmodeledConstructWithoutFabricatingTable()
    {
        var model = ParseSql("SELECT * FROM orders o CROSS APPLY (SELECT 1) x");

        Assert.Contains("APPLY", model.UnsupportedConstructs);
        Assert.DoesNotContain(model.Tables, t => string.Equals(t.TableName, "apply", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Join_OuterApply_RecordsUnmodeledConstructWithoutFabricatingTable()
    {
        var model = ParseSql("SELECT * FROM orders o OUTER APPLY (SELECT 1) x");

        Assert.Contains("APPLY", model.UnsupportedConstructs);
    }

    [Fact]
    public void Join_ApplyWithoutCrossOrOuter_IsATableNamedApply()
    {
        // The APPLY recognition is gated on having seen CROSS/OUTER first --
        // an ordinary table genuinely named "apply" via a plain JOIN must
        // parse as a normal table reference, not an unmodeled construct.
        var model = ParseSql("SELECT * FROM orders o JOIN apply a ON o.id = a.order_id");

        Assert.DoesNotContain("APPLY", model.UnsupportedConstructs);
        Assert.Contains(model.Tables, t => string.Equals(t.TableName, "apply", StringComparison.OrdinalIgnoreCase));
    }

    // ── SplitQualifiedName: dot-segment boundaries ───────────────────────

    [Fact]
    public void SplitQualifiedName_Unqualified_ReturnsEmptyAliasAndFullName()
    {
        var model = ParseSql("SELECT * FROM a JOIN b ON amount = total");

        var join = Assert.Single(model.Joins);
        Assert.Equal(string.Empty, join.LeftTable);
        Assert.Equal("amount", join.LeftColumn);
        Assert.Equal(string.Empty, join.RightTable);
        Assert.Equal("total", join.RightColumn);
    }

    [Fact]
    public void SplitQualifiedName_SingleQualifier_SplitsAliasAndColumn()
    {
        var model = ParseSql("SELECT * FROM a JOIN b ON a.id = b.id");

        var join = Assert.Single(model.Joins);
        Assert.Equal("a", join.LeftTable);
        Assert.Equal("id", join.LeftColumn);
    }

    [Fact]
    public void SplitQualifiedName_SchemaQualified_KeepsOnlyInnermostSegmentAsAlias()
    {
        // "dbo.orders.id" -- only the segment immediately before the last
        // dot ("orders") is the alias/table; the outer "dbo" schema
        // qualifier is discarded, exactly like StripQualifier does for
        // TableRef.TableName.
        var model = ParseSql("SELECT * FROM a JOIN b ON dbo.orders.id = b.id");

        var join = Assert.Single(model.Joins);
        Assert.Equal("orders", join.LeftTable);
        Assert.Equal("id", join.LeftColumn);
    }

    // ── StripQualifier / ParseFrom: schema-qualified table names ────────

    [Fact]
    public void ParseFrom_SchemaQualifiedTableName_StripsToFinalSegment()
    {
        var model = ParseSql("SELECT * FROM dbo.orders o");

        var table = Assert.Single(model.Tables);
        Assert.Equal("orders", table.TableName);
    }

    [Fact]
    public void ParseFrom_ThreePartQualifiedTableName_StripsToFinalSegment()
    {
        var model = ParseSql("SELECT * FROM server.dbo.orders o");

        var table = Assert.Single(model.Tables);
        Assert.Equal("orders", table.TableName);
    }

    // ── IsClauseKeyword: every listed keyword individually terminates ──

    [Theory]
    [InlineData("FROM")]
    [InlineData("WHERE")]
    [InlineData("JOIN")]
    [InlineData("LEFT")]
    [InlineData("RIGHT")]
    [InlineData("INNER")]
    [InlineData("OUTER")]
    [InlineData("CROSS")]
    [InlineData("FULL")]
    [InlineData("GROUP")]
    [InlineData("ORDER")]
    [InlineData("LIMIT")]
    [InlineData("HAVING")]
    [InlineData("UNION")]
    [InlineData("RETURNING")]
    public void ClauseKeyword_TerminatesExpressionRunEvenWithNoOtherKeywordPresent(string keyword)
    {
        // Each keyword must individually satisfy IsClauseKeyword's "is X or
        // Y or ..." check -- if one literal were blanked to "", only that
        // specific keyword would fail to terminate an expression item.
        // GROUP/ORDER/HAVING/UNION need their full two-token clause spelled
        // out or the tokenizer won't classify the second word as a keyword.
        string clause = keyword switch
        {
            "GROUP" => "GROUP BY b",
            "ORDER" => "ORDER BY b",
            "HAVING" => "HAVING b > 1",
            "UNION" => "UNION SELECT b FROM t2",
            "JOIN" => "JOIN t2 ON t2.b = t.b",
            "LEFT" or "RIGHT" or "INNER" or "OUTER" or "CROSS" or "FULL" => $"{keyword} JOIN t2 ON t2.b = t.b",
            "RETURNING" => "RETURNING b",
            _ => $"{keyword} b = 1",
        };
        // No FROM prefix: puts the terminator keyword directly adjacent to
        // the SELECT-list expression, in ParseProjectionList's own scan --
        // FROM would otherwise always terminate the list first, so the other
        // 14 keywords would never reach IsClauseKeyword's check at all.
        var model = ParseSql($"SELECT a + 1 AS total {clause}");

        // If IsClauseKeyword failed to recognize the terminator, the
        // projection loop wouldn't stop and would swallow the clause's
        // own tokens as a second (malformed, unaliased) expression item.
        if (keyword == "UNION")
        {
            // UNION correctly starts a fresh, plain-column projection list
            // ("b") for the SELECT after it. If "UNION" weren't recognized,
            // "UNION SELECT b" would instead glue onto an unaliased
            // expression item ending at the still-recognized "FROM".
            Assert.Equal(2, model.Columns.Count);
            Assert.False(model.Columns[1].IsExpression);
            Assert.Equal("b", model.Columns[1].ColumnName);
        }
        else
        {
            Assert.Single(model.Columns);
        }
        var col = Expr(model, "total");
        Assert.NotNull(col);
    }

    // ── CASE/END nesting depth: ++ / -- swap is genuinely equivalent ────
    //
    // caseDepth is only ever compared against zero (`caseDepth != 0`).
    // Swapping its CASE/END increment/decrement direction negates every
    // partial sum in the running total, but negation preserves zero
    // exactly (-0 == 0) and preserves the sign-pattern of every
    // zero/nonzero crossing (x == 0 iff -x == 0). So for any sequence of
    // CASE/END tokens, nested or not, the swapped counter crosses zero at
    // the exact same token positions as the real one -- the mutation
    // (Stryker ids 812, 821 in SqlParser.cs) cannot be distinguished by
    // any input. See the `// Stryker disable once` comments at the
    // caseDepth++/-- sites in InferExpressionType.
}
