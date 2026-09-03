using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class ShippersTests
{
    private readonly NorthwindFixture _fixture;
    public ShippersTests(NorthwindFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void GetAll_Returns3Shippers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Shippers.GetAll();
        Assert.Equal(3, results.Count);
    }
}
