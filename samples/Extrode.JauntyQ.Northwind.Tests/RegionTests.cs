using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class RegionTests
{
    private readonly NorthwindFixture _fixture;
    public RegionTests(NorthwindFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void GetAll_Returns4Regions()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Region.GetAll();
        Assert.Equal(4, results.Count);
    }
}
