using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class SuppliersTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public SuppliersTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns29Suppliers()
    {
        var results = _fixture.Db.Suppliers.GetAll();
        Assert.Equal(29, results.Count);
    }

    [Fact]
    public void GetById_ReturnsSupplier1()
    {
        var results = _fixture.Db.Suppliers.GetById(1);
        Assert.Single(results);
        Assert.NotNull(results[0].CompanyName);
    }
}
