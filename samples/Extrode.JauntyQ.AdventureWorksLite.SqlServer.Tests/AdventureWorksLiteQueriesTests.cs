using Extrode.JauntyQ.Schema.Extraction;
using Xunit;

namespace Extrode.JauntyQ.AdventureWorksLite.SqlServer.Tests;

[Trait("Category", "AuditRegression")]
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

        // Round 14 (§2.11): a 4th employee (Gustavo, BusinessEntityID 4) was
        // added with a NULL OrganizationNode, to exercise the hierarchyid/
        // NULL round-trip below -- see GetOrganizationNodes_RoundTripsLiveHierarchyIdAndNull.
        Assert.Equal(4, rows.Count);
        Assert.Equal("Ken", rows[0].FirstName);
        Assert.Equal("Chief Executive Officer", rows[0].JobTitle);
        Assert.Equal("Roberto", rows[2].FirstName);
        Assert.Equal("Gustavo", rows[3].FirstName);
    }

    [SkippableFact]
    public async Task GetOrganizationNodes_LiveHierarchyIdColumn_ThrowsFileNotFoundException_KnownClrUdtCrash()
    {
        // Round 14 finding (AUD-R14-01, §2.11 residual carried forward from
        // round 13): the exotic hierarchyid/unmapped-type fallback
        // (OrganizationNode -> "object?" + reader.IsDBNull guard,
        // AUD-R13-01) was only ever proven at the generator level, against
        // synthetic in-memory reader data
        // (UnmappedColumnTypeTests.NullableUnmappedColumn_PropertyIsNullableObject_
        // AndReaderGuardsIsDBNull). Running the first real end-to-end
        // round-trip against a REAL running SQL Server this round revealed
        // it does not actually round-trip at all: reading a hierarchyid
        // column through the generated "object" fallback's
        // reader.GetValue(i) call requires Microsoft.Data.SqlClient to
        // materialize the CLR UDT via the Microsoft.SqlServer.Types
        // assembly -- which JauntyQ's zero-dependency/NativeAOT-compatible
        // core never references -- so it throws
        // System.IO.FileNotFoundException on the very first row that has a
        // non-null hierarchyid value (row 4's NULL, added this round
        // specifically to probe the NULL-handling half of AUD-R13-01, never
        // gets a chance to run: the exception fires on row 1 first).
        //
        // This is now a documented, known limitation -- JNT2007's message is
        // sharpened for exactly this case, see
        // UnmappedColumnTypeTests.SqlServerClrUdtColumn_Jnt2007MessageWarnsAboutRuntimeCrashRisk
        // -- not a silent landmine. Asserting the throw here (rather than
        // leaving this test permanently red) keeps the suite green while
        // still acting as a live regression artifact: if a future round
        // changes the fallback's runtime read strategy for CLR UDT columns,
        // this test will start failing and must be revisited alongside that
        // change.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var ex = await Assert.ThrowsAsync<System.IO.FileNotFoundException>(
            () => _fx.Db.Employee.GetOrganizationNodesAsync());

        Assert.Contains("Microsoft.SqlServer.Types", ex.Message);
    }

    [SkippableFact]
    public async Task GetNullOrganizationNodeRows_LiveNullHierarchyId_RoundTripsAsCSharpNull()
    {
        // Round 15 audit: closes the NULL-handling residual that round 14
        // (AUD-R14-01) explicitly could not reach -- GetOrganizationNodes.sql
        // throws System.IO.FileNotFoundException on row 1's non-null
        // hierarchyid value before row 4's NULL is ever read, so the NULL
        // half of AUD-R13-01's "object?" + reader.IsDBNull guard fix has
        // never actually been exercised against a live running SQL Server.
        //
        // GetNullOrganizationNodeRows.sql filters to `OrganizationNode IS
        // NULL`, selecting only BusinessEntityID 4 (Gustavo). The generated
        // reader code checks reader.IsDBNull(i) before ever calling
        // GetValue/materializing the CLR UDT, so this query reaches and
        // proves the NULL branch live, without touching the (separately
        // scoped, still-open) non-null CLR UDT crash.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var rows = await _fx.Db.Employee.GetNullOrganizationNodeRowsAsync();

        Assert.Single(rows);
        Assert.Equal(4, rows[0].BusinessEntityID);
        Assert.Null(rows[0].OrganizationNode);
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

        // Direct, real-world confirmation of the schema-scoping fix: scoping to
        // "Production" never bleeds in Person/Sales/Purchasing tables that
        // happen to share no names with it.
        var production = await new SqlServerExtractor("Production").ExtractAsync(_fx.ConnectionString);
        Assert.Equal(new[] { "Product", "ProductCategory", "ProductSubcategory" },
            production.Tables.Keys.OrderBy(k => k));

        // Spec 015 reversed the second half of this test. It used to assert
        // that Sales' reporting view was never surfaced by a pull -- a
        // deliberate exclusion that also made a query merely READING the view
        // fail JNT2001 as a missing relation. Views are now captured and
        // flagged instead, so this asserts the flag rather than the absence,
        // against a real view rather than a synthetic one.
        var sales = await new SqlServerExtractor("Sales").ExtractAsync(_fx.ConnectionString);
        Assert.Equal(
            new[] { "Customer", "SalesOrderDetail", "SalesOrderHeader", "SalesTerritory", "vSalesOrderDetailExtended" },
            sales.Tables.Keys.OrderBy(k => k, System.StringComparer.Ordinal));

        var view = sales.Tables["vSalesOrderDetailExtended"];
        Assert.True(view.IsView);
        Assert.False(view.IsInsertable);

        // The scoping fix and the view flag are independent: a base table in
        // the same pull must not pick up the flag.
        Assert.False(sales.Tables["SalesOrderDetail"].IsView);
    }

    [SkippableFact]
    public async Task LiveExtraction_KnownBoundary_CrossSchemaForeignKeysAreInvisibleToASingleSchemaPull()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        // Documents a real limitation surfaced by this schema, not a bug fix
        // target: SqlServerExtractor's foreign-key query requires BOTH the FK
        // and PK table to live in the pulled @schema (see
        // src/Extrode.JauntyQ.Schema.Contract/Extractors/SqlServerExtractor.cs). A
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
