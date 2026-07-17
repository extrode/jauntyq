using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.EShopOnWeb.SqlServer.Tests;

/// <summary>
/// Round-33 audit probe: BulkInsert's own XML doc (CodeEmitter.Part11.cs's
/// EmitBulkInsert) promises "inserts many rows atomically". The SQL Server
/// fast path (CodeEmitter.Part13.cs's EmitBulkInsertBodySqlServer) forwards
/// only an *ambient* transaction (_db?.CurrentTransaction, or the caller's
/// explicit `transaction` parameter on the static overload) into SqlBulkCopy's
/// constructor and never opens SqlBulkCopyOptions.UseInternalTransaction nor a
/// transaction of its own -- unlike the portable ExecuteNonQuery-loop fallback
/// (CodeEmitter.Part12.cs), which explicitly begins its own transaction "so
/// the whole batch commits atomically" when none is ambient. This test calls
/// BulkInsert with zero ambient transaction (the common, simplest calling
/// pattern -- exactly what BulkInsertTests.cs's own existing tests use) and a
/// batch whose middle row violates a NOT NULL constraint, to observe whether
/// the rows preceding the violation are left committed despite the overall
/// call throwing.
/// </summary>
public class BulkInsertAtomicityTests : IClassFixture<EShopOnWebSqlServerFixture>
{
    private readonly EShopOnWebSqlServerFixture _fx;
    public BulkInsertAtomicityTests(EShopOnWebSqlServerFixture fx) => _fx = fx;

    [SkippableFact]
    public void BulkInsert_NoAmbientTransaction_MidBatchConstraintViolation_LeavesPriorRowsCommitted()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int before = _fx.Db.CatalogBrand.GetAll().Count;

        var rows = new[]
        {
            new CatalogBrandRow { Brand = "R33-Atomicity-A" },
            new CatalogBrandRow { Brand = "R33-Atomicity-B" },
            new CatalogBrandRow { Brand = "R33-Atomicity-C" },
            new CatalogBrandRow { Brand = null! }, // NOT NULL violation
            new CatalogBrandRow { Brand = "R33-Atomicity-E" },
        };

        Assert.ThrowsAny<System.Exception>(() => _fx.Db.CatalogBrand.BulkInsert(rows));

        int after = _fx.Db.CatalogBrand.GetAll().Count;

        // BulkInsert's own doc comment promises the whole batch is atomic. If
        // that promise held for the SqlServer fast path absent an ambient
        // transaction, a failed batch would leave the table untouched.
        Assert.Equal(before, after);
    }
}
