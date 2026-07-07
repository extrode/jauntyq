using Xunit;

namespace JauntyQ.EShopOnWeb.Postgres.Tests;

/// <summary>
/// Covers CatalogFilterSpecification (2), CatalogFilterPaginatedSpecification
/// (3), and CatalogItemNameSpecification (4) from eShopOnWeb, ported as
/// hand-written .sql per the test log's Part 1 scope decisions.
/// </summary>
public class CatalogTests : IClassFixture<EShopOnWebPostgresFixture>
{
    private readonly EShopOnWebPostgresFixture _fx;

    public CatalogTests(EShopOnWebPostgresFixture fx) => _fx = fx;

    // Spec 2: CatalogFilterSpecification
    [Fact]
    public void GetFiltered_BrandOnly()
    {
        if (!_fx.Available) return;

        var items = _fx.Db.CatalogItem.GetFiltered(BrandId: 2, TypeId: null);

        Assert.All(items, i => Assert.Equal(2, i.CatalogBrandId));
        Assert.True(items.Count > 0);
    }

    [Fact]
    public void GetFiltered_TypeOnly()
    {
        if (!_fx.Available) return;

        var items = _fx.Db.CatalogItem.GetFiltered(BrandId: null, TypeId: 2);

        Assert.All(items, i => Assert.Equal(2, i.CatalogTypeId));
        Assert.True(items.Count > 0);
    }

    [Fact]
    public void GetFiltered_BothNull_ReturnsAll()
    {
        if (!_fx.Available) return;

        var items = _fx.Db.CatalogItem.GetFiltered(BrandId: null, TypeId: null);

        Assert.Equal(12, items.Count);
    }

    [Fact]
    public void GetFiltered_BothSet()
    {
        if (!_fx.Available) return;

        var items = _fx.Db.CatalogItem.GetFiltered(BrandId: 2, TypeId: 2);

        Assert.All(items, i =>
        {
            Assert.Equal(2, i.CatalogBrandId);
            Assert.Equal(2, i.CatalogTypeId);
        });
    }

    // Spec 3: CatalogFilterPaginatedSpecification
    [Fact]
    public void GetFilteredPaginated_FirstPage()
    {
        if (!_fx.Available) return;

        var page = _fx.Db.CatalogItem.GetFilteredPaginated(BrandId: null, TypeId: null, Skip: 0, Take: 5);
        var total = _fx.Db.CatalogItem.GetCount(BrandId: null, TypeId: null);

        Assert.Equal(5, page.Count);
        Assert.Equal(12, total!.Total);
    }

    [Fact]
    public void GetFilteredPaginated_LastPartialPage()
    {
        if (!_fx.Available) return;

        // 12 rows, page size 5 -> pages of 5, 5, 2
        var page = _fx.Db.CatalogItem.GetFilteredPaginated(BrandId: null, TypeId: null, Skip: 10, Take: 5);

        Assert.Equal(2, page.Count);
    }

    [Fact]
    public void GetFilteredPaginated_TakeZero_SpecialCaseInCallerCode()
    {
        if (!_fx.Available) return;

        // Mirrors OrderService's `if (take == 0) take = int.MaxValue;` -
        // the special-case lives in caller code, not SQL.
        int take = 0;
        if (take == 0) take = int.MaxValue;

        var page = _fx.Db.CatalogItem.GetFilteredPaginated(BrandId: null, TypeId: null, Skip: 0, Take: take);

        Assert.Equal(12, page.Count);
    }

    // Spec 4: CatalogItemNameSpecification
    [Fact]
    public void GetByName_ExactMatch()
    {
        if (!_fx.Available) return;

        var item = _fx.Db.CatalogItem.GetByName(".NET Black & White Mug");

        Assert.NotNull(item);
        Assert.Equal(".NET Black & White Mug", item!.Name);
    }

    [Fact]
    public void GetByName_NoMatch()
    {
        if (!_fx.Available) return;

        var item = _fx.Db.CatalogItem.GetByName("Nonexistent Product");

        Assert.Null(item);
    }
}
