using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.MySql.Tests;

/// <summary>
/// Round-33 audit regression: MySqlConnector's <c>MySqlBulkCopy</c> (the MySQL
/// BulkInsert fast path, emitted by <c>EmitBulkInsertBodyMySql</c> in
/// CodeEmitter.Part13.cs) issues <c>LOAD DATA ... IGNORE INTO TABLE ...</c>
/// under the hood. MySQL's <c>IGNORE</c> downgrades data-integrity violations
/// -- e.g. a NULL value supplied to a NOT NULL column -- from a hard error to
/// a session warning, silently substituting the column's implicit default
/// (empty string for VARCHAR) instead of rejecting the row.
/// <c>MySqlBulkCopy.WriteToServer</c> reports success regardless, so before this
/// round's fix, <c>BulkInsert</c> would return the full row count with no
/// exception even though a row's data had been silently corrupted server-side
/// -- verified live: <c>SHOW WARNINGS</c> immediately after the copy returned
/// MySQL error 1263, "Column set to default value; NULL supplied to NOT NULL
/// column 'company_name'", while <c>BulkInsert</c> itself threw nothing.
///
/// This is a genuine divergence from the sibling fast paths: SqlServer's
/// <c>SqlBulkCopy</c> and PostgreSQL's binary <c>COPY</c> both have the server
/// reject such a row outright (confirmed live for SqlServer in the companion
/// <c>JauntyQ.EShopOnWeb.SqlServer.Tests.BulkInsertAtomicityTests</c>), as does
/// the portable ExecuteNonQuery-loop fallback (CodeEmitter.Part12.cs), which
/// sends a real ADO.NET NULL parameter and trips the constraint normally.
///
/// The fix (CodeEmitter.Part13.cs) makes the MySQL fast path check
/// <c>SHOW WARNINGS</c> immediately after <c>WriteToServer</c>/
/// <c>WriteToServerAsync</c> and throw <see cref="System.InvalidOperationException"/>
/// if the session reports anything at Warning level -- converting the
/// previously-silent corruption into a loud, attributable failure. This does
/// NOT make the MySQL fast path atomic: IGNORE has already let MySQL commit
/// every row (including the corrupted one) by the time the warning is
/// observed, so the exception is a signal, not a rollback. A caller that needs
/// true atomicity must wrap the call in its own transaction and roll back on
/// this exception, exactly as it would for a portable-path constraint
/// violation.
/// </summary>
[Collection("MySql")]
public class BulkInsertAtomicityTests
{
    private readonly MySqlFixture _fx;
    public BulkInsertAtomicityTests(MySqlFixture fx) => _fx = fx;

    [SkippableFact]
    public void BulkInsert_NotNullViolation_ThrowsInsteadOfSilentlyCoercingToDefault()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = new[]
        {
            new Supplier { CompanyName = "R33-My-Atomicity-A", City = "Portland" },
            new Supplier { CompanyName = "R33-My-Atomicity-B", City = "Seattle" },
            new Supplier { CompanyName = "R33-My-Atomicity-C", City = "Denver" },
            new Supplier { CompanyName = null!, City = "Austin" }, // NOT NULL violation
            new Supplier { CompanyName = "R33-My-Atomicity-E", City = "Boise" },
        };

        var ex = Assert.Throws<System.InvalidOperationException>(
            () => _fx.Db.Suppliers.BulkInsert(rows));

        // The exception must name the actual mechanism (a server warning, not
        // a generic failure) so a caller can distinguish "IGNORE silently
        // altered data" from an ordinary ADO.NET exception.
        Assert.Contains("warning", ex.Message, System.StringComparison.OrdinalIgnoreCase);

        // Document the residual limitation explicitly (see class doc): the
        // fix surfaces the corruption, it does not undo it. All 5 rows,
        // including the NOT-NULL-violating one (persisted with CompanyName
        // coerced to ""), are already committed by the time the exception is
        // observed.
        var after = _fx.Db.Suppliers.GetAll();
        Assert.Contains(after, s => s.CompanyName == "" && s.City == "Austin");
    }

    /// <summary>
    /// Coordinator-caught regression in the fix above: the new <c>__warnCmd</c>
    /// (the <c>SHOW WARNINGS</c> check in <c>EmitBulkInsertBodyMySql</c>) was
    /// initially emitted without <c>.Transaction</c> set. MySqlConnector requires
    /// every command run while a connection has an active transaction to be
    /// explicitly associated with it, or <c>ExecuteReader</c>/<c>ExecuteReaderAsync</c>
    /// throws <c>InvalidOperationException: The transaction associated with this
    /// command is not the connection's active transaction</c> -- unconditionally,
    /// even for rows with zero constraint violations. That would have broken
    /// every transactional MySQL <c>BulkInsert</c> call, which is worse than the
    /// silent-corruption bug this round set out to fix. The corrected emitter sets
    /// <c>__warnCmd.Transaction = tx</c> when <c>tx</c> is non-null, mirroring the
    /// established convention already used by every other emitted command that
    /// might run inside a caller-supplied or ambient transaction (see
    /// CodeEmitter.Part3.cs, Part5.cs, Part8.cs, Part11.cs, Part12.cs).
    /// </summary>
    [SkippableFact]
    public void BulkInsert_InsideActiveTransaction_CleanRows_DoesNotThrow()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        using var tx = _fx.Db.BeginTransaction();
        var rows = new[]
        {
            new Supplier { CompanyName = "R33-Tx-Clean-A", City = "Portland" },
        };

        int affected = _fx.Db.Suppliers.BulkInsert(rows);
        Assert.Equal(1, affected);
        tx.Rollback();
    }

    /// <summary>
    /// Companion to <see cref="BulkInsert_InsideActiveTransaction_CleanRows_DoesNotThrow"/>:
    /// confirms the transaction-association fix doesn't undermine this round's
    /// original fix -- a genuine NOT NULL violation still surfaces as a thrown
    /// <see cref="System.InvalidOperationException"/> when the call happens inside
    /// an active transaction, not just in the transaction-free case covered by
    /// <see cref="BulkInsert_NotNullViolation_ThrowsInsteadOfSilentlyCoercingToDefault"/>.
    /// </summary>
    [SkippableFact]
    public void BulkInsert_InsideActiveTransaction_NotNullViolation_StillThrows()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        using var tx = _fx.Db.BeginTransaction();
        var rows = new[]
        {
            new Supplier { CompanyName = "R33-Tx-Violation-A", City = "Portland" },
            new Supplier { CompanyName = null!, City = "Austin" },
        };

        Assert.Throws<System.InvalidOperationException>(
            () => _fx.Db.Suppliers.BulkInsert(rows));
        tx.Rollback();
    }
}
