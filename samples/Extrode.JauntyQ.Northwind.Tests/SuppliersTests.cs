using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class SuppliersTests
{
    private readonly NorthwindFixture _fixture;
    public SuppliersTests(NorthwindFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void GetAll_Returns29Suppliers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Suppliers.GetAll();
        Assert.Equal(29, results.Count);
    }

    [SkippableFact]
    public void GetById_ReturnsSupplier1()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var supplier = _fixture.Db.Suppliers.GetById(1);
        Assert.NotNull(supplier);
        Assert.NotNull(supplier.CompanyName);
    }
}
