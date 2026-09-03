using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class CustomersTests
{
    private readonly NorthwindFixture _fixture;
    public CustomersTests(NorthwindFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void GetAll_Returns91Customers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Customers.GetAll();
        Assert.Equal(91, results.Count);
    }

    [SkippableFact]
    public void GetById_ReturnsAlfki()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var customer = _fixture.Db.Customers.GetById("ALFKI");
        Assert.NotNull(customer);
        Assert.Equal("Alfreds Futterkiste", customer.CompanyName);
    }

    [SkippableFact]
    public void GetByCity_London_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Customers.GetByCity("London");
        Assert.NotEmpty(results);
        Assert.All(results, c => Assert.Equal("London", c.City));
    }

    [SkippableFact]
    public void GetByCity_NonExistent_ReturnsEmpty()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Customers.GetByCity("Atlantis");
        Assert.Empty(results);
    }
}
