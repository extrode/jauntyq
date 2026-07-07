using System;
using System.Collections.Generic;
using System.Linq;
using JauntyQ.Generated;
using JauntyQ.EShopOnWeb.SqlServer.Tests.Domain;

namespace JauntyQ.EShopOnWeb.SqlServer.Tests.Repositories;

/// <summary>
/// Direct port of eShopOnWeb's OrderService.CreateOrderAsync (stress point:
/// checkout multi-table transaction + rollback). Confirmed against the
/// source app: does not delete the basket afterward - the port must not
/// add that behavior for fidelity.
/// </summary>
public sealed class CheckoutService
{
    private readonly JauntyDb _db;
    private readonly OrderRepository _orderRepository;

    public CheckoutService(JauntyDb db)
    {
        _db = db;
        _orderRepository = new OrderRepository(db);
    }

    public int CreateOrder(int basketId, string buyerId, Domain.Address shippingAddress)
    {
        var basket = _db.Basket.GetById(basketId);
        if (basket is null)
            throw new InvalidOperationException($"Basket {basketId} not found.");

        var basketItems = _db.BasketItem.GetByBasketId(basketId);
        if (basketItems.Count == 0)
            throw new InvalidOperationException($"Basket {basketId} has no items.");

        var catalogItemIds = basketItems.Select(bi => bi.CatalogItemId).ToList();
        var catalogItems = CatalogItemRawQueries.GetByIds(_db, catalogItemIds);
        var catalogItemsById = catalogItems.ToDictionary(ci => ci.Id);

        var orderItems = new List<Domain.OrderItem>(basketItems.Count);
        foreach (var basketItem in basketItems)
        {
            var catalogItem = catalogItemsById[basketItem.CatalogItemId];
            var itemOrdered = new Domain.CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new Domain.OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity));
        }

        var order = new Domain.Order(0, buyerId, DateTimeOffset.UtcNow, shippingAddress, orderItems);

        using var tx = _db.BeginTransaction();
        int orderId = _orderRepository.Add(order);
        tx.Commit();
        return orderId;
    }
}
