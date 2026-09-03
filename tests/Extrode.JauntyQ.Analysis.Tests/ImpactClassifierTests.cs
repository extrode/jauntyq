using Extrode.JauntyQ.Analysis.Diff;
using Extrode.JauntyQ.Analysis.Impact;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class ImpactClassifierTests
{
    // ── schema builders ───────────────────────────────────
    private static DatabaseSchema Schema(params TableSchema[] tables)
    {
        var s = new DatabaseSchema { Dialect = "sqlserver" };
        foreach (var t in tables)
            s.Tables[t.Name] = t;
        return s;
    }

    private static TableSchema Table(string name, params ColumnSchema[] cols)
    {
        var t = new TableSchema { Name = name };
        foreach (var c in cols)
            t.Columns[c.Name] = c;
        return t;
    }

    private static ColumnSchema Col(string name, string dbType = "int", int? maxLen = null, bool isComputed = false) =>
        new() { Name = name, DbType = dbType, MaxLength = maxLen, IsComputed = isComputed };

    // ── query builders ────────────────────────────────────
    private static QueryModel Select(string table, params string[] columns)
    {
        var m = new QueryModel { Name = "q", StatementType = StatementType.Select };
        m.Tables.Add(new TableRef { TableName = table });
        foreach (var c in columns)
            m.Columns.Add(new ColumnRef { ColumnName = c });
        return m;
    }

    private static QueryImpactInput Input(string method, QueryModel m, bool pre = false, bool unmodeled = false) =>
        new($"{method}.sql", method, ReferencedObjects.Resolve(m), pre, unmodeled);

    private static SchemaDelta Delta(DatabaseSchema baseline, DatabaseSchema effective) =>
        StructuralSchemaDiff.Compute(baseline, effective);

    private static MigrationImpactReport Run(SchemaDelta delta, params QueryImpactInput[] inputs) =>
        ImpactClassifier.Classify(delta, inputs, "schema/jaunty.schema.json", new[] { "0001.sql" });

    // ── tests ─────────────────────────────────────────────

    [Fact]
    public void Safe_WhenNoReferencedObjectChanged()
    {
        var baseline = Schema(Table("users", Col("id"), Col("name", "varchar")));
        var effective = Schema(Table("users", Col("id"), Col("name", "varchar"), Col("added", "int")));

        var report = Run(Delta(baseline, effective), Input("User.Get", Select("users", "id", "name")));

        Assert.Equal(Classification.Safe, Assert.Single(report.Entries).Classification);
    }

    [Fact]
    public void Breaking_WhenReferencedColumnRemoved()
    {
        var baseline = Schema(Table("users", Col("id"), Col("legacy_flag", "bit")));
        var effective = Schema(Table("users", Col("id")));

        var report = Run(Delta(baseline, effective), Input("User.Get", Select("users", "id", "legacy_flag")));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.legacy_flag" && r.ChangeKind == "removed");
    }

    [Fact]
    public void Breaking_WhenReferencedTableRemoved()
    {
        var baseline = Schema(Table("users", Col("id")), Table("legacy", Col("id")));
        var effective = Schema(Table("users", Col("id")));

        var report = Run(Delta(baseline, effective), Input("Legacy.Get", Select("legacy", "id")));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "legacy" && r.ChangeKind == "removed");
    }

    [Fact]
    public void Risky_WhenReferencedColumnModified()
    {
        var baseline = Schema(Table("users", Col("id"), Col("name", "varchar", 100)));
        var effective = Schema(Table("users", Col("id"), Col("name", "varchar", 50)));

        var report = Run(Delta(baseline, effective), Input("User.Get", Select("users", "id", "name")));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Risky, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.name" && r.ChangeKind == "maxLength");
    }

    [Fact]
    public void Risky_WhenReferencedColumnBecomesComputed()
    {
        // Before this fix, IsComputed was never compared by StructuralSchemaDiff
        // at all, so a migration turning "total" into a GENERATED column
        // produced zero delta for it and any query referencing it was
        // classified Safe -- even though a write to that column would now be
        // rejected by the database at runtime.
        var baseline = Schema(Table("orders", Col("id"), Col("total", "decimal")));
        var effective = Schema(Table("orders", Col("id"), Col("total", "decimal", isComputed: true)));

        var report = Run(Delta(baseline, effective), Input("Order.Get", Select("orders", "id", "total")));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Risky, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "orders.total" && r.ChangeKind == "computed");
    }

    [Fact]
    public void Risky_WhenUnmodeledStatementTouchesReferencedTable()
    {
        var baseline = Schema(Table("users", Col("id")));
        var effective = Schema(Table("users", Col("id")));

        var report = Run(Delta(baseline, effective),
            Input("User.Get", Select("users", "id"), unmodeled: true));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Risky, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.ChangeKind == "unmodeled");
    }

    [Fact]
    public void ExactK_DroppedColumnReferencedByTwoQueries() // SC-001
    {
        var baseline = Schema(Table("users", Col("id"), Col("email", "varchar")));
        var effective = Schema(Table("users", Col("id")));
        var delta = Delta(baseline, effective);

        var report = Run(delta,
            Input("User.GetEmail", Select("users", "id", "email")),
            Input("User.SearchEmail", Select("users", "email")),
            Input("User.GetId", Select("users", "id")));

        Assert.Equal(2, report.Count(Classification.Breaking));
        Assert.Equal(1, report.Count(Classification.Safe));
        Assert.Equal(0, report.Count(Classification.Risky));
    }

    [Fact]
    public void NoFalseSafe_AnyQueryTouchingChangedObjectIsNotSafe() // SC-004
    {
        var baseline = Schema(Table("t", Col("a"), Col("b", "varchar", 100), Col("c")));
        var effective = Schema(Table("t", Col("a"), Col("b", "varchar", 40))); // b modified, c removed
        var delta = Delta(baseline, effective);

        // every query references at least one changed object (b or c)
        var inputs = new[]
        {
            Input("Q.b", Select("t", "b")),
            Input("Q.c", Select("t", "c")),
            Input("Q.bc", Select("t", "b", "c")),
            Input("Q.abc", Select("t", "a", "b", "c")),
        };
        var report = Run(delta, inputs);

        Assert.All(report.Entries, e => Assert.NotEqual(Classification.Safe, e.Classification));
    }

    [Fact]
    public void PreexistingError_NotAttributedToMigration() // SC-006
    {
        var baseline = Schema(Table("users", Col("id")));
        var effective = Schema(Table("users"));  // id dropped

        var report = Run(Delta(baseline, effective),
            Input("User.Get", Select("users", "id"), pre: true));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Safe, entry.Classification);
        Assert.Empty(entry.Reasons);
    }

    [Fact]
    public void Highest_ReflectsMostSevereEntry()
    {
        var baseline = Schema(Table("users", Col("id"), Col("email", "varchar", 100)));
        var effective = Schema(Table("users", Col("id"), Col("email", "varchar", 50)));

        var report = Run(Delta(baseline, effective), Input("User.Get", Select("users", "email")));

        Assert.Equal(Classification.Risky, report.Highest);
    }

    // ── ReferencedObjects.Resolve: WHERE/ORDER BY/subquery/CTE coverage ────
    // A query that only compares/sorts/joins-inside-a-nested-scope a column
    // (never projects or returns it) used to be resolved with NO dependency
    // on that column at all, so a migration that changed it produced a false
    // SAFE verdict instead of BREAKING/RISKY.

    [Fact]
    public void NotSafe_WhenOnlyWhereClauseReferencesRemovedColumn()
    {
        var baseline = Schema(Table("users", Col("id"), Col("status", "varchar")));
        var effective = Schema(Table("users", Col("id")));

        var m = Select("users", "id");
        m.Parameters.Add(new ParameterRef { BoundTableAlias = "users", BoundColumnName = "status", ComparisonOp = "=" });

        var report = Run(Delta(baseline, effective), Input("User.Get", m));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.status" && r.ChangeKind == "removed");
    }

    [Fact]
    public void NotSafe_WhenOnlyLiteralBindingReferencesModifiedColumn()
    {
        var baseline = Schema(Table("users", Col("id"), Col("name", "varchar", 100)));
        var effective = Schema(Table("users", Col("id"), Col("name", "varchar", 50)));

        var m = Select("users", "id");
        m.Literals.Add(new LiteralBinding { Kind = LiteralKind.String, Value = "'x'", BoundTableAlias = "users", BoundColumnName = "name" });

        var report = Run(Delta(baseline, effective), Input("User.Get", m));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Risky, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.name" && r.ChangeKind == "maxLength");
    }

    [Fact]
    public void NotSafe_WhenOnlyOrderByReferencesModifiedColumn()
    {
        var baseline = Schema(Table("users", Col("id"), Col("name", "varchar", 100)));
        var effective = Schema(Table("users", Col("id"), Col("name", "varchar", 50)));

        var m = Select("users", "id");
        m.OrderBy.Add(new OrderByRef { BoundTableAlias = "users", BoundColumnName = "name", Kind = OrderByItemKind.PlainColumn });

        var report = Run(Delta(baseline, effective), Input("User.Get", m));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Risky, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.name" && r.ChangeKind == "maxLength");
    }

    [Fact]
    public void OrderBy_NonPlainColumnKinds_LeaveNoDanglingDependency()
    {
        // Expression/Ordinal/ProjectedAlias items carry an empty bound
        // alias/column; AddColumn must no-op on them rather than attributing
        // the sort to every in-scope table.
        var baseline = Schema(Table("users", Col("id"), Col("name", "varchar")));
        var effective = Schema(Table("users", Col("id"), Col("name", "varchar", 999)));

        var m = Select("users", "id");
        m.OrderBy.Add(new OrderByRef { Kind = OrderByItemKind.Ordinal });
        m.OrderBy.Add(new OrderByRef { Kind = OrderByItemKind.Expression });
        m.OrderBy.Add(new OrderByRef { Kind = OrderByItemKind.ProjectedAlias });

        var report = Run(Delta(baseline, effective), Input("User.Get", m));

        Assert.Equal(Classification.Safe, Assert.Single(report.Entries).Classification);
    }

    [Fact]
    public void NotSafe_WhenOnlyOrderByExpressionReferencesRemovedColumn()
    {
        // Same defect class as the SELECT-list expression gap below, extended
        // to ORDER BY: an Expression-kind item's ReferencedColumns must be
        // resolved too, not just its (always-empty-for-Expression) bound
        // alias/column -- otherwise a migration that removed a column
        // referenced only inside an ORDER BY expression (e.g.
        // "order by count(email)") produced a false SAFE verdict.
        var baseline = Schema(Table("users", Col("id"), Col("email", "varchar")));
        var effective = Schema(Table("users", Col("id")));

        var m = Select("users", "id");
        var orderByExpr = new OrderByRef { Kind = OrderByItemKind.Expression };
        orderByExpr.ReferencedColumns.Add(("", "email"));
        m.OrderBy.Add(orderByExpr);

        var report = Run(Delta(baseline, effective), Input("User.Get", m));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.email" && r.ChangeKind == "removed");
    }

    [Fact]
    public void NotSafe_WhenOnlyWhereInSubqueryReferencesRemovedColumn()
    {
        var baseline = Schema(Table("users", Col("id")), Table("orders", Col("id"), Col("user_id"), Col("discontinued", "bit")));
        var effective = Schema(Table("users", Col("id")), Table("orders", Col("id"), Col("user_id")));

        var m = Select("users", "id");
        var subquery = Select("orders", "user_id");
        subquery.Parameters.Add(new ParameterRef { BoundTableAlias = "orders", BoundColumnName = "discontinued", ComparisonOp = "=" });
        m.Subqueries.Add(new SubqueryRef { Kind = SubqueryKind.In, Body = subquery });

        var report = Run(Delta(baseline, effective), Input("User.Get", m));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "orders.discontinued" && r.ChangeKind == "removed");
    }

    // ── ReferencedObjects.Resolve: expression-projection column coverage ──
    // A projection expression that isn't the narrow "SUM/AVG over a single
    // bare column" shape used to record ZERO column dependency at all, so a
    // migration that removed/changed a column referenced only inside such an
    // expression produced a false SAFE verdict.

    [Fact]
    public void NotSafe_WhenOnlyCountExpressionReferencesRemovedColumn()
    {
        var baseline = Schema(Table("users", Col("id"), Col("email", "varchar")));
        var effective = Schema(Table("users", Col("id")));

        var m = Select("users", "id");
        m.Columns.Add(new ColumnRef
        {
            IsExpression = true,
            ExpressionSql = "count ( email )",
            OutputAlias = "cnt",
            ReferencedColumns = { ("", "email") }
        });

        var report = Run(Delta(baseline, effective), Input("User.CountEmail", m));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.email" && r.ChangeKind == "removed");
    }

    [Fact]
    public void NotSafe_WhenOnlyArithmeticExpressionReferencesModifiedColumn()
    {
        var baseline = Schema(Table("orders", Col("id"), Col("price", "decimal"), Col("qty")));
        var effective = Schema(Table("orders", Col("id"), Col("price", "decimal"), Col("qty")));
        baseline.Tables["orders"].Columns["price"].Precision = 10;
        baseline.Tables["orders"].Columns["price"].Scale = 2;
        effective.Tables["orders"].Columns["price"].Precision = 8;
        effective.Tables["orders"].Columns["price"].Scale = 2;

        var m = Select("orders", "id");
        m.Columns.Add(new ColumnRef
        {
            IsExpression = true,
            ExpressionSql = "orders . price * orders . qty",
            OutputAlias = "total",
            ReferencedColumns = { ("orders", "price"), ("orders", "qty") }
        });

        var report = Run(Delta(baseline, effective), Input("Order.Total", m));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Risky, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "orders.price" && r.ChangeKind == "precisionScale");
    }

    [Fact]
    public void NotSafe_WhenOnlyExpressionInReturningReferencesRemovedColumn()
    {
        var baseline = Schema(Table("users", Col("id"), Col("email", "varchar")));
        var effective = Schema(Table("users", Col("id")));

        var m = Select("users", "id");
        m.Returning.Add(new ColumnRef
        {
            IsExpression = true,
            ExpressionSql = "count ( email )",
            OutputAlias = "cnt",
            ReferencedColumns = { ("", "email") }
        });

        var report = Run(Delta(baseline, effective), Input("User.Insert", m));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.email" && r.ChangeKind == "removed");
    }

    // ── ReferencedObjects.Resolve: PerfHint-only column coverage ──────────
    // WHERE-clause shapes where the referenced column isn't directly
    // adjacent to the comparison operator (fn(column) = ..., or a bare
    // column = column implicit-join/correlated-subquery back-reference) are
    // captured by the parser only as PerfHint entries (for the JNT8xxx
    // performance analyzer) -- never as a ParameterRef or LiteralBinding.
    // Before this fix, ReferencedObjects.ResolveInto never read
    // model.PerfHints at all, so a migration that only touched a column
    // referenced solely via one of these shapes produced a false SAFE
    // verdict.

    [Fact]
    public void NotSafe_WhenOnlyFunctionOnColumnPerfHintReferencesRemovedColumn()
    {
        // WHERE UPPER(email) = 'X' : the column is wrapped in a function
        // call, so neither ExtractParameterBindings nor ExtractLiteralBindings
        // binds it (the token before '=' is ')', not the identifier) -- only
        // ExtractPerfHints captures it, as PerfHintKind.FunctionOnColumn.
        var baseline = Schema(Table("users", Col("id"), Col("email", "varchar")));
        var effective = Schema(Table("users", Col("id")));

        var m = Select("users", "id");
        m.PerfHints.Add(new PerfHint
        {
            Kind = PerfHintKind.FunctionOnColumn,
            FunctionName = "UPPER",
            BoundTableAlias = "users",
            BoundColumnName = "email"
        });

        var report = Run(Delta(baseline, effective), Input("User.Get", m));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.email" && r.ChangeKind == "removed");
    }

    [Fact]
    public void NotSafe_WhenOnlyColumnComparedToColumnPerfHintReferencesRemovedColumn()
    {
        // A correlated subquery back-reference (e.g. inside EXISTS/IN),
        // `oi.order_id = o.id`: both sides are bare identifiers, so neither
        // side is a parameter or a literal -- only ExtractPerfHints captures
        // either column, as two PerfHintKind.ColumnComparedToColumn entries
        // (one per side).
        var baseline = Schema(Table("orders", Col("id"), Col("user_id")),
                              Table("order_items", Col("id"), Col("order_id")));
        var effective = Schema(Table("orders", Col("id"), Col("user_id")),
                               Table("order_items", Col("id")));

        var subquery = Select("order_items", "id");
        subquery.PerfHints.Add(new PerfHint
        {
            Kind = PerfHintKind.ColumnComparedToColumn,
            BoundTableAlias = "order_items",
            BoundColumnName = "order_id"
        });
        subquery.PerfHints.Add(new PerfHint
        {
            Kind = PerfHintKind.ColumnComparedToColumn,
            BoundTableAlias = "orders",
            BoundColumnName = "id"
        });

        var m = Select("orders", "id");
        m.Subqueries.Add(new SubqueryRef { Kind = SubqueryKind.Exists, Body = subquery });

        var report = Run(Delta(baseline, effective), Input("Order.Get", m));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "order_items.order_id" && r.ChangeKind == "removed");
    }

    [Fact]
    public void NotSafe_WhenOnlyCteBodyReferencesModifiedColumn()
    {
        var baseline = Schema(Table("orders", Col("id"), Col("total", "decimal", null)));
        var effective = Schema(Table("orders", Col("id"), Col("total", "decimal", null)));
        baseline.Tables["orders"].Columns["total"].Precision = 10;
        baseline.Tables["orders"].Columns["total"].Scale = 2;
        effective.Tables["orders"].Columns["total"].Precision = 8;
        effective.Tables["orders"].Columns["total"].Scale = 2;

        var cteBody = Select("orders", "id", "total");
        var outer = new QueryModel { Name = "q", StatementType = StatementType.Select };
        outer.Ctes.Add(new CteRef { Name = "recent", Body = cteBody });
        outer.Tables.Add(new TableRef { TableName = "recent" });
        outer.Columns.Add(new ColumnRef { ColumnName = "id" }); // outer only projects id, never total

        var report = Run(Delta(baseline, effective), Input("Recent.Get", outer));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Risky, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "orders.total" && r.ChangeKind == "precisionScale");
    }

    [Fact]
    public void DescribeChange_UnrecognizedColumnChangeKind_StillProducesANonEmptyEffect()
    {
        var baseline = Col("name", "varchar", 100);
        var effective = Col("name", "varchar", 100);
        var change = new ColumnChange("name", baseline, effective,
            new[] { (ColumnChangeKind)999 });
        var delta = new SchemaDelta(
            new string[0], new string[0],
            new[] { new TableDelta("users", new string[0], new string[0], new[] { change }) });

        var report = Run(delta, Input("User.Get", Select("users", "name")));

        var reason = Assert.Single(Assert.Single(report.Entries).Reasons);
        Assert.Equal("changed", reason.ChangeKind);
        Assert.False(string.IsNullOrWhiteSpace(reason.Effect),
            "DescribeChange contributed nothing for a ColumnChangeKind it does not recognize, so the "
            + "reason's Effect is empty: the CLI prints 'users.name: ' with nothing after the colon and "
            + "the JNT9004 build message emits a trailing space. WireKind already degrades an unknown "
            + "kind to the token \"changed\"; DescribeChange must not be the one site that says nothing.");
    }
}
