using Xunit;

namespace JauntyQ.EShopOnWeb.Sqlite.Tests;

public class SmokeTests : IClassFixture<EShopOnWebSqliteFixture>
{
    private readonly EShopOnWebSqliteFixture _fx;

    public SmokeTests(EShopOnWebSqliteFixture fx) => _fx = fx;

    [SkippableFact]
    public void AutoCrudSurfaceExists()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var brands = _fx.Db.CatalogBrand.GetAll();
        var types = _fx.Db.CatalogType.GetAll();
        var items = _fx.Db.CatalogItem.GetAll();

        Assert.Equal(5, brands.Count);
        Assert.Equal(4, types.Count);
        Assert.Equal(12, items.Count);
    }
}
