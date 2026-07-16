using JauntyQ.Schema.Contract.Extractors;
using Xunit;

namespace JauntyQ.AdventureWorksLite.SqlServer.Tests;

public class AdventureWorksLiteQueriesTests : IClassFixture<AdventureWorksLiteSqlServerFixture>
{
    private readonly AdventureWorksLiteSqlServerFixture _fx;
    public AdventureWorksLiteQueriesTests(AdventureWorksLiteSqlServerFixture fixture) => _fx = fixture;

    [SkippableFact]
    public async Task Person_GetById_ReturnsRow()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var row = await _fx.Db.Person.GetByIdAsync(1);

        Assert.NotNull(row);
        Assert.Equal("Ken", row!.FirstName);
        Assert.Equal("Sanchez", row.LastName);
    }

    [SkippableFact]
    public async Task Person_GetWithEmail_JoinsWithinSchema()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var row = await _fx.Db.Person.GetWithEmailAsync(1);

        Assert.NotNull(row);
        Assert.Equal("ken0@adventure-works.com", row!.EmailAddress);
    }

    [SkippableFact]
    public async Task Employee_GetWithPersonName_ResolvesCrossSchemaJoin()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = await _fx.Db.Employee.GetWithPersonNameAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal("Ken", rows[0].FirstName);
        Assert.Equal("Chief Executive Officer", rows[0].JobTitle);
        Assert.Equal("Roberto", rows[2].FirstName);
    }

    [SkippableFact]
    public async Task Product_GetBySubcategory_ReturnsMountainBikes()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = await _fx.Db.Product.GetBySubcategoryAsync(1);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Name == "Mountain-100 Black, 42");
        Assert.Contains(rows, r => r.Name == "Mountain-200 Silver, 38");
    }

    [SkippableFact]
    public async Task Product_GetWithCategory_JoinsThreeProductionTables()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = await _fx.Db.Product.GetWithCategoryAsync();

        Assert.Equal(8, rows.Count);
        var mtn100 = Assert.Single(rows, r => r.Name == "Mountain-100 Black, 42");
        Assert.Equal("Mountain Bikes", mtn100.SubcategoryName);
        Assert.Equal("Bikes", mtn100.CategoryName);
    }

    [SkippableFact]
    public async Task ProductVendor_GetVendorsForProduct_ResolvesCrossSchemaCompositeKeyJunction()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = await _fx.Db.ProductVendor.GetVendorsForProductAsync(1);

        var only = Assert.Single(rows);
        Assert.Equal("Litware, Inc.", only.VendorName);
        Assert.Equal(1735.20m, only.StandardPrice);
    }

    [SkippableFact]
    public async Task Customer_GetWithAccountNumber_ReadsComputedColumn()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var row = await _fx.Db.Customer.GetWithAccountNumberAsync(1);

        Assert.NotNull(row);
        Assert.Equal("AW0000001", row!.AccountNumber);
        Assert.Equal(4, row.PersonID);
    }

    [SkippableFact]
    public async Task Customer_GetByTerritory_JoinsSalesTerritory()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = await _fx.Db.Customer.GetByTerritoryAsync(1);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("Northwest", r.TerritoryName));
    }

    [SkippableFact]
    public async Task SalesOrderHeader_GetWithTotalDue_ReadsComputedColumn()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var row = await _fx.Db.SalesOrderHeader.GetWithTotalDueAsync(1);

        Assert.NotNull(row);
        Assert.Equal(3729.35m, row!.TotalDue);
    }

    [SkippableFact]
    public async Task SalesOrderHeader_GetByCustomer_ReturnsBothOrders()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = await _fx.Db.SalesOrderHeader.GetByCustomerAsync(1);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.SalesOrderID == 1 && r.TotalDue == 3729.35m);
        Assert.Contains(rows, r => r.SalesOrderID == 4 && r.TotalDue == 172.99m);
    }

    [SkippableFact]
    public async Task SalesOrderDetail_GetByOrder_JoinsProductAcrossSchemas()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = await _fx.Db.SalesOrderDetail.GetByOrderAsync(4);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ProductName == "HL Mountain Handlebars" && r.LineTotal == 120.27m);
        Assert.Contains(rows, r => r.ProductName == "LL Road Handlebars" && r.LineTotal == 36.29m);
    }

    [SkippableFact]
    public async Task SalesOrderDetail_GetTopSellingProducts_AggregatesComputedColumnAcrossSchemas()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = await _fx.Db.SalesOrderDetail.GetTopSellingProductsAsync();

        Assert.Equal(8, rows.Count);
        Assert.Equal("Road-150 Red, 44", rows[0].ProductName);
        Assert.Equal(3578.27m, rows[0].Revenue);
        // Strictly descending — confirms ORDER BY on the aggregate, not insertion order.
        for (int i = 1; i < rows.Count; i++)
            Assert.True(rows[i - 1].Revenue >= rows[i].Revenue);
    }

    [SkippableFact]
    public async Task SalesOrderDetailExtended_GetByOrder_ReadsFromRealView()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = await _fx.Db.SalesOrderDetailExtended.GetByOrderAsync(6);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ProductName == "Long-Sleeve Logo Jersey, M" && r.LineTotal == 99.98m);
        Assert.Contains(rows, r => r.ProductName == "Classic Vest, S" && r.LineTotal == 57.15m);
    }

    [SkippableFact]
    public async Task LiveExtraction_SchemaScopedPull_OnlyReturnsThatSchemasBaseTables()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        // Direct, real-world confirmation of both cross-schema fixes at once:
        // (1) scoping to "Production" never bleeds in Person/Sales/Purchasing
        // tables that happen to share no names with it, and (2) the reporting
        // view living in Sales is never surfaced as a table when Sales itself
        // is pulled (the just-fixed VIEW-as-table bug, now proven against a
        // real view rather than the synthetic jq_gizmo_view regression test).
        var production = await new SqlServerExtractor("Production").ExtractAsync(_fx.ConnectionString);
        Assert.Equal(new[] { "Product", "ProductCategory", "ProductSubcategory" },
            production.Tables.Keys.OrderBy(k => k));

        var sales = await new SqlServerExtractor("Sales").ExtractAsync(_fx.ConnectionString);
        Assert.Equal(new[] { "Customer", "SalesOrderDetail", "SalesOrderHeader", "SalesTerritory" },
            sales.Tables.Keys.OrderBy(k => k));
        Assert.DoesNotContain("vSalesOrderDetailExtended", sales.Tables.Keys);
    }

    [SkippableFact]
    public async Task LiveExtraction_KnownBoundary_CrossSchemaForeignKeysAreInvisibleToASingleSchemaPull()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        // Documents a real limitation surfaced by this schema, not a bug fix
        // target: SqlServerExtractor's foreign-key query requires BOTH the FK
        // and PK table to live in the pulled @schema (see
        // src/JauntyQ.Schema.Contract/Extractors/SqlServerExtractor.cs). A
        // cross-schema FK — Purchasing.ProductVendor.ProductID referencing
        // Production.Product — is therefore invisible no matter which single
        // schema is pulled, since no one schema contains both ends. A
        // multi-schema pull mode would need to relax this to "either side."
        var purchasing = await new SqlServerExtractor("Purchasing").ExtractAsync(_fx.ConnectionString);
        Assert.DoesNotContain(purchasing.ForeignKeys, fk => fk.FromTable == "ProductVendor" && fk.ToTable == "Product");

        var production = await new SqlServerExtractor("Production").ExtractAsync(_fx.ConnectionString);
        Assert.DoesNotContain(production.ForeignKeys, fk => fk.FromTable == "ProductVendor" && fk.ToTable == "Product");

        // The FK to Vendor (same schema as ProductVendor) IS captured.
        Assert.Contains(purchasing.ForeignKeys, fk => fk.FromTable == "ProductVendor" && fk.ToTable == "Vendor");
    }
}
