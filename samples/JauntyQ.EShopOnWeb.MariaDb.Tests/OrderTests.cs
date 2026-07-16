using System;
using Xunit;

namespace JauntyQ.EShopOnWeb.MariaDb.Tests;

/// <summary>
/// Covers CustomerOrdersSpecification / CustomerOrdersWithItemsSpecification
/// (6/7, which collapse to one query shape once Address/CatalogItemOrdered
/// are flattened - see the test log) and OrderWithItemsByIdSpec
/// (8). The Order/OrderItem write path belongs to a later milestone
/// (checkout transaction), so this fixture seeds rows directly via raw
/// ADO.NET over JauntyDb.Connection rather than fronting it with
/// generated Insert calls.
/// </summary>
public class OrderTests : IClassFixture<EShopOnWebMariaDbFixture>
{
    private readonly EShopOnWebMariaDbFixture _fx;

    public OrderTests(EShopOnWebMariaDbFixture fx) => _fx = fx;

    private int SeedOrder(string buyerId, DateTimeOffset orderDate)
    {
        using (var cmd = _fx.Db.Connection.CreateCommand())
        {
            cmd.CommandText = @"
                insert into orders (buyer_id, order_date, ship_to_street, ship_to_city, ship_to_state, ship_to_country, ship_to_zipcode)
                values (@buyerId, @orderDate, '123 Main St', 'Redmond', 'WA', 'USA', '98052')";
            AddParam(cmd, "@buyerId", buyerId);
            AddParam(cmd, "@orderDate", orderDate.UtcDateTime);
            cmd.ExecuteNonQuery();
        }
        using var idCmd = _fx.Db.Connection.CreateCommand();
        idCmd.CommandText = "select last_insert_id()";
        return (int)(ulong)idCmd.ExecuteScalar()!;
    }

    private void SeedOrderItem(int orderId, decimal unitPrice, int units, int catalogItemId, string productName, string pictureUri)
    {
        using var cmd = _fx.Db.Connection.CreateCommand();
        cmd.CommandText = @"
            insert into order_item (order_id, unit_price, units, ordered_catalog_item_id, ordered_product_name, ordered_picture_uri)
            values (@orderId, @unitPrice, @units, @catalogItemId, @productName, @pictureUri)";
        AddParam(cmd, "@orderId", orderId);
        AddParam(cmd, "@unitPrice", unitPrice);
        AddParam(cmd, "@units", units);
        AddParam(cmd, "@catalogItemId", catalogItemId);
        AddParam(cmd, "@productName", productName);
        AddParam(cmd, "@pictureUri", pictureUri);
        cmd.ExecuteNonQuery();
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    // Specs 6/7: CustomerOrdersSpecification / CustomerOrdersWithItemsSpecification
    [SkippableFact]
    public void GetByBuyerId_ReturnsOrdersAndItems()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var buyerId = "buyer-6-7";
        var orderDate = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var orderId = SeedOrder(buyerId, orderDate);
        SeedOrderItem(orderId, 19.5m, 2, 2, ".NET Bot Black Sweatshirt", "http://example/1.png");
        SeedOrderItem(orderId, 8.5m, 1, 1, ".NET Black & White Mug", "http://example/2.png");

        var orders = _fx.Db.Orders.GetByBuyerId(buyerId);
        Assert.Single(orders);
        Assert.Equal(buyerId, orders[0].BuyerId);

        var items = _fx.Db.OrderItem.GetByOrderId(orders[0].Id);
        Assert.Equal(2, items.Count);
        Assert.Equal(".NET Bot Black Sweatshirt", items[0].OrderedProductName);
    }

    [SkippableFact]
    public void GetByBuyerId_NoOrders_ReturnsEmpty()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var orders = _fx.Db.Orders.GetByBuyerId("nonexistent-buyer");

        Assert.Empty(orders);
    }

    // Spec 8: OrderWithItemsByIdSpec
    [SkippableFact]
    public void GetById_ReturnsOrderAndItems()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var buyerId = "buyer-8";
        var orderDate = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var orderId = SeedOrder(buyerId, orderDate);
        SeedOrderItem(orderId, 6.0m, 3, 5, "Roslyn Red Sheet", "http://example/3.png");

        var order = _fx.Db.Orders.GetById(orderId);
        Assert.NotNull(order);
        Assert.Equal(buyerId, order!.BuyerId);
        Assert.Equal("123 Main St", order.ShipToStreet);

        var items = _fx.Db.OrderItem.GetByOrderId(orderId);
        Assert.Single(items);
        Assert.Equal("Roslyn Red Sheet", items[0].OrderedProductName);
    }

    [SkippableFact]
    public void GetById_NoMatch_ReturnsNull()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var order = _fx.Db.Orders.GetById(999999);

        Assert.Null(order);
    }
}
