using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class OrderDetailsTests
{
    private readonly NorthwindFixture _fixture;
    public OrderDetailsTests(NorthwindFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void GetByOrder_10248_ReturnsDetails()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.OrderDetails.GetByOrder(new[] { 10248 });
        Assert.NotEmpty(results);
        Assert.All(results, d => Assert.Equal(10248, d.OrderId));
        Assert.All(results, d => Assert.NotNull(d.ProductName));
    }
}
