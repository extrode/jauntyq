using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class OrdersTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public OrdersTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns830Orders()
    {
        var results = _fixture.Db.Orders.GetAll();
        Assert.Equal(830, results.Count);
    }

    [Fact]
    public void GetById_Returns10248()
    {
        var results = _fixture.Db.Orders.GetById(10248);
        Assert.Single(results);
        Assert.Equal(10248, results[0].OrderId);
    }

    [Fact]
    public void GetByCustomer_VINET_ReturnsOrders()
    {
        var results = _fixture.Db.Orders.GetByCustomer("VINET");
        Assert.NotEmpty(results);
    }

    [Fact]
    public void GetByEmployee_ReturnsOrders()
    {
        var results = _fixture.Db.Orders.GetByEmployee(1);
        Assert.NotEmpty(results);
    }
}
