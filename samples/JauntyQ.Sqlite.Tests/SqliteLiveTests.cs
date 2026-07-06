using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Sqlite.Tests;

/// <summary>
/// End-to-end proof that JauntyQ's generated SQLite code runs against a real
/// SQLite engine: typed ordinal reads, auto-CRUD, identity write-back via
/// RETURNING, and upsert via ON CONFLICT (the same emission path Postgres
/// uses). This is the second dialect validated live, beyond SQL Server.
/// </summary>
public class SqliteLiveTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fx;
    public SqliteLiveTests(SqliteFixture fx) => _fx = fx;

    [Fact]
    public void GetAll_ReturnsSeededRows()
    {
        var categories = _fx.Db.Categories.GetAll();
        Assert.Equal(3, categories.Count);
        Assert.Contains(categories, c => c.CategoryName == "Beverages");
    }

    [Fact]
    public void GetById_ReturnsTypedRow()
    {
        var beverages = _fx.Db.Categories.GetById(1);
        Assert.NotNull(beverages);
        Assert.Equal("Beverages", beverages!.CategoryName);
    }

    [Fact]
    public void GetById_Missing_ReturnsNull()
    {
        Assert.Null(_fx.Db.Categories.GetById(9999));
    }

    [Fact]
    public async Task GetAllAsync_Works()
    {
        // The fixture is shared across the class and other tests insert
        // shippers, so assert the seeded rows are present rather than an exact
        // count of a mutable table.
        var shippers = await _fx.Db.Shippers.GetAllAsync();
        Assert.True(shippers.Count >= 2);
        Assert.Contains(shippers, s => s.CompanyName == "Speedy Express");
        Assert.Contains(shippers, s => s.CompanyName == "United Package");
    }

    [Fact]
    public void Insert_ReturnsIdentity_ViaReturning()
    {
        // SQLite auto-CRUD Insert carries -- @identity and emits RETURNING.
        int newId = _fx.Db.Shippers.Insert("Federal Shipping", "(503) 555-9931");
        Assert.True(newId > 2, $"expected a new identity > 2, got {newId}");

        var fetched = _fx.Db.Shippers.GetById(newId);
        Assert.NotNull(fetched);
        Assert.Equal("Federal Shipping", fetched!.CompanyName);
    }

    [Fact]
    public void Update_ChangesRow()
    {
        int id = _fx.Db.Suppliers.Insert("Temp Supplier", "Seattle");
        // Synthetic Update parameter order is SET columns then PK:
        // (CompanyName, City, SupplierId).
        int affected = _fx.Db.Suppliers.Update("Renamed Supplier", "Portland", id);
        Assert.Equal(1, affected);

        var s = _fx.Db.Suppliers.GetById(id);
        Assert.Equal("Renamed Supplier", s!.CompanyName);
        Assert.Equal("Portland", s.City);
    }

    [Fact]
    public void Insert_NullNullableParam_BindsAsDbNull()
    {
        // Regression: a null argument for a nullable parameter must coalesce to
        // DBNull.Value. A bare C# null leaves the parameter value unset, which
        // the provider rejects ("must have either its DbType ... or its Value
        // set"). City is TEXT NULL, so null must round-trip as SQL NULL.
        int id = _fx.Db.Suppliers.Insert("NullCity Supplier", null);
        var s = _fx.Db.Suppliers.GetById(id);
        Assert.NotNull(s);
        Assert.Equal("NullCity Supplier", s!.CompanyName);
        Assert.Null(s.City);
    }

    [Fact]
    public void Delete_RemovesRow()
    {
        int id = _fx.Db.Suppliers.Insert("Disposable Supplier", "Nowhere");
        int affected = _fx.Db.Suppliers.Delete(id);
        Assert.Equal(1, affected);
        Assert.Null(_fx.Db.Suppliers.GetById(id));
    }

    [Fact]
    public void Upsert_InsertsThenUpdates_ViaOnConflict()
    {
        // Region has a non-identity PK, so it gets a synthetic Upsert
        // (INSERT ... ON CONFLICT (RegionId) DO UPDATE) on the SQLite path.
        int inserted = _fx.Db.Region.Upsert(3, "Northern");
        Assert.Equal(1, inserted);
        Assert.Equal("Northern", _fx.Db.Region.GetById(3)!.RegionDescription);

        int updated = _fx.Db.Region.Upsert(3, "Northern (revised)");
        Assert.Equal(1, updated);
        Assert.Equal("Northern (revised)", _fx.Db.Region.GetById(3)!.RegionDescription);
    }

    [Fact]
    public void FkLoader_GetBySupplierId()
    {
        // Products.SupplierId is a foreign key -> synthetic GetBySupplierId.
        var fromSupplier1 = _fx.Db.Products.GetBySupplierId(1);
        Assert.Equal(3, fromSupplier1.Count); // Chai, Chang, Aniseed Syrup
    }

    [Fact]
    public void UserQuery_GetByCategory()
    {
        var beverages = _fx.Db.Products.GetByCategory(1);
        Assert.Equal(2, beverages.Count); // Chai, Chang
        Assert.All(beverages, p => Assert.Equal(1, p.CategoryId));
    }

    [Fact]
    public void TypedReads_NullableAndDecimal()
    {
        var chai = _fx.Db.Products.GetById(1);
        Assert.NotNull(chai);
        Assert.Equal(18.00m, chai!.UnitPrice);
        Assert.False(chai.Discontinued);
    }

    [Fact]
    public void NullNullableValueTypeColumns_ReadBackAsNull_NotDefault()
    {
        // Regression: generated readers emitted `IsDBNull(n) ? default : Get...`
        // for nullable VALUE-type columns. C# infers the conditional's natural
        // type from the non-null arm (decimal/int), so `default` was 0m / 0 —
        // never null. Insert a row whose nullable value-type columns are all
        // NULL and prove they round-trip as null, not zero.
        int newId = _fx.Db.Products.Insert("NullFields Product", null, null, null, false);

        var fetched = _fx.Db.Products.GetById(newId);
        Assert.NotNull(fetched);
        Assert.Equal("NullFields Product", fetched!.ProductName);
        Assert.Null(fetched.UnitPrice);   // decimal?  — would be 0m under the bug
        Assert.Null(fetched.SupplierId);  // int?      — would be 0 under the bug
        Assert.Null(fetched.CategoryId);  // int?      — would be 0 under the bug
    }
}
