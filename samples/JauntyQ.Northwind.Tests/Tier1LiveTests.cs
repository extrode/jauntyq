using JauntyQ.Generated;
using Microsoft.Data.SqlClient;
using Xunit;

namespace JauntyQ.Northwind.Tests;

/// <summary>
/// Live coverage for Tier 1 features: db-level transactions and
/// identity-returning INSERT (synthetic Shippers.Insert carries -- @identity).
/// Uses fresh connections so the shared fixture connection is untouched.
/// </summary>
[Collection("Northwind")]
public class Tier1LiveTests
{
    private readonly NorthwindFixture _fixture;
    public Tier1LiveTests(NorthwindFixture fixture) => _fixture = fixture;

    private static JauntyDb FreshDb() => new(new SqlConnection(NorthwindFixture.ConnectionString));

    [SkippableFact]
    public void IdentityInsert_InsideTransaction_ReturnsNewId_RollbackDiscards()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
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

    [SkippableFact]
    public void Transaction_DisposeWithoutCommit_RollsBack()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var db = FreshDb();
        int before = db.Shippers.GetAll().Count;

        using (db.BeginTransaction())
        {
            db.Shippers.Insert("Disposable Shipper", "(555) 000-2222");
            // no Commit -> Dispose rolls back
        }

        Assert.Equal(before, db.Shippers.GetAll().Count);
    }

    [SkippableFact]
    public void Transaction_Commit_Persists()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
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

    [SkippableFact]
    public async Task TransactionAsync_IdentityInsertAsync_Rollback()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
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

    [SkippableFact]
    public void BeginTransaction_WhileActive_Throws()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var db = FreshDb();
        using var tx = db.BeginTransaction();
        Assert.Throws<InvalidOperationException>(() => db.BeginTransaction());
        tx.Rollback();
    }

    // ── Tier 1.5: canonical POCOs, FK loaders, Upsert ──────

    [SkippableFact]
    public void CanonicalPocoTypes_AreTheApi()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
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

    [SkippableFact]
    public void FkLoader_GetByCategoryId_ReturnsCategoryProducts()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var db = FreshDb();

        List<Product> beverages = db.Products.GetByCategoryId(1);

        Assert.NotEmpty(beverages);
        Assert.Contains(beverages, p => p.ProductName == "Chai");
    }

    [SkippableFact]
    public void PocoOverloads_ReadModifyWrite_UpdateAndUpsert()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var db = FreshDb();

        using (var tx = db.BeginTransaction())
        {
            // the canonical read-modify-write flow (Shippers: synthetic
            // Update, so the POCO overload exists; Products' user-written
            // Update.sql owns its own signature and gets none - by design)
            Shipper? shipper = db.Shippers.GetById(1);
            Assert.NotNull(shipper);
            shipper.CompanyName = "Speedy Express (renamed)";
            int updated = db.Shippers.Update(shipper);
            Assert.Equal(1, updated);
            Assert.Equal("Speedy Express (renamed)", db.Shippers.GetById(1)!.CompanyName);

            // upsert an existing row via its POCO
            Customer? alfki = db.Customers.GetById("ALFKI");
            Assert.NotNull(alfki);
            alfki.CompanyName = "Alfreds Umbenannt";
            Assert.True(db.Customers.Upsert(alfki) >= 1);
            Assert.Equal("Alfreds Umbenannt", db.Customers.GetById("ALFKI")!.CompanyName);

            tx.Rollback();
        }

        Assert.Equal("Speedy Express", db.Shippers.GetById(1)!.CompanyName);
        Assert.Equal("Alfreds Futterkiste", db.Customers.GetById("ALFKI")!.CompanyName);
    }

    [SkippableFact]
    public void PocoInsert_WritesIdentityBack()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var db = FreshDb();

        using (var tx = db.BeginTransaction())
        {
            var shipper = new Shipper { CompanyName = "Poco Shipping Co", Phone = "(555) 777-8888" };
            Assert.Equal(0, shipper.ShipperId);

            int id = db.Shippers.Insert(shipper);

            Assert.True(id > 0);
            Assert.Equal(id, shipper.ShipperId); // identity written back
            Assert.NotNull(db.Shippers.GetById(id));

            db.Shippers.Delete(shipper); // POCO delete, by PK
            Assert.Null(db.Shippers.GetById(id));

            tx.Rollback();
        }
    }
}
