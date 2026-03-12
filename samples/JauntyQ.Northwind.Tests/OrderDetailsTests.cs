using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class OrderDetailsTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public OrderDetailsTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetByOrder_10248_ReturnsDetails()
    {
        var results = _fixture.Db.OrderDetails.GetByOrder(10248);
        Assert.NotEmpty(results);
        Assert.All(results, d => Assert.Equal(10248, d.OrderId));
        Assert.All(results, d => Assert.NotNull(d.ProductName));
    }
}
