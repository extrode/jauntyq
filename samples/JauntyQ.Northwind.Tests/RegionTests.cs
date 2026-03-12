using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class RegionTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public RegionTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns4Regions()
    {
        var results = _fixture.Db.Region.GetAll();
        Assert.Equal(4, results.Count);
    }
}
