using Extrode.JauntyQ.Generated;
using Xunit;

namespace Microsoft.eShopWeb.SqlServer.Tests;

/// <summary>
/// Exercises the SQL Server provider-native BulkInsert fast path
/// (Microsoft.Data.SqlClient.SqlBulkCopy over the AOT-safe DbDataReader adapter)
/// against a real SQL Server engine: many rows inserted, row count returned (read
/// back from the adapter since SqlBulkCopy provides none), spot-check value
/// round-trips, sync + async. Soft-skips when Docker is absent.
/// </summary>
[Collection("EShopOnWebSqlServer")]
public class BulkInsertTests
{
    private readonly EShopOnWebSqlServerFixture _fx;
    public BulkInsertTests(EShopOnWebSqlServerFixture fx) => _fx = fx;

    [SkippableFact]
    public void BulkInsert_InsertsAllRows_ViaSqlBulkCopy()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int before = _fx.Db.CatalogBrand.GetAll().Count;
        var rows = new[]
        {
            new CatalogBrandRow { Brand = "Bulk Brand A" },
            new CatalogBrandRow { Brand = "Bulk Brand B" },
            new CatalogBrandRow { Brand = "Bulk Brand C" },
        };

        int affected = _fx.Db.CatalogBrand.BulkInsert(rows);
        Assert.Equal(3, affected);

        var after = _fx.Db.CatalogBrand.GetAll();
        Assert.Equal(before + 3, after.Count);
        Assert.Contains(after, b => b.Brand == "Bulk Brand B");
    }

    [SkippableFact]
    public async Task BulkInsertAsync_Works()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int before = _fx.Db.CatalogType.GetAll().Count;
        var rows = new[]
        {
            new CatalogTypeRow { Type = "Async Type 1" },
            new CatalogTypeRow { Type = "Async Type 2" },
        };

        int affected = await _fx.Db.CatalogType.BulkInsertAsync(rows);
        Assert.Equal(2, affected);
        Assert.Equal(before + 2, _fx.Db.CatalogType.GetAll().Count);
    }

    [SkippableFact]
    public void BulkInsert_Empty_ReturnsZero()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        Assert.Equal(0, _fx.Db.CatalogBrand.BulkInsert(System.Array.Empty<CatalogBrandRow>()));
    }
}
