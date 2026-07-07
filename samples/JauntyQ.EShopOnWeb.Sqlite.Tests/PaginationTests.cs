using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JauntyQ.EShopOnWeb.Sqlite.Tests;

/// <summary>
/// Hardens CatalogFilterPaginatedSpecification (spec 3) - the Skip/Take ->
/// LIMIT/OFFSET stress point from the handoff notes.
/// Confirms parameterized LIMIT/OFFSET (not just literal constants) works
/// and cross-checks page contents against GetCount for boundary
/// correctness. 12 seeded catalog_item rows total.
/// </summary>
public class PaginationTests : IClassFixture<EShopOnWebSqliteFixture>
{
    private readonly EShopOnWebSqliteFixture _fx;

    public PaginationTests(EShopOnWebSqliteFixture fx) => _fx = fx;

    [Theory]
    [InlineData(0, 5, 5)]
    [InlineData(5, 5, 5)]
    [InlineData(10, 5, 2)]
    [InlineData(12, 5, 0)]
    [InlineData(0, 12, 12)]
    [InlineData(0, 100, 12)]
    public void GetFilteredPaginated_PageSizes_MatchExpectedCounts(int skip, int take, int expectedCount)
    {
        if (!_fx.Available) return;

        var page = _fx.Db.CatalogItem.GetFilteredPaginated(BrandId: null, TypeId: null, Skip: skip, Take: take);

        Assert.Equal(expectedCount, page.Count);
    }

    [Fact]
    public void GetFilteredPaginated_SkipBeyondTotal_ReturnsEmpty()
    {
        if (!_fx.Available) return;

        var page = _fx.Db.CatalogItem.GetFilteredPaginated(BrandId: null, TypeId: null, Skip: 999, Take: 5);

        Assert.Empty(page);
    }

    [Fact]
    public void GetFilteredPaginated_AllPagesCoverEveryRowExactlyOnce()
    {
        if (!_fx.Available) return;

        var total = _fx.Db.CatalogItem.GetCount(BrandId: null, TypeId: null)!.Total;
        Assert.Equal(12, total);

        var seenIds = new List<int>();
        for (int skip = 0; skip < total; skip += 5)
        {
            var page = _fx.Db.CatalogItem.GetFilteredPaginated(BrandId: null, TypeId: null, Skip: skip, Take: 5);
            seenIds.AddRange(page.Select(p => p.Id));
        }

        Assert.Equal(total, seenIds.Count);
        Assert.Equal(seenIds.Distinct().Count(), seenIds.Count);
    }

    [Fact]
    public void GetFilteredPaginated_FilteredByBrand_CountMatchesPageTotal()
    {
        if (!_fx.Available) return;

        var total = _fx.Db.CatalogItem.GetCount(BrandId: 2, TypeId: null)!.Total;
        var page = _fx.Db.CatalogItem.GetFilteredPaginated(BrandId: 2, TypeId: null, Skip: 0, Take: 100);

        Assert.Equal(total, page.Count);
        Assert.All(page, p => Assert.Equal(2, p.CatalogBrandId));
    }

    [Fact]
    public void GetFilteredPaginated_PagesAreOrderedConsistently()
    {
        if (!_fx.Available) return;

        var page1 = _fx.Db.CatalogItem.GetFilteredPaginated(BrandId: null, TypeId: null, Skip: 0, Take: 6);
        var page2 = _fx.Db.CatalogItem.GetFilteredPaginated(BrandId: null, TypeId: null, Skip: 6, Take: 6);
        var all = _fx.Db.CatalogItem.GetFilteredPaginated(BrandId: null, TypeId: null, Skip: 0, Take: 12);

        var combined = page1.Select(p => p.Id).Concat(page2.Select(p => p.Id)).ToList();
        Assert.Equal(all.Select(p => p.Id).ToList(), combined);
    }
}
