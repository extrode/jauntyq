using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class ShippersTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public ShippersTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns3Shippers()
    {
        var results = _fixture.Db.Shippers.GetAll();
        Assert.Equal(3, results.Count);
    }
}
