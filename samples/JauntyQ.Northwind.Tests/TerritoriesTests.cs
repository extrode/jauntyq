using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class TerritoriesTests
{
    private readonly NorthwindFixture _fixture;
    public TerritoriesTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns53Territories()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Territories.GetAll();
        Assert.Equal(53, results.Count);
        Assert.All(results, t => Assert.NotNull(t.RegionDescription));
    }

    [Fact]
    public void GetByRegion_Region1_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Territories.GetByRegion(new short[] { 1 });
        Assert.NotEmpty(results);
    }
}
