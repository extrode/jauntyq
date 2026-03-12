using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class ProductsTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public ProductsTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns77Products()
    {
        var results = _fixture.Db.Products.GetAll();
        Assert.Equal(77, results.Count);
    }

    [Fact]
    public void GetById_ReturnsChai()
    {
        var results = _fixture.Db.Products.GetById(1);
        Assert.Single(results);
        Assert.Equal("Chai", results[0].ProductName);
    }

    [Fact]
    public void GetByCategory_Beverages_ReturnsProducts()
    {
        var results = _fixture.Db.Products.GetByCategory(1);
        Assert.NotEmpty(results);
        Assert.All(results, p => Assert.NotNull(p.CategoryName));
    }

    [Fact]
    public void GetBySupplier_ReturnsProducts()
    {
        var results = _fixture.Db.Products.GetBySupplier(1);
        Assert.NotEmpty(results);
        Assert.All(results, p => Assert.NotNull(p.SupplierName));
    }

    [Fact]
    public void GetById_NonExistent_ReturnsEmpty()
    {
        var results = _fixture.Db.Products.GetById(9999);
        Assert.Empty(results);
    }
}
