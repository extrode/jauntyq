using System;
using System.Collections.Generic;
using System.Linq;
using Extrode.JauntyQ.Generated;

namespace Microsoft.eShopWeb.MySql.Tests.Repositories;

/// <summary>
/// Reconstructs the hand-written Domain.Order aggregate (nested Address,
/// List&lt;OrderItem&gt;) from the flattened orders/order_item rows. The
/// generated row types are named Order/OrderItemRow (auto-CRUD singularizes
/// "orders" to "Order" but falls back to "OrderItemRow" for the
/// already-singular "order_item" - see the test log), so they're
/// referenced unqualified here via the Extrode.JauntyQ.Generated using, distinct from
/// the qualified Domain.Order/Domain.OrderItem types.
/// </summary>
public sealed class OrderRepository
{
    private readonly JauntyDb _db;

    public OrderRepository(JauntyDb db) => _db = db;

    public Domain.Order? GetById(int id)
    {
        var row = _db.Orders.GetById(id);
        if (row is null) return null;
        var items = _db.OrderItem.GetByOrderId(new[] { row.Id });
        return ToDomain(row, items);
    }

    public List<Domain.Order> GetByBuyerId(string buyerId)
    {
        var rows = _db.Orders.GetByBuyerId(buyerId);
        var itemsByOrderId = _db.OrderItem.GetByOrderId(rows.Select(r => r.Id).ToArray())
            .GroupBy(i => i.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<Domain.Order>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(ToDomain(row, itemsByOrderId.TryGetValue(row.Id, out var items) ? items : new List<OrderItemRow>()));
        }
        return result;
    }

    public int Add(Domain.Order order)
    {
        int orderId = _db.Orders.Insert(
            BuyerId: order.BuyerId,
            OrderDate: order.OrderDate.UtcDateTime,
            ShipToStreet: order.ShipToAddress.Street,
            ShipToCity: order.ShipToAddress.City,
            ShipToState: order.ShipToAddress.State,
            ShipToCountry: order.ShipToAddress.Country,
            ShipToZipcode: order.ShipToAddress.ZipCode);

        foreach (var item in order.OrderItems)
        {
            _db.OrderItem.Insert(
                OrderId: orderId,
                UnitPrice: item.UnitPrice,
                Units: item.Units,
                OrderedCatalogItemId: item.ItemOrdered.CatalogItemId,
                OrderedProductName: item.ItemOrdered.ProductName,
                OrderedPictureUri: item.ItemOrdered.PictureUri);
        }

        return orderId;
    }

    private static Domain.Order ToDomain(Order row, List<OrderItemRow> itemRows)
    {
        var address = new Domain.Address(row.ShipToStreet, row.ShipToCity, row.ShipToState, row.ShipToCountry, row.ShipToZipcode);
        var items = itemRows.Select(i => new Domain.OrderItem(
            new Domain.CatalogItemOrdered(i.OrderedCatalogItemId, i.OrderedProductName, i.OrderedPictureUri),
            i.UnitPrice,
            i.Units)).ToList();
        return new Domain.Order(row.Id, row.BuyerId, new DateTimeOffset(row.OrderDate, TimeSpan.Zero), address, items);
    }
}
