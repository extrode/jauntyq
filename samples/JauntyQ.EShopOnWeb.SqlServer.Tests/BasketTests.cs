using Xunit;

namespace JauntyQ.EShopOnWeb.SqlServer.Tests;

/// <summary>
/// Covers BasketWithItemsSpecification (1a: by id, 1b: by buyerId) from
/// eShopOnWeb, plus the write path (Basket/Insert, BasketItem/Insert)
/// used by later checkout milestones.
/// </summary>
public class BasketTests : IClassFixture<EShopOnWebSqlServerFixture>
{
    private readonly EShopOnWebSqlServerFixture _fx;

    public BasketTests(EShopOnWebSqlServerFixture fx) => _fx = fx;

    // Spec 1a: BasketWithItemsSpecification(basketId)
    [Fact]
    public void GetById_ReturnsBasketAndItems()
    {
        if (!_fx.Available) return;

        var basketId = _fx.Db.Basket.Insert("buyer-1a");
        _fx.Db.BasketItem.Insert(basketId, CatalogItemId: 1, UnitPrice: 8.5m, Quantity: 2);
        _fx.Db.BasketItem.Insert(basketId, CatalogItemId: 2, UnitPrice: 19.5m, Quantity: 1);

        var basket = _fx.Db.Basket.GetById(basketId);
        Assert.NotNull(basket);
        Assert.Equal("buyer-1a", basket!.BuyerId);

        var items = _fx.Db.BasketItem.GetByBasketId(basketId);
        Assert.Equal(2, items.Count);
        Assert.Equal(1, items[0].CatalogItemId);
        Assert.Equal(2, items[1].CatalogItemId);
    }

    [Fact]
    public void GetById_NoMatch_ReturnsNull()
    {
        if (!_fx.Available) return;

        var basket = _fx.Db.Basket.GetById(999999);

        Assert.Null(basket);
    }

    // Spec 1b: BasketWithItemsSpecification(buyerId)
    [Fact]
    public void GetByBuyerId_ReturnsBasketAndItems()
    {
        if (!_fx.Available) return;

        var basketId = _fx.Db.Basket.Insert("buyer-1b");
        _fx.Db.BasketItem.Insert(basketId, CatalogItemId: 3, UnitPrice: 6.0m, Quantity: 5);

        var basket = _fx.Db.Basket.GetByBuyerId("buyer-1b");
        Assert.NotNull(basket);
        Assert.Equal(basketId, basket!.Id);

        var items = _fx.Db.BasketItem.GetByBasketId(basket.Id);
        Assert.Single(items);
        Assert.Equal(5, items[0].Quantity);
    }

    [Fact]
    public void GetByBuyerId_NoMatch_ReturnsNull()
    {
        if (!_fx.Available) return;

        var basket = _fx.Db.Basket.GetByBuyerId("nonexistent-buyer");

        Assert.Null(basket);
    }
}
