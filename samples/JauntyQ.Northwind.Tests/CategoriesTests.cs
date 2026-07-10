using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class CategoriesTests
{
    private readonly NorthwindFixture _fixture;
    public CategoriesTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns8Categories()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Categories.GetAll();
        Assert.Equal(8, results.Count);
    }

    [Fact]
    public void GetById_ReturnsBeverages()
    {
        if (!_fixture.Available) return;
        var category = _fixture.Db.Categories.GetById(1);
        Assert.NotNull(category);
        Assert.Equal("Beverages", category.CategoryName);
    }

    [Fact]
    public void GetById_NonExistent_ReturnsEmpty()
    {
        if (!_fixture.Available) return;
        var category = _fixture.Db.Categories.GetById(999);
        Assert.Null(category);
    }
}
