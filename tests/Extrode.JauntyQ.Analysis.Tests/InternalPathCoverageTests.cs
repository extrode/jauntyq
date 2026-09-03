using System.Linq;
using Extrode.JauntyQ.Analysis.Impact;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

/// <summary>
/// Coverage for internal/private code paths that are only reachable through
/// their public callers: the <c>ReferencedColumnComparer</c> dedup used inside
/// <see cref="ReferencedObjects.Resolve"/>, and the <c>Unsupported</c> fallback
/// in <see cref="MigrationParser.Parse"/>. Both are private members, so they are
/// exercised through the public API rather than exposed — testing real usage.
/// </summary>
public class InternalPathCoverageTests
{
    // ── ReferencedColumnComparer (dedup inside Resolve) ────────────────────
    [Fact]
    public void Resolve_DedupesCaseInsensitiveDuplicateColumns()
    {
        // Two references to the same column (differing only in case) must
        // collapse to one ReferencedColumn — that collapse fires the comparer's
        // Equals (case-insensitive) on a hash-equal pair.
        var m = new QueryModel { Name = "q", StatementType = StatementType.Select };
        m.Tables.Add(new TableRef { TableName = "users" });
        m.Columns.Add(new ColumnRef { ColumnName = "id" });
        m.Columns.Add(new ColumnRef { ColumnName = "ID" });

        var refs = ReferencedObjects.Resolve(m);

        var idCols = refs.Columns.Where(c =>
            string.Equals(c.Column, "id", System.StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(idCols);
        Assert.Equal("users", idCols[0].Table);
    }

    [Fact]
    public void Resolve_KeepsDistinctColumnsSeparate()
    {
        var m = new QueryModel { Name = "q", StatementType = StatementType.Select };
        m.Tables.Add(new TableRef { TableName = "users" });
        m.Columns.Add(new ColumnRef { ColumnName = "id" });
        m.Columns.Add(new ColumnRef { ColumnName = "name" });

        var refs = ReferencedObjects.Resolve(m);

        Assert.Equal(2, refs.Columns.Count);
    }

    // ── MigrationParser.Unsupported fallback ───────────────────────────────
    [Fact]
    public void Parse_ArbitraryDdl_ClassifiedUnsupported()
    {
        // A rename is real DDL the simulator cannot model → Unsupported (JNT9001).
        var stmts = MigrationParser.Parse("EXEC sp_rename 'users.id', 'user_id', 'COLUMN';");

        Assert.Single(stmts);
        Assert.Equal(MigrationStatementKind.Unsupported, stmts[0].Kind);
        Assert.NotEqual(string.Empty, stmts[0].RawText);
    }

    [Fact]
    public void Parse_MalformedCreateTable_FallsBackToUnsupported()
    {
        // CREATE TABLE with no column list cannot be modeled → Unsupported.
        var stmts = MigrationParser.Parse("CREATE TABLE users;");

        Assert.Single(stmts);
        Assert.Equal(MigrationStatementKind.Unsupported, stmts[0].Kind);
    }
}
