using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class ShippersTests
{
    private readonly NorthwindFixture _fixture;
    public ShippersTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns3Shippers()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Shippers.GetAll();
        Assert.Equal(3, results.Count);
    }
}
