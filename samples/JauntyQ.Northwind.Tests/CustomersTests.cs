using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class CustomersTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public CustomersTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns91Customers()
    {
        var results = _fixture.Db.Customers.GetAll();
        Assert.Equal(91, results.Count);
    }

    [Fact]
    public void GetById_ReturnsAlfki()
    {
        var results = _fixture.Db.Customers.GetById("ALFKI");
        Assert.Single(results);
        Assert.Equal("Alfreds Futterkiste", results[0].CompanyName);
    }

    [Fact]
    public void GetByCity_London_ReturnsResults()
    {
        var results = _fixture.Db.Customers.GetByCity("London");
        Assert.NotEmpty(results);
        Assert.All(results, c => Assert.Equal("London", c.City));
    }

    [Fact]
    public void GetByCity_NonExistent_ReturnsEmpty()
    {
        var results = _fixture.Db.Customers.GetByCity("Atlantis");
        Assert.Empty(results);
    }
}
