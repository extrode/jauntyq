using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class EmployeeTerritoriesTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public EmployeeTerritoriesTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetByEmployee_ReturnsTerritoriesForEmployee1()
    {
        var results = _fixture.Db.EmployeeTerritories.GetByEmployee(1);
        Assert.NotEmpty(results);
        Assert.All(results, et => Assert.NotNull(et.TerritoryDescription));
    }
}
