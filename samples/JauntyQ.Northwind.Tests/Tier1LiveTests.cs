using JauntyQ.Generated;
using Microsoft.Data.SqlClient;
using Xunit;

namespace JauntyQ.Northwind.Tests;

/// <summary>
/// Live coverage for Tier 1 features: db-level transactions and
/// identity-returning INSERT (synthetic Shippers.Insert carries -- @identity).
/// Uses fresh connections so the shared fixture connection is untouched.
/// </summary>
public class Tier1LiveTests
{
    private static JauntyDb FreshDb() => new(new SqlConnection(NorthwindFixture.ConnectionString));

    [Fact]
    public void IdentityInsert_InsideTransaction_ReturnsNewId_RollbackDiscards()
    {
        var db = FreshDb();
        int before = db.Shippers.GetAll().Count;

        using (var tx = db.BeginTransaction())
        {
            int newId = db.Shippers.Insert("Tier1 Test Shipper", "(555) 000-1111");
            Assert.True(newId > 0, $"expected a database-assigned id, got {newId}");

            // visible inside the transaction
            var inserted = db.Shippers.GetById(newId);
            Assert.NotNull(inserted);
            Assert.Equal("Tier1 Test Shipper", inserted.CompanyName);

            tx.Rollback();
        }

        Assert.Equal(before, db.Shippers.GetAll().Count);
    }

    [Fact]
    public void Transaction_DisposeWithoutCommit_RollsBack()
    {
        var db = FreshDb();
        int before = db.Shippers.GetAll().Count;

        using (db.BeginTransaction())
        {
            db.Shippers.Insert("Disposable Shipper", "(555) 000-2222");
            // no Commit -> Dispose rolls back
        }

        Assert.Equal(before, db.Shippers.GetAll().Count);
    }

    [Fact]
    public void Transaction_Commit_Persists()
    {
        var db = FreshDb();
        int newId;

        using (var tx = db.BeginTransaction())
        {
            newId = db.Shippers.Insert("Committed Shipper", "(555) 000-3333");
            tx.Commit();
        }

        try
        {
            var persisted = db.Shippers.GetById(newId);
            Assert.NotNull(persisted);
            Assert.Equal("Committed Shipper", persisted.CompanyName);
        }
        finally
        {
            // cleanup outside any transaction
            db.Shippers.Delete(newId);
        }

        Assert.Null(db.Shippers.GetById(newId));
    }

    [Fact]
    public async Task TransactionAsync_IdentityInsertAsync_Rollback()
    {
        var db = FreshDb();
        int before = (await db.Shippers.GetAllAsync()).Count;

        using (var tx = await db.BeginTransactionAsync())
        {
            int newId = await db.Shippers.InsertAsync("Async Shipper", "(555) 000-4444");
            Assert.True(newId > 0);
            tx.Rollback();
        }

        Assert.Equal(before, (await db.Shippers.GetAllAsync()).Count);
    }

    [Fact]
    public void BeginTransaction_WhileActive_Throws()
    {
        var db = FreshDb();
        using var tx = db.BeginTransaction();
        Assert.Throws<InvalidOperationException>(() => db.BeginTransaction());
        tx.Rollback();
    }
}
