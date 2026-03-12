using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class CategoriesTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public CategoriesTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns8Categories()
    {
        var results = _fixture.Db.Categories.GetAll();
        Assert.Equal(8, results.Count);
    }

    [Fact]
    public void GetById_ReturnsBeverages()
    {
        var results = _fixture.Db.Categories.GetById(1);
        Assert.Single(results);
        Assert.Equal("Beverages", results[0].CategoryName);
    }

    [Fact]
    public void GetById_NonExistent_ReturnsEmpty()
    {
        var results = _fixture.Db.Categories.GetById(999);
        Assert.Empty(results);
    }
}
