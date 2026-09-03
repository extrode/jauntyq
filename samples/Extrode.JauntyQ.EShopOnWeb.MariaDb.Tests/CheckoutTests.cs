using System;
using System.Collections.Generic;
using Xunit;
using Microsoft.eShopWeb.MariaDb.Tests.Domain;
using Microsoft.eShopWeb.MariaDb.Tests.Repositories;

namespace Microsoft.eShopWeb.MariaDb.Tests;

/// <summary>
/// Covers the checkout multi-table transaction stress point: happy-path
/// checkout, Address/CatalogItemOrdered round-trip, snapshot-immunity
/// (order items must not change if the catalog item they reference is
/// edited later), and rollback under a forced mid-transaction failure.
/// </summary>
[Collection("EShopOnWebMariaDb")]
public class CheckoutTests
{
    private readonly EShopOnWebMariaDbFixture _fx;

    public CheckoutTests(EShopOnWebMariaDbFixture fx) => _fx = fx;

    private int SeedBasketWithItem(string buyerId, int catalogItemId, decimal unitPrice, int quantity)
    {
        var basketId = _fx.Db.Basket.Insert(buyerId);
        _fx.Db.BasketItem.Insert(basketId, catalogItemId, unitPrice, quantity);
        return basketId;
    }

    private int CountRows(string table)
    {
        using var cmd = _fx.Db.Connection.CreateCommand();
        cmd.CommandText = $"select count(*) from {table}";
        return (int)(long)cmd.ExecuteScalar()!;
    }

    [SkippableFact]
    public void CreateOrder_HappyPath_RoundTripsAddressAndItems()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var basketId = SeedBasketWithItem("buyer-checkout-1", catalogItemId: 1, unitPrice: 19.5m, quantity: 3);
        var address = new Address("1 Microsoft Way", "Redmond", "WA", "USA", "98052");
        var checkout = new CheckoutService(_fx.Db);

        var orderId = checkout.CreateOrder(basketId, "buyer-checkout-1", address);

        var orderRepository = new OrderRepository(_fx.Db);
        var order = orderRepository.GetById(orderId);
        Assert.NotNull(order);
        Assert.Equal("buyer-checkout-1", order!.BuyerId);
        Assert.Equal("1 Microsoft Way", order.ShipToAddress.Street);
        Assert.Equal("98052", order.ShipToAddress.ZipCode);
        Assert.Single(order.OrderItems);
        Assert.Equal(".NET Bot Black Sweatshirt", order.OrderItems[0].ItemOrdered.ProductName);
        Assert.Equal(3, order.OrderItems[0].Units);

        // Fidelity check: the source app's OrderService does not delete the
        // basket after checkout - confirm the port doesn't add that behavior.
        Assert.NotNull(_fx.Db.Basket.GetById(basketId));
    }

    [SkippableFact]
    public void CreateOrder_SnapshotImmune_ToLaterCatalogItemEdits()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var basketId = SeedBasketWithItem("buyer-checkout-2", catalogItemId: 2, unitPrice: 8.5m, quantity: 1);
        var address = new Address("2 Microsoft Way", "Redmond", "WA", "USA", "98052");
        var checkout = new CheckoutService(_fx.Db);

        var orderId = checkout.CreateOrder(basketId, "buyer-checkout-2", address);

        using (var cmd = _fx.Db.Connection.CreateCommand())
        {
            cmd.CommandText = "update catalog_item set name = 'Renamed After Checkout' where id = 2";
            cmd.ExecuteNonQuery();
        }

        var orderRepository = new OrderRepository(_fx.Db);
        var order = orderRepository.GetById(orderId);
        Assert.NotNull(order);
        Assert.Equal(".NET Black & White Mug", order!.OrderItems[0].ItemOrdered.ProductName);
    }

    [SkippableFact]
    public void CreateOrder_RollsBackFully_OnConstraintViolationMidInsert()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var ordersBefore = CountRows("orders");
        var orderItemsBefore = CountRows("order_item");

        var address = new Address("3 Microsoft Way", "Redmond", "WA", "USA", "98052");
        // Second item carries a null product name: NOT NULL violates on
        // ordered_product_name partway through the multi-row insert (the
        // order row and first order-item row would already be inserted).
        var orderItems = new List<Domain.OrderItem>
        {
            new Domain.OrderItem(new Domain.CatalogItemOrdered(1, ".NET Black & White Mug", "uri"), 8.5m, 1),
            new Domain.OrderItem(new Domain.CatalogItemOrdered(2, null!, "uri"), 19.5m, 1),
        };
        var order = new Domain.Order(0, "buyer-rollback", DateTimeOffset.UtcNow, address, orderItems);
        var orderRepository = new OrderRepository(_fx.Db);

        using (var tx = _fx.Db.BeginTransaction())
        {
            Assert.ThrowsAny<Exception>(() => orderRepository.Add(order));
            // tx goes out of scope without Commit() -> implicit rollback
        }

        Assert.Equal(ordersBefore, CountRows("orders"));
        Assert.Equal(orderItemsBefore, CountRows("order_item"));
    }
}
