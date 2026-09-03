using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class CategoriesTests
{
    private readonly NorthwindFixture _fixture;
    public CategoriesTests(NorthwindFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void GetAll_Returns8Categories()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.Categories.GetAll();
        Assert.Equal(8, results.Count);
    }

    [SkippableFact]
    public void GetById_ReturnsBeverages()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var category = _fixture.Db.Categories.GetById(1);
        Assert.NotNull(category);
        Assert.Equal("Beverages", category.CategoryName);
    }

    [SkippableFact]
    public void GetById_NonExistent_ReturnsEmpty()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var category = _fixture.Db.Categories.GetById(999);
        Assert.Null(category);
    }
}
