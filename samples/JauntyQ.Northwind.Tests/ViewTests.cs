using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class ViewTests
{
    private readonly NorthwindFixture _fixture;
    public ViewTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void AlphabeticalListOfProducts_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.AlphabeticalListOfProducts.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void CurrentProductList_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.CurrentProductList.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void CustomerAndSuppliersByCity_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.CustomerAndSuppliersByCity.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void OrderDetailsExtended_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.OrderDetailsExtended.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void OrderSubtotals_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.OrderSubtotals.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void OrdersQry_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.OrdersQry.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void ProductsAboveAveragePrice_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.ProductsAboveAveragePrice.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void ProductsByCategory_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.ProductsByCategory.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void QuarterlyOrders_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.QuarterlyOrders.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void SalesByCategory_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.SalesByCategory.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void SalesTotalsByAmount_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.SalesTotalsByAmount.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void CategorySalesFor1997_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.CategorySalesFor1997.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void ProductSalesFor1997_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.ProductSalesFor1997.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void SummaryOfSalesByQuarter_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.SummaryOfSalesByQuarter.GetAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void SummaryOfSalesByYear_ReturnsResults()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.SummaryOfSalesByYear.GetAll();
        Assert.NotEmpty(results);
    }
}
