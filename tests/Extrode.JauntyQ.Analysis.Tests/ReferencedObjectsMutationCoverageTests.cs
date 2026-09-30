using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Extrode.JauntyQ.Analysis.Impact;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

/// <summary>
/// Direct coverage for ReferencedObjects.Resolve gaps not exercised by
/// InternalPathCoverageTests/ImpactClassifierTests: the TableName/Alias/
/// TargetTable null-vs-empty-vs-set guards, the AddColumn empty/"*"/
/// qualified/unqualified branches, expression columns not double-counting
/// their own plain ColumnName, RETURNING/JOIN wiring, and the
/// ReferencedColumnComparer's Equals requiring both Table and Column to
/// match.
/// </summary>
[Trait("Category", "AuditRegression")]
public class ReferencedObjectsMutationCoverageTests
{
    private static QueryModel Model(StatementType type = StatementType.Select) =>
        new() { Name = "q", StatementType = type };

    // ── Tables loop: null/empty TableName is skipped ────────────────────

    [Fact]
    public void Resolve_TableWithEmptyName_IsSkippedEntirely()
    {
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "" });
        m.Tables.Add(new TableRef { TableName = "users" });
        m.Columns.Add(new ColumnRef { ColumnName = "id" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Single(refs.Tables);
        Assert.Contains("users", refs.Tables);
    }

    // ── Tables loop: alias registration is null/empty-guarded ───────────

    [Fact]
    public void Resolve_TableWithNoAlias_ColumnQualifiedByTableNameStillResolves()
    {
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "users", Alias = "" });
        m.Columns.Add(new ColumnRef { TableAlias = "users", ColumnName = "id" });

        var refs = ReferencedObjects.Resolve(m);

        var col = Assert.Single(refs.Columns);
        Assert.Equal("users", col.Table);
    }

    [Fact]
    public void Resolve_TableWithAlias_ColumnQualifiedByAliasResolvesToRealTableName()
    {
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "users", Alias = "u" });
        m.Columns.Add(new ColumnRef { TableAlias = "u", ColumnName = "id" });

        var refs = ReferencedObjects.Resolve(m);

        var col = Assert.Single(refs.Columns);
        Assert.Equal("users", col.Table);
    }

    [Fact]
    public void Resolve_TableWithAlias_UnqualifiedColumnFallsBackToRawAliasWhenNotRegistered()
    {
        // A column qualified by a name that was never registered in
        // aliasToTable (neither a real table name nor a declared alias)
        // falls back to using that raw string as the table.
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "users", Alias = "u" });
        m.Columns.Add(new ColumnRef { TableAlias = "ghost", ColumnName = "id" });

        var refs = ReferencedObjects.Resolve(m);

        var col = Assert.Single(refs.Columns);
        Assert.Equal("ghost", col.Table);
    }

    // ── TargetTable: null/empty/set ──────────────────────────────────────

    [Fact]
    public void Resolve_NullTargetTable_ContributesNoTableAndNoUnqualifiedAttribution()
    {
        var m = Model(StatementType.Update);
        m.TargetTable = null;
        m.Tables.Add(new TableRef { TableName = "users" });
        m.Columns.Add(new ColumnRef { ColumnName = "id" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Single(refs.Tables);
        Assert.Contains("users", refs.Tables);
    }

    [Fact]
    public void Resolve_EmptyTargetTable_ContributesNothing()
    {
        var m = Model(StatementType.Update);
        m.TargetTable = "";
        m.Columns.Add(new ColumnRef { ColumnName = "id" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Empty(refs.Tables);
    }

    [Fact]
    public void Resolve_SetTargetTable_IsAddedToTablesAndUnqualifiedColumnScope()
    {
        var m = Model(StatementType.Update);
        m.TargetTable = "orders";
        m.Columns.Add(new ColumnRef { ColumnName = "total" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Contains("orders", refs.Tables);
        var col = Assert.Single(refs.Columns);
        Assert.Equal("orders", col.Table);
        Assert.Equal("total", col.Column);
    }

    // ── AddColumn: empty / "*" / qualified / unqualified ─────────────────

    [Fact]
    public void Resolve_EmptyColumnName_ContributesNoColumn()
    {
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "users" });
        m.Columns.Add(new ColumnRef { ColumnName = "" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Empty(refs.Columns);
    }

    [Fact]
    public void Resolve_StarColumnName_ContributesNoColumn()
    {
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "users" });
        m.Columns.Add(new ColumnRef { ColumnName = "*" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Empty(refs.Columns);
    }

    [Fact]
    public void Resolve_QualifiedColumn_AddsOnlyTheOneResolvedPair_NotEveryInScopeTable()
    {
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "users", Alias = "u" });
        m.Tables.Add(new TableRef { TableName = "orders", Alias = "o" });
        m.Columns.Add(new ColumnRef { TableAlias = "u", ColumnName = "id" });

        var refs = ReferencedObjects.Resolve(m);

        var col = Assert.Single(refs.Columns);
        Assert.Equal("users", col.Table);
    }

    [Fact]
    public void Resolve_UnqualifiedColumn_AttributedToEveryInScopeTable()
    {
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "users" });
        m.Tables.Add(new TableRef { TableName = "orders" });
        m.Columns.Add(new ColumnRef { ColumnName = "id" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Equal(2, refs.Columns.Count);
        Assert.Contains(refs.Columns, c => c.Table == "users" && c.Column == "id");
        Assert.Contains(refs.Columns, c => c.Table == "orders" && c.Column == "id");
    }

    // ── Expression columns: no double-counting of the plain ColumnName ──

    [Fact]
    public void Resolve_ExpressionProjection_DoesNotAlsoAddItsOwnColumnNameField()
    {
        // ColumnRef.ColumnName/TableAlias are carried on the expression item
        // too (leftover from parsing), but for an IsExpression item they must
        // be ignored in favor of ReferencedColumns -- otherwise a spurious
        // third dependency pair would appear alongside the two real ones.
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "orders" });
        var expr = new ColumnRef
        {
            IsExpression = true, OutputAlias = "total_cost",
            TableAlias = "orders", ColumnName = "should_be_ignored"
        };
        expr.ReferencedColumns.Add(("orders", "price"));
        expr.ReferencedColumns.Add(("orders", "qty"));
        m.Columns.Add(expr);

        var refs = ReferencedObjects.Resolve(m);

        Assert.Equal(2, refs.Columns.Count);
        Assert.Contains(refs.Columns, c => c.Column == "price");
        Assert.Contains(refs.Columns, c => c.Column == "qty");
        Assert.DoesNotContain(refs.Columns, c => c.Column == "should_be_ignored");
    }

    [Fact]
    public void Resolve_ExpressionReturningProjection_DoesNotAlsoAddItsOwnColumnNameField()
    {
        var m = Model(StatementType.Insert);
        m.TargetTable = "orders";
        var expr = new ColumnRef
        {
            IsExpression = true, OutputAlias = "computed",
            TableAlias = "orders", ColumnName = "should_be_ignored"
        };
        expr.ReferencedColumns.Add(("orders", "total"));
        m.Returning.Add(expr);

        var refs = ReferencedObjects.Resolve(m);

        var col = Assert.Single(refs.Columns);
        Assert.Equal("total", col.Column);
    }

    // ── RETURNING (plain) and JOIN wiring ────────────────────────────────

    [Fact]
    public void Resolve_PlainReturningColumn_IsAddedAsADependency()
    {
        var m = Model(StatementType.Insert);
        m.TargetTable = "orders";
        m.Returning.Add(new ColumnRef { TableAlias = "orders", ColumnName = "order_id" });

        var refs = ReferencedObjects.Resolve(m);

        var col = Assert.Single(refs.Columns);
        Assert.Equal("orders", col.Table);
        Assert.Equal("order_id", col.Column);
    }

    [Fact]
    public void Resolve_Join_AddsBothLeftAndRightBoundColumns()
    {
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "orders", Alias = "o" });
        m.Tables.Add(new TableRef { TableName = "customers", Alias = "c" });
        m.Joins.Add(new JoinRef { LeftTable = "o", LeftColumn = "customer_id", RightTable = "c", RightColumn = "id" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Contains(refs.Columns, col => col.Table == "orders" && col.Column == "customer_id");
        Assert.Contains(refs.Columns, col => col.Table == "customers" && col.Column == "id");
    }

    // ── ReferencedColumnComparer: both Table AND Column must match ──────

    [Fact]
    public void Resolve_SameColumnNameOnDifferentTables_KeptAsDistinctEntries()
    {
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "users" });
        m.Tables.Add(new TableRef { TableName = "orders" });
        m.Columns.Add(new ColumnRef { TableAlias = "users", ColumnName = "id" });
        m.Columns.Add(new ColumnRef { TableAlias = "orders", ColumnName = "id" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Equal(2, refs.Columns.Count);
    }

    [Fact]
    public void Resolve_DifferentColumnNamesOnSameTable_KeptAsDistinctEntries()
    {
        var m = Model();
        m.Tables.Add(new TableRef { TableName = "users" });
        m.Columns.Add(new ColumnRef { TableAlias = "users", ColumnName = "id" });
        m.Columns.Add(new ColumnRef { TableAlias = "users", ColumnName = "name" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Equal(2, refs.Columns.Count);
    }

    // ── ReferencedColumnComparer.Equals: direct call, both fields required ──
    //
    // The two "kept as distinct entries" tests above go through a real
    // HashSet<ReferencedColumn>, so they only observe a broken Equals (e.g.
    // "&&" flipped to "||") when the two entries happen to land in the same
    // hash bucket -- otherwise Equals is never even called, and the mutant
    // survives by luck of the hash layout. Calling the private comparer's
    // Equals directly (via reflection, since ReferencedColumnComparer is a
    // private nested class) removes that luck: it always executes the exact
    // mutated line regardless of bucket placement.
    private static IEqualityComparer<ReferencedColumn> GetComparer()
    {
        var comparerType = typeof(ReferencedObjects).GetNestedType("ReferencedColumnComparer", BindingFlags.NonPublic)!;
        var instance = comparerType.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        return (IEqualityComparer<ReferencedColumn>)instance;
    }

    [Fact]
    public void Comparer_SameColumnDifferentTable_IsNotEqual()
    {
        var comparer = GetComparer();
        var x = new ReferencedColumn("users", "id");
        var y = new ReferencedColumn("orders", "id");

        Assert.False(comparer.Equals(x, y));
    }

    [Fact]
    public void Comparer_SameTableDifferentColumn_IsNotEqual()
    {
        var comparer = GetComparer();
        var x = new ReferencedColumn("users", "id");
        var y = new ReferencedColumn("users", "name");

        Assert.False(comparer.Equals(x, y));
    }

    [Fact]
    public void Comparer_SameTableAndColumn_IsEqual()
    {
        var comparer = GetComparer();
        var x = new ReferencedColumn("users", "id");
        var y = new ReferencedColumn("USERS", "ID");

        Assert.True(comparer.Equals(x, y));
    }
}
