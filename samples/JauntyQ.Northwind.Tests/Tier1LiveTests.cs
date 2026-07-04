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

    // ── Tier 1.5: canonical POCOs, FK loaders, Upsert ──────

    [Fact]
    public void CanonicalPocoTypes_AreTheApi()
    {
        var db = FreshDb();

        // explicit types: full-row queries return the singular POCO
        Shipper? one = db.Shippers.GetById(1);
        List<Shipper> all = db.Shippers.GetAll();

        Assert.NotNull(one);
        Assert.Equal("Speedy Express", one.CompanyName);
        Assert.Equal(3, all.Count);

        // already-singular table name falls back to <Entity>Row
        RegionRow? region = db.Region.GetById(1);
        Assert.NotNull(region);
    }

    [Fact]
    public void FkLoader_GetByCategoryId_ReturnsCategoryProducts()
    {
        var db = FreshDb();

        List<Product> beverages = db.Products.GetByCategoryId(1);

        Assert.NotEmpty(beverages);
        Assert.Contains(beverages, p => p.ProductName == "Chai");
    }

    [Fact]
    public void Upsert_UpdatesExisting_InsertsNew_InsideRollback()
    {
        var db = FreshDb();

        using (var tx = db.BeginTransaction())
        {
            // existing key -> update path
            int updated = db.Customers.Upsert(
                "ALFKI", "Alfreds Umbenannt", null, null, null, null, null, null, null, null, null);
            Assert.True(updated >= 1);
            var alfki = db.Customers.GetById("ALFKI");
            Assert.NotNull(alfki);
            Assert.Equal("Alfreds Umbenannt", alfki.CompanyName);

            // new key -> insert path
            int inserted = db.Customers.Upsert(
                "ZZ999", "Zebra Zone Ltd", null, null, null, null, null, null, null, null, null);
            Assert.True(inserted >= 1);
            Assert.NotNull(db.Customers.GetById("ZZ999"));

            tx.Rollback();
        }

        var restored = db.Customers.GetById("ALFKI");
        Assert.NotNull(restored);
        Assert.Equal("Alfreds Futterkiste", restored.CompanyName);
        Assert.Null(db.Customers.GetById("ZZ999"));
    }
}
