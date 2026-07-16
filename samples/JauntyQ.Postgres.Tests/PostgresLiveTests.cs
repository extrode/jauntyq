using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Postgres.Tests;

/// <summary>
/// End-to-end proof that JauntyQ's generated PostgreSQL code runs against a
/// real Postgres engine (via Testcontainers): snake_case-to-PascalCase name
/// mapping, RETURNING identity, ON CONFLICT upsert, and typed no-box params.
/// Soft-skips when Docker is unavailable.
/// </summary>
public class PostgresLiveTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;
    public PostgresLiveTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public void GetAll_ReturnsSeededRows()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var categories = _fx.Db.Categories.GetAll();
        Assert.Equal(3, categories.Count);
        Assert.Contains(categories, c => c.CategoryName == "Beverages");
    }

    [SkippableFact]
    public void GetById_ReturnsTypedRow()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var beverages = _fx.Db.Categories.GetById(1);
        Assert.NotNull(beverages);
        Assert.Equal("Beverages", beverages!.CategoryName);
    }

    [SkippableFact]
    public async Task GetAllAsync_Works()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var shippers = await _fx.Db.Shippers.GetAllAsync();
        Assert.True(shippers.Count >= 2);
        Assert.Contains(shippers, s => s.CompanyName == "Speedy Express");
    }

    [SkippableFact]
    public void Insert_ReturnsIdentity_ViaReturning()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        int newId = _fx.Db.Shippers.Insert("Federal Shipping", "(503) 555-9931");
        Assert.True(newId > 2, $"expected a new identity > 2, got {newId}");
        var fetched = _fx.Db.Shippers.GetById(newId);
        Assert.Equal("Federal Shipping", fetched!.CompanyName);
    }

    [SkippableFact]
    public void Update_And_Delete()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        int id = _fx.Db.Suppliers.Insert("Temp Supplier", "Seattle");
        // Synthetic Update parameter order is SET columns then PK.
        int updated = _fx.Db.Suppliers.Update("Renamed Supplier", "Portland", id);
        Assert.Equal(1, updated);
        Assert.Equal("Renamed Supplier", _fx.Db.Suppliers.GetById(id)!.CompanyName);

        int deleted = _fx.Db.Suppliers.Delete(id);
        Assert.Equal(1, deleted);
        Assert.Null(_fx.Db.Suppliers.GetById(id));
    }

    [SkippableFact]
    public void Upsert_InsertsThenUpdates_ViaOnConflict()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        int inserted = _fx.Db.Region.Upsert(3, "Northern");
        Assert.Equal(1, inserted);
        Assert.Equal("Northern", _fx.Db.Region.GetById(3)!.RegionDescription);

        int changed = _fx.Db.Region.Upsert(3, "Northern (revised)");
        Assert.Equal(1, changed);
        Assert.Equal("Northern (revised)", _fx.Db.Region.GetById(3)!.RegionDescription);
    }

    [SkippableFact]
    public void FkLoader_And_TypedReads()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var fromSupplier1 = _fx.Db.Products.GetBySupplierId(1);
        Assert.Equal(3, fromSupplier1.Count);

        var chai = _fx.Db.Products.GetById(1);
        Assert.Equal(18.00m, chai!.UnitPrice);
        Assert.False(chai.Discontinued);
    }
}
