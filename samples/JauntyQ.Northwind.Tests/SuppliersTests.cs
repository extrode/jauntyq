using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class SuppliersTests
{
    private readonly NorthwindFixture _fixture;
    public SuppliersTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns29Suppliers()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Suppliers.GetAll();
        Assert.Equal(29, results.Count);
    }

    [Fact]
    public void GetById_ReturnsSupplier1()
    {
        if (!_fixture.Available) return;
        var supplier = _fixture.Db.Suppliers.GetById(1);
        Assert.NotNull(supplier);
        Assert.NotNull(supplier.CompanyName);
    }
}
