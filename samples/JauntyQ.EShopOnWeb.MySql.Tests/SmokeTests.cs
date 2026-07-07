using Xunit;

namespace JauntyQ.EShopOnWeb.MySql.Tests;

public class SmokeTests : IClassFixture<EShopOnWebMySqlFixture>
{
    private readonly EShopOnWebMySqlFixture _fx;

    public SmokeTests(EShopOnWebMySqlFixture fx) => _fx = fx;

    [Fact]
    public void AutoCrudSurfaceExists()
    {
        if (!_fx.Available) return;

        var brands = _fx.Db.CatalogBrand.GetAll();
        var types = _fx.Db.CatalogType.GetAll();
        var items = _fx.Db.CatalogItem.GetAll();

        Assert.Equal(5, brands.Count);
        Assert.Equal(4, types.Count);
        Assert.Equal(12, items.Count);
    }
}
