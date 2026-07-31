using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class OrdersTests
{
    private readonly NorthwindFixture _fixture;
    public OrdersTests(NorthwindFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void GetAll_Returns830Orders()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Orders.GetAll();
        Assert.Equal(830, results.Count);
    }

    [SkippableFact]
    public void GetById_Returns10248()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var order = _fixture.Db.Orders.GetById(10248);
        Assert.NotNull(order);
        Assert.Equal(10248, order.OrderId);
    }

    [SkippableFact]
    public void GetByCustomer_VINET_ReturnsOrders()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Orders.GetByCustomer(new[] { "VINET" });
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void GetByEmployee_ReturnsOrders()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Orders.GetByEmployee(new short?[] { 1 });
        Assert.NotEmpty(results);
    }
}
