using Xunit;

namespace JauntyQ.EShopOnWeb.SqlServer.Tests;

[Collection("EShopOnWebSqlServer")]
public class SmokeTests
{
    private readonly EShopOnWebSqlServerFixture _fx;

    public SmokeTests(EShopOnWebSqlServerFixture fx) => _fx = fx;

    [SkippableFact]
    public void AutoCrudSurfaceExists()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var brands = _fx.Db.CatalogBrand.GetAll();
        var types = _fx.Db.CatalogType.GetAll();
        var items = _fx.Db.CatalogItem.GetAll();

        Assert.True(brands.Count >= 5, $"expected the 5 seeded brands, saw {brands.Count}");
        Assert.True(types.Count >= 4, $"expected the 4 seeded types, saw {types.Count}");
        Assert.True(items.Count >= 12, $"expected the 12 seeded items, saw {items.Count}");
    }
}
