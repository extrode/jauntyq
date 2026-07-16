using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class ViewTests
{
    private readonly NorthwindFixture _fixture;
    public ViewTests(NorthwindFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public void AlphabeticalListOfProducts_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.AlphabeticalListOfProducts.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void CurrentProductList_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.CurrentProductList.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void CustomerAndSuppliersByCity_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.CustomerAndSuppliersByCity.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void OrderDetailsExtended_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.OrderDetailsExtended.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void OrderSubtotals_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.OrderSubtotals.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void OrdersQry_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.OrdersQry.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void ProductsAboveAveragePrice_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.ProductsAboveAveragePrice.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void ProductsByCategory_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.ProductsByCategory.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void QuarterlyOrders_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.QuarterlyOrders.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void SalesByCategory_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.SalesByCategory.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void SalesTotalsByAmount_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.SalesTotalsByAmount.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void CategorySalesFor1997_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.CategorySalesFor1997.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void ProductSalesFor1997_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.ProductSalesFor1997.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void SummaryOfSalesByQuarter_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.SummaryOfSalesByQuarter.GetAll();
        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void SummaryOfSalesByYear_ReturnsResults()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var results = _fixture.Db.SummaryOfSalesByYear.GetAll();
        Assert.NotEmpty(results);
    }
}
