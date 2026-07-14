using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class EmployeeTerritoriesTests
{
    private readonly NorthwindFixture _fixture;
    public EmployeeTerritoriesTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetByEmployee_ReturnsTerritoriesForEmployee1()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.EmployeeTerritories.GetByEmployee(new short[] { 1 });
        Assert.NotEmpty(results);
        Assert.All(results, et => Assert.NotNull(et.TerritoryDescription));
    }
}
