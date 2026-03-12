using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class TerritoriesTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public TerritoriesTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns53Territories()
    {
        var results = _fixture.Db.Territories.GetAll();
        Assert.Equal(53, results.Count);
        Assert.All(results, t => Assert.NotNull(t.RegionDescription));
    }

    [Fact]
    public void GetByRegion_Region1_ReturnsResults()
    {
        var results = _fixture.Db.Territories.GetByRegion(1);
        Assert.NotEmpty(results);
    }
}
