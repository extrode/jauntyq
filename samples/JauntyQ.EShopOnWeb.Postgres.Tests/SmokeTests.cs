using Xunit;

namespace JauntyQ.EShopOnWeb.Postgres.Tests;

[Collection("EShopOnWebPostgres")]
public class SmokeTests
{
    private readonly EShopOnWebPostgresFixture _fx;

    public SmokeTests(EShopOnWebPostgresFixture fx) => _fx = fx;

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
