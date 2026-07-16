using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class EmployeeTerritoriesTests
{
    private readonly NorthwindFixture _fixture;
    public EmployeeTerritoriesTests(NorthwindFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void GetByEmployee_ReturnsTerritoriesForEmployee1()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.EmployeeTerritories.GetByEmployee(1);
        Assert.NotEmpty(results);
        Assert.All(results, et => Assert.NotNull(et.TerritoryDescription));
    }
}
