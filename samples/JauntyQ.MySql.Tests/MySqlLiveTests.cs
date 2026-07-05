using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.MySql.Tests;

/// <summary>
/// End-to-end proof that JauntyQ's generated MySQL code runs against a real
/// MySQL engine (via Testcontainers). MySQL is the most distinct dialect:
/// identity returns via SELECT last_insert_id() and upsert uses ON DUPLICATE
/// KEY UPDATE. Soft-skips when Docker is unavailable.
/// </summary>
public class MySqlLiveTests : IClassFixture<MySqlFixture>
{
    private readonly MySqlFixture _fx;
    public MySqlLiveTests(MySqlFixture fx) => _fx = fx;

    [Fact]
    public void GetAll_ReturnsSeededRows()
    {
        if (!_fx.Available) return;
        var categories = _fx.Db.Categories.GetAll();
        Assert.Equal(3, categories.Count);
        Assert.Contains(categories, c => c.CategoryName == "Beverages");
    }

    [Fact]
    public void GetById_ReturnsTypedRow()
    {
        if (!_fx.Available) return;
        var beverages = _fx.Db.Categories.GetById(1);
        Assert.NotNull(beverages);
        Assert.Equal("Beverages", beverages!.CategoryName);
    }

    [Fact]
    public async Task GetAllAsync_Works()
    {
        if (!_fx.Available) return;
        var shippers = await _fx.Db.Shippers.GetAllAsync();
        Assert.True(shippers.Count >= 2);
        Assert.Contains(shippers, s => s.CompanyName == "Speedy Express");
    }

    [Fact]
    public void Insert_ReturnsIdentity_ViaLastInsertId()
    {
        if (!_fx.Available) return;
        // MySQL Insert returns the key via SELECT last_insert_id().
        int newId = _fx.Db.Shippers.Insert("Federal Shipping", "(503) 555-9931");
        Assert.True(newId > 2, $"expected a new identity > 2, got {newId}");
        Assert.Equal("Federal Shipping", _fx.Db.Shippers.GetById(newId)!.CompanyName);
    }

    [Fact]
    public void Update_And_Delete()
    {
        if (!_fx.Available) return;
        int id = _fx.Db.Suppliers.Insert("Temp Supplier", "Seattle");
        int updated = _fx.Db.Suppliers.Update("Renamed Supplier", "Portland", id);
        Assert.Equal(1, updated);
        Assert.Equal("Renamed Supplier", _fx.Db.Suppliers.GetById(id)!.CompanyName);

        int deleted = _fx.Db.Suppliers.Delete(id);
        Assert.Equal(1, deleted);
        Assert.Null(_fx.Db.Suppliers.GetById(id));
    }

    [Fact]
    public void Upsert_InsertsThenUpdates_ViaOnDuplicateKey()
    {
        if (!_fx.Available) return;
        // First upsert inserts (1 row affected). MySQL reports 2 affected rows
        // for an ON DUPLICATE KEY UPDATE that updates an existing row, so the
        // second call asserts >= 1 rather than an exact count.
        int inserted = _fx.Db.Region.Upsert(3, "Northern");
        Assert.True(inserted >= 1);
        Assert.Equal("Northern", _fx.Db.Region.GetById(3)!.RegionDescription);

        int changed = _fx.Db.Region.Upsert(3, "Northern (revised)");
        Assert.True(changed >= 1);
        Assert.Equal("Northern (revised)", _fx.Db.Region.GetById(3)!.RegionDescription);
    }

    [Fact]
    public void FkLoader_And_TypedReads()
    {
        if (!_fx.Available) return;
        var fromSupplier1 = _fx.Db.Products.GetBySupplierId(1);
        Assert.Equal(3, fromSupplier1.Count);

        var chai = _fx.Db.Products.GetById(1);
        Assert.Equal(18.00m, chai!.UnitPrice);
        // discontinued is TINYINT -> short in the generated POCO (0 = false).
        Assert.Equal((short)0, chai.Discontinued);
    }
}
