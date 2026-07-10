using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class OrdersTests
{
    private readonly NorthwindFixture _fixture;
    public OrdersTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns830Orders()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Orders.GetAll();
        Assert.Equal(830, results.Count);
    }

    [Fact]
    public void GetById_Returns10248()
    {
        if (!_fixture.Available) return;
        var order = _fixture.Db.Orders.GetById(10248);
        Assert.NotNull(order);
        Assert.Equal(10248, order.OrderId);
    }

    [Fact]
    public void GetByCustomer_VINET_ReturnsOrders()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Orders.GetByCustomer("VINET");
        Assert.NotEmpty(results);
    }

    [Fact]
    public void GetByEmployee_ReturnsOrders()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Orders.GetByEmployee(1);
        Assert.NotEmpty(results);
    }
}
