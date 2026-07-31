using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class TerritoriesTests
{
    private readonly NorthwindFixture _fixture;
    public TerritoriesTests(NorthwindFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void GetAll_Returns53Territories()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Territories.GetAll();
        Assert.Equal(53, results.Count);
        Assert.All(results, t => Assert.NotNull(t.RegionDescription));
    }

    [SkippableFact]
    public void GetByRegion_Region1_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Territories.GetByRegion(new short[] { 1 });
        Assert.NotEmpty(results);
    }
}
