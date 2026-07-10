using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class RegionTests
{
    private readonly NorthwindFixture _fixture;
    public RegionTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns4Regions()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Region.GetAll();
        Assert.Equal(4, results.Count);
    }
}
