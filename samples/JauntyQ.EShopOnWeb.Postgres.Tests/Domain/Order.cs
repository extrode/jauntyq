using System;
using System.Collections.Generic;

namespace JauntyQ.EShopOnWeb.Postgres.Tests.Domain;

public sealed class Order
{
    public int Id { get; }
    public string BuyerId { get; }
    public DateTimeOffset OrderDate { get; }
    public Address ShipToAddress { get; }
    public IReadOnlyList<OrderItem> OrderItems { get; }

    public Order(int id, string buyerId, DateTimeOffset orderDate, Address shipToAddress, IReadOnlyList<OrderItem> orderItems)
    {
        Id = id;
        BuyerId = buyerId;
        OrderDate = orderDate;
        ShipToAddress = shipToAddress;
        OrderItems = orderItems;
    }
}
